using System.Text.Json;
using Microsoft.Extensions.AI;
using Simorgh.AI.Abstractions.Agent;
using Simorgh.AI.Abstractions.Agent.Cancellation;
using Simorgh.AI.Abstractions.Agent.Middleware;
using Simorgh.AI.Abstractions.Contents;
using Simorgh.AI.Abstractions.Messages;
using Simorgh.AI.Abstractions.Tools;
using Simorgh.AI.Core.Tools;

namespace Simorgh.AI.Core.Agent;

/// <summary>
///     Handle for one submitted agent run; executes against an <see cref="IChatClient" />
///     through the middleware pipeline, raising progress updates while it runs.
///     Tool calling — which tools the model sees and how its calls are resolved — is
///     delegated to an <see cref="IToolCallingStrategy" />.
/// </summary>
internal sealed class AgentRun : IAgentRun
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly TaskCompletionSource<AiResponse> _completion = new(
        TaskCreationOptions.RunContinuationsAsynchronously);

    private AgentRun()
    {
    }

    /// <summary>
    ///     Id of the run; generated unless the caller supplied one through
    ///     <see cref="AgentRunRequest.RunId" />.
    /// </summary>
    public string RunId { get; private set; } = Guid.NewGuid().ToString("N");

    public Task<AiResponse> Completion => _completion.Task;

    public event Action<AgentUpdate>? Updated;

    private CancellationTokenSource? _cancellationSource;
    private volatile bool _cancelRequested;
    private string? _cancelReason;

    /// <summary>Creates the run and starts executing it in the background.</summary>
    /// <param name="subscribe">
    ///     Optional callback invoked synchronously before execution starts, with the run
    ///     handle itself, so update handlers can be attached before any update is raised.
    ///     This is the only way to guarantee a handler sees every update, including
    ///     <see cref="AgentRunStartedUpdate" />.
    /// </param>
    /// <exception cref="ArgumentException">
    ///     <paramref name="request" /> carries a run id that is empty or whitespace.
    /// </exception>
    public static IAgentRun Start(
        IChatClient chatClient,
        ToolRegistry? tools,
        IToolCallingStrategy toolCallingStrategy,
        AgentPipeline pipeline,
        string? instructions,
        AgentRunRequest request,
        CancellationToken cancellationToken,
        IAgentRunRegistry? runRegistry = null,
        Action<IAgentRun>? subscribe = null,
        long? contextWindowTokens = null)
    {
        if (request.RunId is not null)
        {
            if (string.IsNullOrWhiteSpace(request.RunId))
                throw new ArgumentException("The supplied run id must not be empty or whitespace.", nameof(request));
        }

        var run = new AgentRun();
        if (request.RunId is not null)
            run.RunId = request.RunId;
        // Register before the background execution starts so the run is cancellable as
        // soon as Submit returns.
        var registration = runRegistry?.Register(run.RunId, run.Cancel);
        // Wire up update handlers before execution starts so no update can be published
        // ahead of the consumer's subscription.
        subscribe?.Invoke(run);
        _ = run.ExecuteAsync(
            chatClient, tools, toolCallingStrategy, pipeline, instructions, request, cancellationToken, registration,
            contextWindowTokens);
        return run;
    }

    /// <summary>
    ///     Cancels the run. Streaming from the model and any in-flight tool calls are
    ///     aborted, and an <see cref="AgentRunCancelledUpdate" /> is raised. Idempotent.
    /// </summary>
    /// <param name="reason">Optional reason surfaced through <see cref="AgentRunCancelledUpdate" />.</param>
    internal void Cancel(string? reason = null)
    {
        _cancelRequested = true;
        _cancelReason = reason;
        _cancellationSource?.Cancel();
    }

    private async Task ExecuteAsync(
        IChatClient chatClient,
        ToolRegistry? tools,
        IToolCallingStrategy toolCallingStrategy,
        AgentPipeline pipeline,
        string? instructions,
        AgentRunRequest request,
        CancellationToken cancellationToken,
        IDisposable? registration,
        long? contextWindowTokens = null)
    {
        var messages = new List<AiMessage>();
        if (!string.IsNullOrWhiteSpace(instructions))
            messages.Add(AiMessage.FromText(AiMessageRole.System, instructions));
        messages.AddRange(request.Messages);

        using var cancellationSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _cancellationSource = cancellationSource;
        var runToken = cancellationSource.Token;
        var context = new AgentRunContext(RunId, messages, runToken);
        // Only a window the developer explicitly supplied is used; without it no
        // percentage is computed. OnStartupAsync middleware may still set or override it.
        context.ContextWindowTokens = contextWindowTokens;

        var availableTools = tools?.Tools
            .Select(t => new AiToolDeclaration(
                t.Identifier,
                t.Description,
                t.Declaration.JsonSchema.ValueKind == JsonValueKind.Undefined
                    ? null
                    : t.Declaration.JsonSchema.GetRawText()))
            .ToList() as IReadOnlyList<AiToolDeclaration> ?? [];

        Raise(new AgentRunStartedUpdate(RunId));
        try
        {
            await pipeline.OnStartupAsync(context, runToken).ConfigureAwait(false);

            AiResponse reply;
            var round = 0;
            while (true)
            {
                var declarations = await toolCallingStrategy.GetToolDeclarationsAsync(
                    availableTools, context, round, runToken).ConfigureAwait(false);

                await pipeline.OnBeforeAgentResponseAsync(context, runToken).ConfigureAwait(false);
                reply = await RunModelRoundAsync(
                    chatClient, declarations, context, request.Stream, pipeline, request.MaxOutputTokens)
                    .ConfigureAwait(false);
                if (reply.Usage is { } roundUsage)
                    context.ReportContextTokens(roundUsage.InputTokens);
                context.Messages.Add(reply.Message);

                var calls = reply.Message.Contents.OfType<AiToolCallContent>().ToList();
                if (calls.Count == 0) break;

                if (++round > toolCallingStrategy.MaxToolRounds)
                    throw new AgentRunException(
                        $"The run exceeded the maximum of {toolCallingStrategy.MaxToolRounds} tool rounds without a final answer.");

                Raise(new AgentMessageUpdate(RunId, reply.Message));

                var followUpMessages = await toolCallingStrategy.InvokeToolsAsync(
                    context, calls,
                    async (call, ct) =>
                    {
                        var effectiveCall = await pipeline.OnBeforeToolCallAsync(context, call, ct)
                            .ConfigureAwait(false);
                        var result = await ExecuteToolAsync(tools, context, effectiveCall)
                            .ConfigureAwait(false);
                        return await pipeline.OnAfterToolCallAsync(context, effectiveCall, result, ct)
                            .ConfigureAwait(false);
                    },
                    runToken).ConfigureAwait(false);

                foreach (var message in followUpMessages)
                {
                    context.Messages.Add(message);
                    Raise(new AgentMessageUpdate(RunId, message));
                }
            }

            Raise(new AgentRunCompletedUpdate(RunId, reply));
            _completion.TrySetResult(reply);
        }
        catch (OperationCanceledException) when (_cancelRequested)
        {
            Raise(new AgentRunCancelledUpdate(RunId, _cancelReason));
            _completion.TrySetCanceled(runToken);
        }
        catch (OperationCanceledException)
        {
            _completion.TrySetCanceled(cancellationToken);
        }
        catch (Exception ex)
        {
            Raise(new AgentRunFailedUpdate(RunId, ex.Message));
            _completion.TrySetException(new AgentRunException(ex.Message, ex));
        }
        finally
        {
            registration?.Dispose();
            _cancellationSource = null;
            await pipeline.OnEndAsync(context, CancellationToken.None).ConfigureAwait(false);
        }
    }

    /// <summary>
    ///     Runs one model round and passes its output through
    ///     <see cref="AgentPipeline.OnAfterAgentResponseAsync" />. The after hook always runs —
    ///     even when the model call throws or is cancelled — with the partial message and
    ///     whatever usage arrived up to that point (streaming providers report usage in
    ///     updates, so it can be available even without a completed response). The original
    ///     exception is rethrown afterwards.
    /// </summary>
    private async Task<AiResponse> RunModelRoundAsync(
        IChatClient chatClient,
        IReadOnlyList<AiToolDeclaration> declarations,
        AgentRunContext context,
        bool stream,
        AgentPipeline pipeline,
        int? maxOutputTokens)
    {
        var contents = new List<AIContent>();
        AiResponse response;
        try
        {
            response = await CallModelAsync(chatClient, declarations, context, stream, contents, maxOutputTokens)
                .ConfigureAwait(false);
        }
        catch (Exception)
        {
            // The run token may already be cancelled; the hook must still run, so no token here.
            var partial = ToPartialResponse(contents);
            await pipeline.OnAfterAgentResponseAsync(
                context, partial.Message, AgentResponseUsage.FromResponse(partial), CancellationToken.None)
                .ConfigureAwait(false);
            throw;
        }

        var message = await pipeline.OnAfterAgentResponseAsync(
            context, response.Message, AgentResponseUsage.FromResponse(response), context.CancellationToken)
            .ConfigureAwait(false);
        return new AiResponse(message, response.Usage, response.FinishReason);
    }

    /// <summary>Builds a response from whatever content arrived before a failed stream ended.</summary>
    private static AiResponse ToPartialResponse(List<AIContent> contents)
    {
        return contents.Count == 0
            ? new AiResponse(AiMessage.FromText(AiMessageRole.Agent, string.Empty))
            : AiMessageMapper.ToAiResponse(contents);
    }

    /// <summary>The actual model call.</summary>
    /// <param name="stream">
    ///     When <c>true</c>, the response is streamed and each text delta is raised as an
    ///     <see cref="AgentStreamUpdate" /> while it is produced.
    /// </param>
    /// <param name="chatClient">Client used to perform the model call.</param>
    /// <param name="declarations">Tool declarations exposed to the model, possibly empty.</param>
    /// <param name="context">Context of the run the call belongs to.</param>
    /// <param name="contents">
    ///     Collects every content received while streaming, so usage that already arrived is
    ///     not lost when the stream fails or is cancelled partway.
    /// </param>
    /// <param name="maxOutputTokens">Optional provider-enforced cap on produced tokens.</param>
    private async Task<AiResponse> CallModelAsync(
        IChatClient chatClient, IReadOnlyList<AiToolDeclaration> declarations, AgentRunContext context,
        bool stream, List<AIContent> contents, int? maxOutputTokens)
    {
        var chatMessages = context.Messages.Select(AiMessageMapper.ToChatMessage).ToList();
        ChatOptions? options;
        if (declarations.Count > 0 || maxOutputTokens is not null)
        {
            options = new ChatOptions { MaxOutputTokens = maxOutputTokens };
            if (declarations.Count > 0)
                options.Tools = [.. declarations.Select(ToChatTool)];
        }
        else
        {
            options = null;
        }

        if (!stream)
        {
            var response = await chatClient.GetResponseAsync(
                    chatMessages, options, context.CancellationToken)
                .ConfigureAwait(false);
            return AiMessageMapper.ToAiResponse(response);
        }

        string? finishReason = null;
        await foreach (var update in chatClient
                           .GetStreamingResponseAsync(chatMessages, options, context.CancellationToken)
                           .ConfigureAwait(false))
        {
            if (update.FinishReason is not null)
                finishReason = update.FinishReason.ToString();
            foreach (var content in update.Contents)
            {
                if (content is TextContent text && !string.IsNullOrEmpty(text.Text))
                    Raise(new AgentStreamUpdate(RunId, text.Text));
                contents.Add(content);
            }
        }

        return AiMessageMapper.ToAiResponse(contents, finishReason);
    }

    /// <summary>Converts a framework declaration into a non-invocable tool for the model.</summary>
    private static AITool ToChatTool(AiToolDeclaration declaration)
    {
        return AIFunctionFactory.CreateDeclaration(
            declaration.Identifier,
            declaration.Description,
            JsonDocument.Parse(declaration.JsonSchema ?? """{"type":"object","properties":{},"required":[]}""")
                .RootElement.Clone());
    }

    /// <summary>Default tool execution: registry lookup, error capture.</summary>
    private static async Task<AiToolResultContent> ExecuteToolAsync(
        ToolRegistry? tools, AgentRunContext context, AiToolCallContent call)
    {
        if (tools is null || !tools.TryGet(call.Name, out var tool))
            return new AiToolResultContent(call.CallId, $"Unknown tool '{call.Name}'.", true);

        try
        {
            return await tool.InvokeAsync(call, context.CancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return new AiToolResultContent(call.CallId, ex.Message, true);
        }
    }

    private void Raise(AgentUpdate update)
    {
        Updated?.Invoke(update);
    }
}