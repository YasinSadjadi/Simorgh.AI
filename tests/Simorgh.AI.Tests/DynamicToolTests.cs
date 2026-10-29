using System.Text.Json;
using Microsoft.Extensions.AI;
using Simorgh.AI.Abstractions.Agent;
using Simorgh.AI.Abstractions.Contents;
using Simorgh.AI.Abstractions.Messages;
using Simorgh.AI.Abstractions.Tools;
using Simorgh.AI.Core.Agent;
using Simorgh.AI.Core.Tools;
using Simorgh.AI.Extensions.Tools;

namespace Simorgh.AI.Tests;

public class DynamicToolTests
{
    private const string Schema = """{"type":"object","properties":{"value":{"type":"integer"}},"required":["value"]}""";

    [Fact]
    public async Task Dynamic_tool_is_advertised_and_invoked_through_agent_pipeline()
    {
        AiToolCallContent? received = null;
        var registry = new ToolRegistry().Register("svc/do", "dynamic tool", Schema, (call, ct) =>
        {
            received = call;
            return Task.FromResult(new AiToolResultContent(call.CallId, "42"));
        });
        var recorder = new RecordingMiddleware();
        var client = new CapturingClient();
        var agent = AgentService.Initialize(client, new InstantToolCallingStrategy(),
            new AgentServiceOptions { Tools = registry, Middlewares = [recorder] });

        await agent.Submit(new AgentRunRequest([AiMessage.FromText(AiMessageRole.User, "hello")], stream: false)).Completion;

        var declaration = Assert.IsAssignableFrom<AIFunctionDeclaration>(Assert.Single(client.Advertised!));
        Assert.IsNotAssignableFrom<AIFunction>(declaration);
        Assert.Equal("svc/do", declaration.Name);
        Assert.Equal("integer", declaration.JsonSchema.GetProperty("properties").GetProperty("value").GetProperty("type").GetString());
        Assert.Equal(1, JsonDocument.Parse(received!.ArgumentsJson).RootElement.GetProperty("value").GetInt32());
        Assert.Equal("call-1", received.CallId);
        Assert.True(recorder.Called("before-tool"));
        Assert.Equal("42", Assert.Single(recorder.AfterToolCalls).Result!.Result);
        Assert.Equal("42", client.ToolResult);
    }

    [Fact]
    public async Task Handler_instance_executes_through_agent_pipeline()
    {
        var handler = new TestHandler();
        var registry = new ToolRegistry().Register("svc/do", "dynamic tool", Schema, handler);
        var recorder = new RecordingMiddleware();
        var client = new CapturingClient();
        var agent = AgentService.Initialize(client, new InstantToolCallingStrategy(),
            new AgentServiceOptions { Tools = registry, Middlewares = [recorder] });

        await agent.Submit(new AgentRunRequest([AiMessage.FromText(AiMessageRole.User, "hello")], stream: false)).Completion;

        Assert.Equal("call-1", handler.Call!.CallId);
        Assert.Equal(1, JsonDocument.Parse(handler.Call.ArgumentsJson).RootElement.GetProperty("value").GetInt32());
        Assert.True(handler.Token.CanBeCanceled);
        Assert.True(recorder.Called("before-tool"));
        Assert.Equal("handled", Assert.Single(recorder.AfterToolCalls).Result!.Result);
        Assert.Equal("handled", client.ToolResult);
    }

    [Fact]
    public void Handler_overload_rejects_null_and_duplicate_identifiers()
    {
        var registry = new ToolRegistry();
        Assert.Throws<ArgumentNullException>(() => registry.Register("svc/do", "dynamic", Schema, (IDynamicToolHandler)null!));
        registry.Register("svc/do", "dynamic", Schema, new TestHandler());
        Assert.Throws<InvalidOperationException>(() => registry.Register("svc/do", "dynamic", Schema, new TestHandler()));
    }

    private sealed class TestHandler : IDynamicToolHandler
    {
        public AiToolCallContent? Call { get; private set; }
        public CancellationToken Token { get; private set; }

        public Task<AiToolResultContent> InvokeAsync(AiToolCallContent call, CancellationToken cancellationToken = default)
        {
            Call = call;
            Token = cancellationToken;
            return Task.FromResult(new AiToolResultContent(call.CallId, "handled"));
        }
    }

    [Fact]
    public void Dynamic_tools_share_duplicate_detection_and_validate_schema()
    {
        var registry = new ToolRegistry().Register("svc/do", "dynamic", Schema,
            (call, ct) => Task.FromResult(new AiToolResultContent(call.CallId, "ok")));
        Assert.Throws<InvalidOperationException>(() => registry.Register(new ThrowingToolService()));
        Assert.Throws<ArgumentException>(() => registry.Register("bad", "bad", "[]",
            (call, ct) => Task.FromResult(new AiToolResultContent(call.CallId, "ok"))));
    }

    private sealed class CapturingClient : IChatClient
    {
        private int _round;
        public IList<AITool>? Advertised { get; private set; }
        public string? ToolResult { get; private set; }

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            if (_round++ == 0)
            {
                Advertised = options!.Tools;
                return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant,
                    [new FunctionCallContent("call-1", "svc/do", new Dictionary<string, object?> { ["value"] = 1 })])));
            }
            ToolResult = messages.SelectMany(m => m.Contents).OfType<FunctionResultContent>().Single().Result?.ToString();
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "done")));
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
            ChatOptions? options = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }
}

