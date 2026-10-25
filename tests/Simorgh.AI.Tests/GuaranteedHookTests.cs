using Microsoft.Extensions.AI;
using Simorgh.AI.Abstractions.Agent;
using Simorgh.AI.Abstractions.Agent.Middleware;
using Simorgh.AI.Abstractions.Contents;
using Simorgh.AI.Abstractions.Messages;
using Simorgh.AI.Abstractions.Tools;
using Simorgh.AI.Core.Agent;
using Simorgh.AI.Core.Tools;
using Simorgh.AI.Extensions.Cancellation;
using Simorgh.AI.Extensions.Tools;

namespace Simorgh.AI.Tests;

/// <summary>
///     Chat client that answers the first round with a tool call and every later round
///     with a plain text answer, so a run performs exactly one tool round-trip.
/// </summary>
public sealed class ToolRoundChatClient : IChatClient
{
    public const string ToolIdentifier = "svc/do";
    
    public int Calls { get; private set; }

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        Calls++;
        var message = Calls == 1
            ? new ChatMessage(ChatRole.Assistant,
                [new FunctionCallContent("call-1", ToolIdentifier, new Dictionary<string, object?> { ["value"] = 1 })])
            : new ChatMessage(ChatRole.Assistant, "done");
        return Task.FromResult(new ChatResponse(message));
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.Yield();
        var update = Calls == 0
            ? new ChatResponseUpdate
            {
                Contents = [new FunctionCallContent("call-1", ToolIdentifier, new Dictionary<string, object?> { ["value"] = 1 })]
            }
            : new ChatResponseUpdate { Contents = [new TextContent("done")] };
        Calls++;
        yield return update;
    }

    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        return null;
    }

    public void Dispose()
    {
    }
}

/// <summary>Tool service whose only tool throws, to exercise the tool error path.</summary>
public sealed class ThrowingToolService : IToolService
{
    public bool Invoked { get; private set; }

    public string Identifier => "svc";

    public string Description => "test tools";

    [AiToolMethod("do", "does nothing useful")]
    public string Do()
    {
        Invoked = true;
        throw new InvalidOperationException("tool blew up");
    }
}

/// <summary>Which hook a <see cref="ResultingMiddleware" /> acts on.</summary>
public enum ResultingHook
{
    Startup,
    BeforeAgentResponse,
    AfterAgentResponse,
    BeforeToolCall,
    AfterToolCall,
    End
}

/// <summary>What the hook of a <see cref="ResultingMiddleware" /> returns.</summary>
public enum HookOutcome
{
    Ok,
    Warn,
    Fail,
    Throw
}

/// <summary>
///     Middleware that returns the given outcome from exactly one hook, exercising the
///     result semantics: <c>Warn</c> records a warning and the run carries on, <c>Fail</c>
///     breaks the run, and <c>Throw</c> is a stray exception the pipeline must treat as a
///     warning.
/// </summary>
public sealed class ResultingMiddleware(ResultingHook hook, HookOutcome outcome) : AgentMiddleware
{
    public InvalidOperationException FailError { get; } = new("hook failed on purpose");

    public int Hits { get; private set; }

    public override Task<AgentResult> OnStartupAsync(
        AgentRunContext context, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Plain(ResultingHook.Startup));
    }

    public override Task<AgentResult> OnBeforeAgentResponseAsync(
        AgentRunContext context, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Plain(ResultingHook.BeforeAgentResponse));
    }

    public override Task<AgentResult<AiMessage>> OnAfterAgentResponseAsync(
        AgentRunContext context, AiMessage response, AgentResponseUsage? usage,
        Exception? error = null, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Valued(ResultingHook.AfterAgentResponse, response));
    }

    public override Task<AgentResult<AiToolCallContent>> OnBeforeToolCallAsync(
        AgentRunContext context, AiToolCallContent call, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Valued(ResultingHook.BeforeToolCall, call));
    }

    public override Task<AgentResult<AiToolResultContent?>> OnAfterToolCallAsync(
        AgentRunContext context, AiToolCallContent call, AiToolResultContent? result,
        Exception? error = null, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Valued(ResultingHook.AfterToolCall, result));
    }

    public override Task<AgentResult> OnEndAsync(
        AgentRunContext context, AgentRunEnd end, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Plain(ResultingHook.End));
    }

    private AgentResult Plain(ResultingHook when)
    {
        if (hook != when) return AgentResult.Ok();
        Hits++;
        return outcome switch
        {
            HookOutcome.Warn => AgentResult.Warn("hook warned on purpose"),
            HookOutcome.Fail => AgentResult.Fail(FailError),
            HookOutcome.Throw => throw new InvalidOperationException("hook threw on purpose"),
            _ => AgentResult.Ok()
        };
    }

    private AgentResult<T> Valued<T>(ResultingHook when, T value)
    {
        if (hook != when) return AgentResult<T>.Ok(value!);
        Hits++;
        return outcome switch
        {
            HookOutcome.Warn => AgentResult<T>.Warn(value!, "hook warned on purpose"),
            HookOutcome.Fail => AgentResult<T>.Fail(FailError),
            HookOutcome.Throw => throw new InvalidOperationException("hook threw on purpose"),
            _ => AgentResult<T>.Ok(value!)
        };
    }
}

/// <summary>Middleware that records every hook invocation, including errors and the end reason.</summary>
public sealed class RecordingMiddleware : AgentMiddleware
{
    public List<string> Calls { get; } = [];
    public List<Exception?> ResponseErrors { get; } = [];
    public List<(AiToolCallContent Call, AiToolResultContent? Result, Exception? Error)> AfterToolCalls { get; } = [];
    public List<AgentRunEnd> Ends { get; } = [];

    public bool Called(string hook)
    {
        return Calls.Contains(hook);
    }

    public override Task<AgentResult> OnStartupAsync(
        AgentRunContext context, CancellationToken cancellationToken = default)
    {
        Calls.Add("startup");
        return Task.FromResult(AgentResult.Ok());
    }

    public override Task<AgentResult> OnBeforeAgentResponseAsync(
        AgentRunContext context, CancellationToken cancellationToken = default)
    {
        Calls.Add("before-response");
        return Task.FromResult(AgentResult.Ok());
    }

    public override Task<AgentResult<AiMessage>> OnAfterAgentResponseAsync(
        AgentRunContext context, AiMessage response, AgentResponseUsage? usage,
        Exception? error = null, CancellationToken cancellationToken = default)
    {
        Calls.Add("after-response");
        ResponseErrors.Add(error);
        return Task.FromResult(AgentResult<AiMessage>.Ok(response));
    }

    public override Task<AgentResult<AiToolCallContent>> OnBeforeToolCallAsync(
        AgentRunContext context, AiToolCallContent call, CancellationToken cancellationToken = default)
    {
        Calls.Add("before-tool");
        return Task.FromResult(AgentResult<AiToolCallContent>.Ok(call));
    }

    public override Task<AgentResult<AiToolResultContent?>> OnAfterToolCallAsync(
        AgentRunContext context, AiToolCallContent call, AiToolResultContent? result,
        Exception? error = null, CancellationToken cancellationToken = default)
    {
        Calls.Add("after-tool");
        AfterToolCalls.Add((call, result, error));
        return Task.FromResult(AgentResult<AiToolResultContent?>.Ok(result));
    }

    public override Task<AgentResult> OnEndAsync(
        AgentRunContext context, AgentRunEnd end, CancellationToken cancellationToken = default)
    {
        Calls.Add("end");
        Ends.Add(end);
        return Task.FromResult(AgentResult.Ok());
    }
}

public class GuaranteedHookTests
{
    private static AgentRunRequest Request()
    {
        return new AgentRunRequest([AiMessage.FromText(AiMessageRole.User, "hello")], stream: false);
    }

    private static AgentService AgentWithTools(
        IChatClient chatClient, IToolService toolService, params AgentMiddleware[] middlewares)
    {
        return AgentService.Initialize(
            chatClient, new InstantToolCallingStrategy(),
            new AgentServiceOptions
            {
                Middlewares = middlewares,
                Tools = new ToolRegistry().Register(toolService)
            });
    }

    [Fact]
    public async Task Model_error_reaches_after_response_hook_of_every_middleware_with_exception()
    {
        var first = new RecordingMiddleware();
        var second = new RecordingMiddleware();
        var agent = AgentService.Initialize(
            new FailingAfterUsageChatClient(), new NoToolsStrategy(),
            new AgentServiceOptions { Middlewares = [first, second] });

        var run = agent.Submit(Request());
        await Assert.ThrowsAsync<AgentRunException>(() => run.Completion);

        foreach (var middleware in new[] { first, second })
        {
            Assert.Equal(["startup", "before-response", "after-response", "end"], middleware.Calls);
            var error = Assert.Single(middleware.ResponseErrors);
            Assert.IsType<InvalidOperationException>(error);
            var end = Assert.Single(middleware.Ends);
            Assert.Equal(AgentRunEndReason.Failed, end.Reason);
            Assert.Same(error, end.Error);
        }
    }

    [Fact]
    public async Task Warn_result_records_a_warning_and_the_run_carries_on()
    {
        var warning = new ResultingMiddleware(ResultingHook.AfterAgentResponse, HookOutcome.Warn);
        var recorder = new RecordingMiddleware();
        var agent = AgentService.Initialize(
            new ReportingChatClient(), new NoToolsStrategy(),
            new AgentServiceOptions { Middlewares = [warning, recorder] });

        var updates = new List<AgentUpdate>();
        var run = agent.Submit(Request(), subscribe: r => r.Updated += updates.Add);
        var reply = await run.Completion;

        Assert.NotNull(reply);
        Assert.True(recorder.Called("after-response"));
        Assert.True(recorder.Called("end"));
        Assert.Equal(AgentRunEndReason.Completed, Assert.Single(recorder.Ends).Reason);

        var reported = Assert.IsType<AgentRunWarningUpdate>(
            Assert.Single(updates, u => u is AgentRunWarningUpdate)).Warning;
        Assert.Equal(nameof(ResultingMiddleware), reported.Source);
        Assert.Equal("hook warned on purpose", reported.Message);
    }

    [Fact]
    public async Task Stray_exception_from_a_middleware_is_treated_as_a_warning()
    {
        var stray = new ResultingMiddleware(ResultingHook.AfterAgentResponse, HookOutcome.Throw);
        var recorder = new RecordingMiddleware();
        var agent = AgentService.Initialize(
            new ReportingChatClient(), new NoToolsStrategy(),
            new AgentServiceOptions { Middlewares = [stray, recorder] });

        var updates = new List<AgentUpdate>();
        var run = agent.Submit(Request(), subscribe: r => r.Updated += updates.Add);
        var reply = await run.Completion;

        Assert.NotNull(reply);
        Assert.True(recorder.Called("after-response"));
        var reported = Assert.IsType<AgentRunWarningUpdate>(
            Assert.Single(updates, u => u is AgentRunWarningUpdate)).Warning;
        Assert.Contains("threw on purpose", reported.Message);
        Assert.IsType<InvalidOperationException>(reported.Error);
    }

    [Fact]
    public async Task Fail_result_breaks_the_run_and_reports_reason_on_end()
    {
        var failing = new ResultingMiddleware(ResultingHook.AfterAgentResponse, HookOutcome.Fail);
        var recorder = new RecordingMiddleware();
        var agent = AgentService.Initialize(
            new ReportingChatClient(), new NoToolsStrategy(),
            new AgentServiceOptions { Middlewares = [failing, recorder] });

        var run = agent.Submit(Request());
        var failure = await Assert.ThrowsAsync<AgentRunException>(() => run.Completion);

        // The failing result skips the rest of the stage...
        Assert.False(recorder.Called("after-response"));
        // ...but the end hook still runs with the middleware's own error as the reason.
        var end = Assert.Single(recorder.Ends);
        Assert.Equal(AgentRunEndReason.Failed, end.Reason);
        Assert.Same(failing.FailError, end.Error);
        Assert.Same(failing.FailError, failure.InnerException);
    }

    [Fact]
    public async Task Tool_error_runs_after_tool_hook_with_exception_and_run_still_completes()
    {
        var toolService = new ThrowingToolService();
        var recorder = new RecordingMiddleware();
        var agent = AgentWithTools(new ToolRoundChatClient(), toolService, recorder);

        var reply = await agent.Submit(Request()).Completion;

        Assert.Equal("done", Assert.Single(reply.Message.Contents.OfType<AiTextContent>()).Text);
        Assert.True(toolService.Invoked);
        var (_, result, error) = Assert.Single(recorder.AfterToolCalls);
        // The tool threw; the exception is passed to the hook (no result was produced),
        // and the fallback error result is fed back to the model, so the run recovers
        // with a final answer.
        Assert.NotNull(error);
        Assert.True(result is null || result.IsError);
        Assert.Equal(AgentRunEndReason.Completed, Assert.Single(recorder.Ends).Reason);
    }

    [Fact]
    public async Task Fail_result_in_before_tool_runs_after_tool_hook_then_fails_the_run()
    {
        var toolService = new ThrowingToolService();
        var failing = new ResultingMiddleware(ResultingHook.BeforeToolCall, HookOutcome.Fail);
        var recorder = new RecordingMiddleware();
        var agent = AgentWithTools(new ToolRoundChatClient(), toolService, failing, recorder);

        var run = agent.Submit(Request());
        await Assert.ThrowsAsync<AgentRunException>(() => run.Completion);

        Assert.False(toolService.Invoked);
        Assert.False(recorder.Called("before-tool")); // fail skips the rest of the stage
        var (_, result, error) = Assert.Single(recorder.AfterToolCalls);
        // The after hook saw the failure of the before stage, then the run failed.
        Assert.IsAssignableFrom<Exception>(error);
        Assert.True(result is null || result.IsError);
        var end = Assert.Single(recorder.Ends);
        Assert.Equal(AgentRunEndReason.Failed, end.Reason);
        Assert.Same(failing.FailError, end.Error);
    }

    [Fact]
    public async Task Cancelled_run_runs_all_hooks_and_reports_cancelled_end_reason()
    {
        var recorder = new RecordingMiddleware();
        var registry = new InMemoryAgentRunRegistry();
        var agent = AgentService.Initialize(
            new SlowStreamingChatClient(deltas: 50, deltaDelayMs: 20), new NoToolsStrategy(),
            new AgentServiceOptions { Middlewares = [recorder], RunRegistry = registry });

        var run = agent.Submit(new AgentRunRequest(
            [AiMessage.FromText(AiMessageRole.User, "hello")], stream: true));
        await registry.CancelAsync(run.RunId, "user requested");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run.Completion);

        Assert.True(recorder.Called("after-response"));
        // The streaming delay surfaces cancellation as a TaskCanceledException, a
        // subclass of OperationCanceledException.
        Assert.IsAssignableFrom<OperationCanceledException>(Assert.Single(recorder.ResponseErrors));
        var end = Assert.Single(recorder.Ends);
        Assert.Equal(AgentRunEndReason.Cancelled, end.Reason);
        Assert.Equal("user requested", end.CancelReason);
    }

    [Fact]
    public async Task Successful_run_reports_completed_end_reason()
    {
        var recorder = new RecordingMiddleware();
        var agent = AgentService.Initialize(
            new ReportingChatClient(), new NoToolsStrategy(),
            new AgentServiceOptions { Middlewares = [recorder] });

        await agent.Submit(Request()).Completion;

        var end = Assert.Single(recorder.Ends);
        Assert.Equal(AgentRunEndReason.Completed, end.Reason);
        Assert.Null(end.Error);
    }

    [Fact]
    public async Task End_hook_failure_cannot_break_the_finished_run()
    {
        var failing = new ResultingMiddleware(ResultingHook.End, HookOutcome.Fail);
        var recorder = new RecordingMiddleware();
        var agent = AgentService.Initialize(
            new ReportingChatClient(), new NoToolsStrategy(),
            new AgentServiceOptions { Middlewares = [failing, recorder] });

        var updates = new List<AgentUpdate>();
        var run = agent.Submit(Request(), subscribe: r => r.Updated += updates.Add);
        var reply = await run.Completion;

        // The run is already over: the failing end result is recorded as a warning and
        // every middleware still ran.
        Assert.NotNull(reply);
        Assert.Equal(1, failing.Hits);
        Assert.Equal(AgentRunEndReason.Completed, Assert.Single(recorder.Ends).Reason);
        var reported = Assert.IsType<AgentRunWarningUpdate>(
            Assert.Single(updates, u => u is AgentRunWarningUpdate)).Warning;
        Assert.Equal(nameof(ResultingMiddleware), reported.Source);
    }

    /// <summary>Middleware that reports a warning through the context instead of throwing.</summary>
    private sealed class ReportingWarningMiddleware : AgentMiddleware
    {
        public override Task<AgentResult> OnStartupAsync(
            AgentRunContext context, CancellationToken cancellationToken = default)
        {
            context.ReportWarning("custom-source", "custom message");
            return Task.FromResult(AgentResult.Ok());
        }
    }
}
