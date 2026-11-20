using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using Simorgh.AI.Abstractions.Agent;
using Simorgh.AI.Abstractions.Agent.Middleware;
using Simorgh.AI.Abstractions.Contents;
using Simorgh.AI.Abstractions.Messages;
using Simorgh.AI.Abstractions.Skills;
using Simorgh.AI.Abstractions.Tools;
using Simorgh.AI.Core.Agent;
using Simorgh.AI.Core.Skills;
using Simorgh.AI.Core.Tools;
using Simorgh.AI.Extensions.Cancellation;
using Simorgh.AI.Extensions.Tools;

namespace Simorgh.AI.Tests;

public sealed class AgentSkillsTests
{
    private const string ActivateSkill = "activate_skill";
    private static readonly SkillDto Refund = new(
        "refund-policy", "Refund policy", "Use when a customer asks for a refund.", ["billing"], "REFUND INSTRUCTIONS");
    private static readonly SkillDto Delivery = new(
        "delivery-status", "Delivery status", "Use when tracking a delivery.", [], "DELIVERY INSTRUCTIONS");
    private static readonly SkillDto Exchange = new(
        "exchange-policy", "Exchange policy", "Use when exchanging an order.", [], "EXCHANGE INSTRUCTIONS");

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Model_sees_metadata_then_activates_a_suggested_skill_once(bool stream, bool lazy)
    {
        var round = 0;
        var client = new SkillChatClient((messages, options) =>
        {
            var declaration = Assert.Single(options!.Tools!.OfType<AIFunctionDeclaration>(), tool => tool.Name == ActivateSkill);
            if (++round == 1)
            {
                var system = Assert.Single(messages, message => message.Role == ChatRole.System).Text;
                Assert.Contains(Refund.Title, system);
                Assert.Contains(Refund.WhenToUse, system);
                Assert.Contains("wait for the instructions", system);
                Assert.DoesNotContain("billing", system);
                Assert.DoesNotContain(Delivery.Title, system);
                Assert.DoesNotContain(Refund.Content, Transcript(messages));
                var ids = declaration.JsonSchema.GetProperty("properties").GetProperty("id").GetProperty("enum");
                Assert.Equal(Refund.Id, Assert.Single(ids.EnumerateArray()).GetString());

                return Reply(
                    Activation("out-of-list", Delivery.Id), Activation("load-refund", Refund.Id));
            }

            var toolResults = ToolResults(messages);
            Assert.Contains("availableSkills", toolResults["out-of-list"]);
            Assert.Contains(Refund.Content, toolResults["load-refund"]);
            Assert.DoesNotContain(Delivery.Content, Transcript(messages));
            if (round == 2)
                return Reply(Activation("repeat", Refund.Id));

            Assert.Contains("already_active", toolResults["repeat"]);
            Assert.DoesNotContain(Refund.Content, toolResults["repeat"]);
            return Reply(new TextContent("done"));
        });
        var recorder = new RecordingMiddleware();
        IToolCallingStrategy strategy = lazy ? new LazyToolCallingStrategy() : new InstantToolCallingStrategy();
        var agent = AgentService.Initialize(client, strategy, new AgentServiceOptions
        {
            Skills = new SkillSet([Refund, Delivery], maxResults: 1),
            Middlewares = [recorder],
        });
        var updates = new List<AgentUpdate>();
        var run = agent.Submit(Request("I need a refund", stream), subscribe: handle => handle.Updated += updates.Add);

        Assert.Equal("done", (await run.Completion).Message.Text);
        Assert.Equal(3, round);
        Assert.Equal(3, recorder.Calls.Count(hook => hook == "before-tool"));
        Assert.Equal(3, recorder.AfterToolCalls.Count);
        Assert.True(recorder.AfterToolCalls[0].Result!.IsError);
        Assert.Equal(stream, updates.OfType<AgentStreamUpdate>().Any());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Inline_and_structured_invocations_preload_once_and_preserve_user_content(bool stream)
    {
        const string text = "لطفاً از $refund-policy و ($delivery-status) استفاده کن، بعد $refund-policy را توضیح بده.";
        var image = new AiImageContent("image/png", new byte[] { 1, 2, 3 });
        var latestUserMessage = new AiMessage(AiMessageRole.User, [new AiTextContent(text), image]);
        var client = new SkillChatClient((messages, options) =>
        {
            var system = Assert.Single(messages, message => message.Role == ChatRole.System).Text;
            Assert.Contains("Keep responses concise.", system);
            Assert.Contains("explicitly selected by the user", system);
            var user = messages.Last(message => message.Role == ChatRole.User);
            var parts = user.Contents.OfType<TextContent>().ToList();
            Assert.Equal(text, parts[1].Text);
            foreach (var skill in new[] { Refund, Delivery, Exchange })
                Assert.Equal(1, parts[0].Text.Split(skill.Content, StringSplitOptions.None).Length - 1);
            var attachment = Assert.Single(user.Contents.OfType<DataContent>());
            Assert.Equal(image.Data!.Value.ToArray(), attachment.Data.ToArray());
            Assert.Null(options);
            return Reply(new TextContent("done"));
        });
        var agent = AgentService.Initialize(client, new InstantToolCallingStrategy(), new AgentServiceOptions
        {
            Skills = new SkillSet([Refund, Delivery, Exchange]),
        }).WithInstructions("Keep responses concise.").WithRunRegistry(new InMemoryAgentRunRegistry());
        var request = new AgentRunRequest([latestUserMessage], stream: stream)
        {
            SkillIds = [Exchange.Id, Refund.Id, Exchange.Id],
        };

        Assert.Equal("done", (await agent.RunAsync(request)).Message.Text);
        Assert.Equal(2, latestUserMessage.Contents.Count);
        Assert.Equal(text, latestUserMessage.Text);
        Assert.Same(latestUserMessage, Assert.Single(request.Messages));
    }

    [Fact]
    public async Task Code_escaped_unknown_and_historical_mentions_do_not_force_activation()
    {
        const string input = """
            Use $refund-policy and leave $unknown, \$delivery-status, `$delivery-status`,
            https://example.org/$delivery-status alone.
            ```csharp
            $delivery-status
            ```
            ~~~text
            $delivery-status
            ~~~~

                $delivery-status
            """;
        var client = new SkillChatClient((messages, _) =>
        {
            var text = Transcript(messages);
            Assert.Contains(Refund.Content, text);
            Assert.DoesNotContain(Delivery.Content, text);
            Assert.DoesNotContain(Exchange.Content, text);
            Assert.Contains(input, text);
            return Reply(new TextContent("done"));
        });
        var agent = AgentService.Initialize(client, new InstantToolCallingStrategy(), new AgentServiceOptions
        {
            Skills = new SkillSet([Refund, Delivery, Exchange]),
        });

        await agent.RunAsync(new AgentRunRequest(
        [
            AiMessage.FromText(AiMessageRole.User, "Use $exchange-policy"),
            AiMessage.FromText(AiMessageRole.Agent, "OK"),
            AiMessage.FromText(AiMessageRole.User, input),
        ]));
    }

    [Fact]
    public async Task Unknown_structured_id_fails_before_calling_the_model()
    {
        var client = new SkillChatClient((_, _) => throw new InvalidOperationException("Unexpected model call."));
        var agent = AgentService.Initialize(client, new NoToolsStrategy(), new AgentServiceOptions
        {
            Skills = new SkillSet([Refund]),
        });
        var request = Request("Refund");
        request.SkillIds = ["missing"];

        var error = await Assert.ThrowsAsync<AgentRunException>(() => agent.RunAsync(request));

        Assert.Contains("Unknown skill id 'missing'", error.Message);
    }

    [Fact]
    public async Task Discovery_uses_only_the_latest_user_message_and_omits_empty_catalogs()
    {
        var client = new SkillChatClient((messages, options) =>
        {
            Assert.Equal(["I need a refund", "I can help.", "Greetings!"], messages.Select(message => message.Text));
            Assert.Null(options);
            return Reply(new TextContent("done"));
        });
        var agent = AgentService.Initialize(client, new NoToolsStrategy(), new AgentServiceOptions
        {
            Skills = new SkillSet([Refund]),
        });

        await agent.RunAsync(new AgentRunRequest(
        [
            AiMessage.FromText(AiMessageRole.User, "I need a refund"),
            AiMessage.FromText(AiMessageRole.Agent, "I can help."),
            AiMessage.FromText(AiMessageRole.User, "Greetings!"),
        ]));
    }

    [Fact]
    public async Task Skill_and_regular_tool_calls_are_both_handled_in_the_same_round()
    {
        var tools = new ToolRegistry().Register("lookup", "Look up data", """{"type":"object"}""",
            (call, _) => Task.FromResult(new AiToolResultContent(call.CallId, "lookup result")));
        var client = new SkillChatClient((messages, options) =>
        {
            Assert.Equal(2, options!.Tools!.Count);
            if (messages.Count == 2)
                return Reply(new FunctionCallContent("regular", "lookup"), Activation("skill", Refund.Id));

            Assert.Single(messages, message => message.Role == ChatRole.Tool);
            var results = ToolResults(messages);
            Assert.Contains(Refund.Content, results["skill"]);
            Assert.Equal("lookup result", results["regular"]);
            return Reply(new TextContent("done"));
        });
        var agent = AgentService.Initialize(client, new InstantToolCallingStrategy(), new AgentServiceOptions
        {
            Skills = new SkillSet([Refund]),
            Tools = tools,
        });

        Assert.Equal("done", (await agent.RunAsync(Request("refund"))).Message.Text);
        Assert.Single(tools.Tools);
    }

    [Fact]
    public async Task A_search_failure_warns_and_preserves_explicit_activation()
    {
        var searchError = new InvalidOperationException("Search unavailable.");
        var search = new CallbackSearch((skills, query, _, _) =>
        {
            Assert.Same(Delivery, Assert.Single(skills));
            Assert.Equal("Use $refund-policy", query);
            throw searchError;
        });
        var client = new SkillChatClient((messages, options) =>
        {
            Assert.Contains(Refund.Content, Transcript(messages));
            Assert.Null(options);
            return Reply(new TextContent("done"));
        });
        var agent = AgentService.Initialize(client, new NoToolsStrategy(), new AgentServiceOptions
        {
            Skills = new SkillSet([Refund, Delivery], search),
        });
        var warnings = new List<AgentRunWarningUpdate>();
        var run = agent.Submit(Request("Use $refund-policy"), subscribe: handle => handle.Updated += update =>
        {
            if (update is AgentRunWarningUpdate warning)
                warnings.Add(warning);
        });

        Assert.Equal("done", (await run.Completion).Message.Text);
        var warning = Assert.Single(warnings).Warning;
        Assert.Equal("Skills", warning.Source);
        Assert.Same(searchError, warning.Error);
    }

    [Fact]
    public async Task Activation_can_be_retried_if_middleware_rejects_the_result()
    {
        var client = new SkillChatClient((messages, _) =>
        {
            var results = ToolResults(messages);
            if (results.Count == 0)
                return Reply(Activation("first", Refund.Id));
            if (results.Count == 1)
            {
                Assert.Equal("Try again.", results["first"]);
                return Reply(Activation("retry", Refund.Id));
            }

            Assert.Contains(Refund.Content, results["retry"]);
            return Reply(new TextContent("done"));
        });
        var agent = AgentService.Initialize(client, new InstantToolCallingStrategy(), new AgentServiceOptions
        {
            Skills = new SkillSet([Refund]),
            Middlewares = [new RejectFirstResultMiddleware()],
        });

        Assert.Equal("done", (await agent.RunAsync(Request("refund"))).Message.Text);
    }

    [Fact]
    public async Task Cancelling_during_search_stops_the_run_before_the_model_call()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var search = new CallbackSearch(async (_, _, _, cancellationToken) =>
        {
            started.SetResult();
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return [];
        });
        var client = new SkillChatClient((_, _) => throw new InvalidOperationException("Unexpected model call."));
        var agent = AgentService.Initialize(client, new NoToolsStrategy(), new AgentServiceOptions
        {
            Skills = new SkillSet([Refund], search),
        });
        using var cancellation = new CancellationTokenSource();
        var run = agent.Submit(Request("refund"), cancellation.Token);

        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run.Completion.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task Concurrent_runs_can_each_load_the_same_skill()
    {
        var searchCount = 0;
        var bothSearching = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var search = new CallbackSearch(async (skills, _, _, cancellationToken) =>
        {
            if (Interlocked.Increment(ref searchCount) == 2)
                bothSearching.SetResult();
            await bothSearching.Task.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
            return skills.ToArray();
        });
        var client = new SkillChatClient((messages, _) =>
        {
            var user = messages.Last(message => message.Role == ChatRole.User).Text;
            var results = ToolResults(messages);
            if (results.Count == 0)
                return Reply(Activation(user, Refund.Id));
            Assert.Contains(Refund.Content, results[user]);
            return Reply(new TextContent(user));
        });
        var agent = AgentService.Initialize(client, new LazyToolCallingStrategy(), new AgentServiceOptions
        {
            Skills = new SkillSet([Refund], search),
        });

        var responses = await Task.WhenAll(agent.RunAsync(Request("first")), agent.RunAsync(Request("second", stream: true)));

        Assert.Equal(["first", "second"], responses.Select(response => response.Message.Text));
    }

    private static AgentRunRequest Request(string text, bool stream = false) =>
        new([AiMessage.FromText(AiMessageRole.User, text)], stream: stream);

    private static FunctionCallContent Activation(string callId, string skillId) =>
        new(callId, ActivateSkill, new Dictionary<string, object?> { ["id"] = skillId });

    private static ChatResponse Reply(params AIContent[] contents) =>
        new(new ChatMessage(ChatRole.Assistant, contents));

    private static string Transcript(IReadOnlyList<ChatMessage> messages) =>
        string.Join("\n", messages.Select(message => message.Text).Concat(ToolResults(messages).Values));

    private static Dictionary<string, string> ToolResults(IReadOnlyList<ChatMessage> messages) =>
        messages.SelectMany(message => message.Contents).OfType<FunctionResultContent>()
            .ToDictionary(result => result.CallId, result => Assert.IsType<string>(result.Result));

    private sealed class RejectFirstResultMiddleware : AgentMiddleware
    {
        public override Task<AgentResult<AiToolResultContent?>> OnAfterToolCallAsync(
            AgentRunContext context, AiToolCallContent call, AiToolResultContent? result,
            Exception? error = null, CancellationToken cancellationToken = default)
        {
            if (call.CallId == "first")
                result = new AiToolResultContent(call.CallId, "Try again.", isError: true);

            return Task.FromResult(AgentResult<AiToolResultContent?>.Ok(result));
        }
    }

    private sealed class CallbackSearch(
        Func<IEnumerable<SkillDto>, string, int, CancellationToken, Task<IReadOnlyList<SkillDto>>> search) : ISkillSearchProvider
    {
        public Task<IReadOnlyList<SkillDto>> SearchAsync(
            IEnumerable<SkillDto> skills, string query, int maxResults, CancellationToken cancellationToken = default) =>
            search(skills, query, maxResults, cancellationToken);
    }

    private sealed class SkillChatClient(Func<IReadOnlyList<ChatMessage>, ChatOptions?, ChatResponse> respond) : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(respond(messages.ToList(), options));
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
