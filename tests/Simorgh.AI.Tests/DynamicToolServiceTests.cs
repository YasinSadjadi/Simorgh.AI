using System.Runtime.CompilerServices;
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

public class DynamicToolServiceTests
{
    private const string Schema = """{"type":"object","properties":{}}""";

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task All_registration_methods_work_together_with_instant_and_lazy_strategies(bool lazy, bool stream)
    {
        CancellationToken receivedToken = default;
        var doubleTool = AIFunctionFactory.Create((int value, CancellationToken cancellationToken) =>
        {
            receivedToken = cancellationToken;
            return value * 2;
        }, name: "double", description: "Double a number");
        var weatherTool = AIFunctionFactory.Create(
            (WeatherQuery query) => new WeatherReport(query.City, 22),
            name: "current", description: "Current weather");
        IToolService service = new DynamicToolService("dynamic", "Group metadata only", [doubleTool, weatherTool]);
        var registry = new ToolRegistry()
            .Register(new IncrementService())
            .Register("standalone/delegate", "Delegate tool", Schema,
                (call, _) => Task.FromResult(new AiToolResultContent(call.CallId, "delegate")))
            .Register("standalone/handler", "Handler tool", Schema, new ConstantHandler())
            .Register(service);
        var identifiers = registry.Tools.Select(t => t.Identifier).Order().ToArray();
        var calls = new[]
        {
            Call("method-call", "attributed/increment", ("value", 2)),
            Call("delegate-call", "standalone/delegate"),
            Call("handler-call", "standalone/handler"),
            Call("function-call", "dynamic/double", ("value", 7)),
            Call("weather-call", "dynamic/current", ("query", new { city = "Paris" })),
        };
        var steps = new List<Func<IReadOnlyList<ChatMessage>, ChatOptions?, ChatResponse>>();
        if (lazy)
        {
            steps.Add((_, options) =>
            {
                Assert.Equal(new[] { "list_tools", "load_tool" }, options!.Tools!.Select(t => t.Name));
                return Calls(Call("discover", LazyToolCallingStrategy.ListToolsName));
            });
            steps.Add((messages, _) =>
            {
                using var catalog = JsonDocument.Parse(Result(messages, "discover"));
                Assert.Equal(identifiers, catalog.RootElement.EnumerateArray()
                    .Select(t => t.GetProperty("identifier").GetString()).Order());
                return Calls(identifiers.Select((id, i) =>
                    Call($"load-{i}", LazyToolCallingStrategy.LoadToolName, ("identifier", id))).ToArray());
            });
        }
        steps.Add((_, options) =>
        {
            Assert.All(options!.Tools!, tool => Assert.IsNotAssignableFrom<AIFunction>(tool));
            Assert.All(identifiers, id => Assert.Contains(options.Tools!, tool => tool.Name == id));
            var declaration = options.Tools!.OfType<AIFunctionDeclaration>().Single(t => t.Name == "dynamic/double");
            Assert.Equal("Double a number", declaration.Description);
            Assert.Equal("integer", declaration.JsonSchema.GetProperty("properties").GetProperty("value")
                .GetProperty("type").GetString());
            return Calls(calls);
        });
        steps.Add((messages, _) =>
        {
            Assert.Equal("3", Result(messages, "method-call"));
            Assert.Equal("delegate", Result(messages, "delegate-call"));
            Assert.Equal("handler", Result(messages, "handler-call"));
            Assert.Equal("14", Result(messages, "function-call"));
            using var weather = JsonDocument.Parse(Result(messages, "weather-call"));
            Assert.Equal("Paris", weather.RootElement.GetProperty("city").GetString());
            Assert.Equal(22, weather.RootElement.GetProperty("temperature").GetInt32());
            return Done();
        });
        var recorder = new RecordingMiddleware();
        var agent = AgentService.Initialize(new ConversationClient(steps),
            lazy ? new LazyToolCallingStrategy() : new InstantToolCallingStrategy(),
            new AgentServiceOptions { Tools = registry, Middlewares = [recorder] });

        await agent.Submit(Request(stream)).Completion;

        Assert.True(receivedToken.CanBeCanceled);
        Assert.Equal(5, recorder.AfterToolCalls.Count);
        Assert.All(recorder.AfterToolCalls, call => Assert.Null(call.Error));
        Assert.Equal("double", doubleTool.Name);
        Assert.All(registry.ChatTools, tool => Assert.IsNotAssignableFrom<AIFunction>(tool));
        Assert.Same(service, registry.Tools.Single(t => t.Identifier == "dynamic/double").Service);
    }

    [Fact]
    public async Task Strategies_can_select_services_by_group_metadata_without_changing_the_existing_contract()
    {
        var function = AIFunctionFactory.Create(() => 42, name: "answer", description: "Answer a question");
        var registry = new ToolRegistry()
            .Register(new IncrementService())
            .Register(new DynamicToolService("selected", "Select this group", [function]))
            .Register("standalone", "Ungrouped tool", Schema, new ConstantHandler());
        var strategy = new GroupSelectingStrategy("Select this group");
        var client = new ConversationClient([
            (_, options) =>
            {
                var advertised = Assert.Single(options!.Tools!);
                Assert.Equal("selected/answer", advertised.Name);
                Assert.Equal("Answer a question", advertised.Description);
                return Calls(Call("answer-call", "selected/answer"));
            },
            (messages, _) =>
            {
                Assert.Equal("42", Result(messages, "answer-call"));
                return Done();
            },
        ]);
        var agent = AgentService.Initialize(client, strategy, new AgentServiceOptions { Tools = registry });

        await agent.Submit(Request()).Completion;

        Assert.Equal(3, strategy.AvailableTools!.Count);
        Assert.Equal(2, strategy.Services!.Count);
        var selected = strategy.Services.Single(s => s.Identifier == "selected");
        Assert.Equal("Select this group", selected.Description);
        Assert.Equal("selected/answer", Assert.Single(selected.Tools).Identifier);
        Assert.Equal("Attributed tools", strategy.Services.Single(s => s.Identifier == "attributed").Description);
    }

    [Fact]
    public async Task Lazy_strategy_does_not_execute_a_function_before_it_is_loaded()
    {
        var invoked = false;
        var function = AIFunctionFactory.Create(() => { invoked = true; return 42; }, name: "answer");
        var registry = new ToolRegistry().Register(new DynamicToolService("dynamic", "Answers", [function]));
        var client = new ConversationClient([
            (_, _) => Calls(Call("unloaded", "dynamic/answer")),
            (messages, _) =>
            {
                Assert.Contains("not loaded", Result(messages, "unloaded"));
                return Done();
            },
        ]);
        var agent = AgentService.Initialize(client, new LazyToolCallingStrategy(), new AgentServiceOptions { Tools = registry });

        await agent.Submit(Request()).Completion;

        Assert.False(invoked);
    }

    [Fact]
    public async Task Functions_keep_their_own_argument_binding_and_result_serialization()
    {
        var options = new JsonSerializerOptions(JsonSerializerOptions.Web)
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        };
        var function = AIFunctionFactory.Create((CityQuery query) => new CityQuery(query.CityName.ToUpperInvariant()),
            name: "city", serializerOptions: options);
        var registry = new ToolRegistry().Register(new DynamicToolService("weather", "Weather", [function]));

        var result = await Assert.Single(registry.Tools).InvokeAsync(
            new AiToolCallContent("city-call", "weather/city", """{"query":{"city_name":"Paris"}}"""), default);

        Assert.Equal("city-call", result.CallId);
        using var document = JsonDocument.Parse(result.Result);
        Assert.Equal("PARIS", document.RootElement.GetProperty("city_name").GetString());
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("42")]
    [InlineData("{invalid")]
    public async Task Invalid_argument_objects_are_rejected_before_the_function_runs(string json)
    {
        var invoked = false;
        var function = AIFunctionFactory.Create(() => { invoked = true; return 1; }, name: "answer");
        var registry = new ToolRegistry().Register(new DynamicToolService("dynamic", "Answers", [function]));

        await Assert.ThrowsAsync<JsonException>(() => Assert.Single(registry.Tools).InvokeAsync(
            new AiToolCallContent("bad-call", "dynamic/answer", json), default));

        Assert.False(invoked);
    }

    [Fact]
    public async Task Parameterless_void_functions_accept_empty_arguments()
    {
        var invoked = false;
        var function = AIFunctionFactory.Create(() => { invoked = true; }, name: "action");
        var registry = new ToolRegistry().Register(new DynamicToolService("dynamic", "Actions", [function]));

        var result = await Assert.Single(registry.Tools).InvokeAsync(
            new AiToolCallContent("void-call", "dynamic/action", " "), default);

        Assert.True(invoked);
        Assert.Equal(string.Empty, result.Result);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void Unsupported_tools_do_not_partially_register_a_service(int kind)
    {
        var function = AIFunctionFactory.Create(() => 1, name: "valid");
        AITool unsupported = kind switch
        {
            0 => null!,
            1 => function.AsDeclarationOnly(),
            _ => new HostedCodeInterpreterTool(),
        };
        var registry = new ToolRegistry();

        Assert.Throws<ArgumentException>(() => registry.Register(
            new DynamicToolService("dynamic", "Tools", [function, unsupported])));

        Assert.Empty(registry.Tools);
        Assert.Empty(registry.Services);
    }

    [Theory]
    [InlineData("method")]
    [InlineData("delegate")]
    [InlineData("handler")]
    [InlineData("function")]
    public void Duplicate_detection_is_shared_with_every_registration_method(string existingKind)
    {
        var duplicate = AIFunctionFactory.Create((int value) => value, name: "increment");
        var registry = existingKind switch
        {
            "method" => new ToolRegistry().Register(new IncrementService()),
            "delegate" => new ToolRegistry().Register("attributed/increment", "Existing", Schema,
                (call, _) => Task.FromResult(new AiToolResultContent(call.CallId, "existing"))),
            "handler" => new ToolRegistry().Register("attributed/increment", "Existing", Schema, new ConstantHandler()),
            _ => new ToolRegistry().Register(new DynamicToolService("attributed", "Existing", [duplicate])),
        };
        var serviceCount = registry.Services.Count;
        var another = AIFunctionFactory.Create(() => 1, name: "another");

        Assert.Throws<InvalidOperationException>(() => registry.Register(
            new DynamicToolService("attributed", "Duplicate", [another, duplicate])));

        Assert.Single(registry.Tools);
        Assert.Equal(serviceCount, registry.Services.Count);
        Assert.False(registry.TryGet("attributed/another", out _));
        Assert.Throws<InvalidOperationException>(() => new ToolRegistry().Register(
            new DynamicToolService("same", "Duplicate within a group", [duplicate, duplicate])));
    }

    [Fact]
    public async Task Functions_can_be_reused_in_different_services_and_input_collections_are_snapshotted()
    {
        var function = AIFunctionFactory.Create(() => 7, name: "answer");
        AITool[] input = [function];
        var first = new DynamicToolService("first", "First group", input);
        input[0] = AIFunctionFactory.Create(() => 0, name: "replacement");
        var registry = new ToolRegistry().Register(first);
        var snapshot = registry.Services;
        registry.Register(new DynamicToolService("second", "Second group", [function]));

        Assert.Single(snapshot);
        Assert.Equal(2, registry.Services.Count);
        Assert.Same(function, Assert.Single(first.Tools));
        Assert.Equal("answer", function.Name);
        foreach (var tool in registry.Tools)
            Assert.Equal("7", (await tool.InvokeAsync(new AiToolCallContent("call", tool.Identifier, "{}"), default)).Result);

        Assert.Throws<InvalidOperationException>(() => registry.Register(new IncrementServiceWithCollision()));
        Assert.Equal(2, registry.Tools.Count);
    }

    [Fact]
    public async Task Function_exceptions_flow_through_tool_middleware_and_back_to_the_model()
    {
        var function = AIFunctionFactory.Create((Func<int>)(() => throw new InvalidOperationException("function failed")),
            name: "fail");
        var registry = new ToolRegistry().Register(new DynamicToolService("dynamic", "Failures", [function]));
        var recorder = new RecordingMiddleware();
        var client = new ConversationClient([
            (_, _) => Calls(Call("failure-call", "dynamic/fail")),
            (messages, _) =>
            {
                Assert.Contains("function failed", Result(messages, "failure-call"));
                return Done();
            },
        ]);
        var agent = AgentService.Initialize(client, new InstantToolCallingStrategy(),
            new AgentServiceOptions { Tools = registry, Middlewares = [recorder] });

        await agent.Submit(Request()).Completion;

        var call = Assert.Single(recorder.AfterToolCalls);
        Assert.Equal("failure-call", call.Call.CallId);
        Assert.IsType<InvalidOperationException>(call.Error);
        Assert.True(recorder.Called("before-tool"));
    }

    [Fact]
    public async Task Cancellation_reaches_the_function_and_the_after_tool_hook()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var function = AIFunctionFactory.Create(async (CancellationToken cancellationToken) =>
        {
            entered.SetResult();
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return 1;
        }, name: "wait");
        var registry = new ToolRegistry().Register(new DynamicToolService("dynamic", "Waiting", [function]));
        var recorder = new RecordingMiddleware();
        var client = new ConversationClient([(_, _) => Calls(Call("wait-call", "dynamic/wait"))]);
        var agent = AgentService.Initialize(client, new InstantToolCallingStrategy(),
            new AgentServiceOptions { Tools = registry, Middlewares = [recorder] });
        using var cancellation = new CancellationTokenSource();
        var run = agent.Submit(Request(), cancellation.Token);

        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run.Completion.WaitAsync(TimeSpan.FromSeconds(10)));

        Assert.IsAssignableFrom<OperationCanceledException>(Assert.Single(recorder.AfterToolCalls).Error);
    }

    private static AgentRunRequest Request(bool stream = false) =>
        new([AiMessage.FromText(AiMessageRole.User, "Use the tools")], stream: stream);

    private static FunctionCallContent Call(string id, string name, params (string Name, object? Value)[] arguments) =>
        new(id, name, arguments.ToDictionary(p => p.Name, p => p.Value));

    private static ChatResponse Calls(params FunctionCallContent[] calls) =>
        new(new ChatMessage(ChatRole.Assistant, [.. calls]));

    private static ChatResponse Done() => new(new ChatMessage(ChatRole.Assistant, "done"));

    private static string Result(IReadOnlyList<ChatMessage> messages, string callId) => messages
        .SelectMany(m => m.Contents).OfType<FunctionResultContent>().Single(r => r.CallId == callId).Result!.ToString()!;

    public sealed record WeatherQuery(string City);
    public sealed record WeatherReport(string City, int Temperature);
    public sealed record CityQuery(string CityName);

    private sealed class IncrementService : IToolService
    {
        public string Identifier => "attributed";
        public string Description => "Attributed tools";

        [AiToolMethod("increment", "Increment a number")]
        public int Increment(int value) => value + 1;
    }

    private sealed class IncrementServiceWithCollision : IToolService
    {
        public string Identifier => "first";
        public string Description => "Colliding service";

        [AiToolMethod("answer", "An existing function name")]
        public int Answer() => 0;
    }

    private sealed class ConstantHandler : IDynamicToolHandler
    {
        public Task<AiToolResultContent> InvokeAsync(AiToolCallContent call, CancellationToken cancellationToken = default) =>
            Task.FromResult(new AiToolResultContent(call.CallId, "handler"));
    }

    private sealed class GroupSelectingStrategy(string description) : IToolCallingStrategy
    {
        public IReadOnlyList<AiToolDeclaration>? AvailableTools { get; private set; }
        public IReadOnlyList<AiToolServiceDeclaration>? Services { get; private set; }
        public int MaxToolRounds => 4;

        public Task<IReadOnlyList<AiToolDeclaration>> GetToolDeclarationsAsync(
            IReadOnlyList<AiToolDeclaration> availableTools, AgentRunContext context, int round,
            CancellationToken cancellationToken = default)
        {
            AvailableTools = availableTools;
            Services = context.ToolServices;
            return Task.FromResult<IReadOnlyList<AiToolDeclaration>>(
                context.ToolServices.Where(s => s.Description == description).SelectMany(s => s.Tools).ToArray());
        }

        public Task<IReadOnlyList<AiMessage>> InvokeToolsAsync(AgentRunContext context,
            IReadOnlyList<AiToolCallContent> calls,
            Func<AiToolCallContent, CancellationToken, Task<AiToolResultContent>> invokeNext,
            CancellationToken cancellationToken = default) =>
            new InstantToolCallingStrategy().InvokeToolsAsync(context, calls, invokeNext, cancellationToken);
    }

    private sealed class ConversationClient(
        IReadOnlyList<Func<IReadOnlyList<ChatMessage>, ChatOptions?, ChatResponse>> steps) : IChatClient
    {
        private int _round;

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.InRange(_round, 0, steps.Count - 1);
            return Task.FromResult(steps[_round++](messages.ToList(), options));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
            ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var response = await GetResponseAsync(messages, options, cancellationToken);
            foreach (var message in response.Messages)
                yield return new ChatResponseUpdate { Role = message.Role, Contents = message.Contents };
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }
}
