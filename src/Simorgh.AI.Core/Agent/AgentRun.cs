using System.Text.Json;
using Microsoft.Extensions.AI;
using Simorgh.AI.Abstractions.Agent;
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

    private readonly TaskCompletionSource<AiMessage> _completion = new(
        TaskCreationOptions.RunContinuationsAsynchronously);

    private AgentRun()
    {
    }

    public string RunId { get; } = Guid.NewGuid().ToString("N");

    public Task<AiMessage> Completion => _completion.Task;

    public event Action<AgentUpdate>? Updated;

    /// <summary>Creates the run and starts executing it in the background.</summary>
    public static IAgentRun Start(
        IChatClient chatClient,
        ToolRegistry? tools,
        IToolCallingStrategy toolCallingStrategy,
        AgentPipeline pipeline,
        AgentRunRequest request,
        CancellationToken cancellationToken)
    {
        var run = new AgentRun();
        _ = run.ExecuteAsync(chatClient, tools, toolCallingStrategy, pipeline, request, cancellationToken);
        return run;
    }

    private async Task ExecuteAsync(
        IChatClient chatClient,
        ToolRegistry? tools,
        IToolCallingStrategy toolCallingStrategy,
        AgentPipeline pipeline,
        AgentRunRequest request,
        CancellationToken cancellationToken)
    {
        var context = new AgentRunContext(RunId, [.. request.Messages], cancellationToken);

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
            await pipeline.OnStartupAsync(context, cancellationToken).ConfigureAwait(false);

            AiMessage reply;
            var round = 0;
            while (true)
            {
                var declarations = await toolCallingStrategy.GetToolDeclarationsAsync(
                    availableTools, context, round, cancellationToken).ConfigureAwait(false);

                reply = await pipeline.InvokeResponseAsync(
                    context, ctx => CallModelAsync(chatClient, declarations, ctx, request.Stream))
                    .ConfigureAwait(false);
                context.Messages.Add(reply);

                var calls = reply.Contents.OfType<AiToolCallContent>().ToList();
                if (calls.Count == 0) break;

                if (++round > toolCallingStrategy.MaxToolRounds)
                    throw new AgentRunException(
                        $"The run exceeded the maximum of {toolCallingStrategy.MaxToolRounds} tool rounds without a final answer.");

                Raise(new AgentMessageUpdate(RunId, reply));

                var followUpMessages = await toolCallingStrategy.InvokeToolsAsync(
                    context, calls,
                    (call, ct) => pipeline.InvokeToolAsync(
                        context, call, (ctx, c) => ExecuteToolAsync(tools, ctx, c)),
                    cancellationToken).ConfigureAwait(false);

                foreach (var message in followUpMessages)
                {
                    context.Messages.Add(message);
                    Raise(new AgentMessageUpdate(RunId, message));
                }
            }

            Raise(new AgentRunCompletedUpdate(RunId, reply));
            _completion.TrySetResult(reply);
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
            await pipeline.OnEndAsync(context, CancellationToken.None).ConfigureAwait(false);
        }
    }

    /// <summary>Terminal stage of the response chain: the actual model call.</summary>
    /// <param name="stream">
    ///     When <c>true</c>, the response is streamed and each text delta is raised as an
    ///     <see cref="AgentStreamUpdate" /> while it is produced.
    /// </param>
    private async Task<AiMessage> CallModelAsync(
        IChatClient chatClient, IReadOnlyList<AiToolDeclaration> declarations, AgentRunContext context,
        bool stream)
    {
        var chatMessages = context.Messages.Select(AiMessageMapper.ToChatMessage).ToList();
        var options = declarations.Count > 0
            ? new ChatOptions { Tools = [.. declarations.Select(ToChatTool)] }
            : null;

        if (!stream)
        {
            var response = await chatClient.GetResponseAsync(
                    chatMessages, options, context.CancellationToken)
                .ConfigureAwait(false);
            return AiMessageMapper.ToAiMessage(response);
        }

        var contents = new List<AIContent>();
        await foreach (var update in chatClient
                           .GetStreamingResponseAsync(chatMessages, options, context.CancellationToken)
                           .ConfigureAwait(false))
        {
            foreach (var content in update.Contents)
            {
                if (content is TextContent text && !string.IsNullOrEmpty(text.Text))
                    Raise(new AgentStreamUpdate(RunId, text.Text));
                contents.Add(content);
            }
        }

        return AiMessageMapper.ToAiMessage(contents);
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