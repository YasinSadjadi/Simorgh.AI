# Simorgh.AI

Simorgh.AI is a .NET framework for building AI agents with streaming, tool calling, and model routing.

[Source code on GitHub](https://github.com/YasinSadjadi/Simorgh.AI) · [Report an issue](https://github.com/YasinSadjadi/Simorgh.AI/issues)

## Projects

- `Simorgh.AI.Abstractions`: public contracts shared by the framework and integrations; references `Microsoft.Extensions.AI.Abstractions`.
- `Simorgh.AI.Core`: the framework's core implementation; references Abstractions.
- `Simorgh.AI.Integrations`: provider and service integrations; references Abstractions.
- `Simorgh.AI.Extensions`: ready-made tool-calling strategies and observability (`AgentTelemetryMiddleware`: ILogger-based run/round/tool logging, token accounting, and `System.Diagnostics.Metrics` metrics on the `Simorgh.AI` meter, OpenTelemetry-compatible).
- `Simorgh.AI.Routing`: an `IChatClient` adapter and strategies for routing requests between models.

The packages target .NET Standard 2.0 and .NET 8. Package IDs follow the project names above.
Shared NuGet package metadata is configured in `Directory.Build.props`; dependency versions are managed centrally in `Directory.Packages.props`.

## Streaming

Submit a run with `Stream = true` to receive the answer incrementally. Text deltas arrive as `AgentStreamUpdate` on the run's `Updated` event while the model is still generating; the middleware hooks and pipeline are unchanged, and the accumulated final answer is still delivered via `AgentRunCompletedUpdate` and `Completion`.

```csharp
var run = agent.Submit(new AgentRunRequest(messages, stream: true));
run.Updated += update =>
{
    if (update is AgentStreamUpdate delta)
        Console.Write(delta.TextDelta);
};
var answer = await run.Completion;
```

## Skills

Give an agent a collection of text instructions that it can discover and load when relevant.

```csharp
using Simorgh.AI.Abstractions.Agent;
using Simorgh.AI.Abstractions.Messages;
using Simorgh.AI.Abstractions.Skills;
using Simorgh.AI.Core.Agent;
using Simorgh.AI.Core.Skills;
using Simorgh.AI.Extensions.Tools;

var refundPolicy = new SkillDto(
    id: "refund-policy",
    title: "Refund policy",
    whenToUse: "Use when a customer asks about refund eligibility or the refund process.",
    tags: ["refund", "billing"],
    content: "Ask for the order date and payment method before explaining refund eligibility.");

var agent = AgentService.Initialize(chatClient, new InstantToolCallingStrategy(), new AgentServiceOptions
{
    Skills = new SkillSet([refundPolicy])
});

var request = new AgentRunRequest(
    [AiMessage.FromText(AiMessageRole.User, "Please use $refund-policy to explain my options.")]);
var answer = await agent.RunAsync(request);
```

For a request such as “How do I get a refund?”, the agent searches the latest user message at the
start of the run. It sends the model each candidate's id, title, and `WhenToUse`. When a skill is
relevant, the model calls `activate_skill` to read its instructions. That tool name is reserved
on agents configured with skills and is available in both Instant and Lazy tool-calling modes.

An inline `$skill-id` in the latest user message loads the named skill before the first model call.
The chat UI can also set `AgentRunRequest.SkillIds` to the ids selected by the user. Unknown inline
mentions, escaped mentions, and mentions inside Markdown code are treated as ordinary text. An
unknown id in `SkillIds` fails the run. Each skill needs a unique id of up to 64 lowercase ASCII
letters or digits, with single hyphens allowed between groups.

`SkillSet` takes a snapshot of an application-provided `IEnumerable<SkillDto>`, including a query
passed as `IQueryable<SkillDto>`. The default provider scans the title, tags, usage guidance, and
content in memory and returns up to five ranked matches. Set `maxResults` to change the limit or
pass your own `ISkillSearchProvider`. Skill search failures surface as run warnings.

## Runtime tools

Tools obtained from an API or another source can be registered alongside `IToolService`
instances. Supply the full identifier, description, argument JSON Schema, and a handler:

```csharp
using Simorgh.AI.Abstractions.Contents;
using Simorgh.AI.Core.Tools;

var tools = new ToolRegistry();
tools.Register("weather/lookup", "Look up the weather", """
    {"type":"object","properties":{"city":{"type":"string"}},"required":["city"]}
    """, async (call, cancellationToken) =>
    {
        // Forward call.ArgumentsJson to an API, invoke local code, or use another executor.
        var output = await ExecuteWeatherAsync(call.ArgumentsJson, cancellationToken);
        return new AiToolResultContent(call.CallId, output);
    });
// Pass tools as AgentServiceOptions.Tools when initializing the agent.
```

The schema describes the arguments exposed to the model; handlers are responsible for
validating inputs and returning a result with the original call ID. Exceptions flow through
the agent's normal tool error handling and middleware. Runtime tools use the same tool-calling
strategies as service tools, including lazy discovery and loading. Identifiers must be unique
across all kinds of registration. Register tools before starting runs; a run snapshots the
available declarations at startup.

Alternatively, implement `IDynamicToolHandler` to keep execution logic in a separate class
and pass an instance to the same `Register` method:

```csharp
using Simorgh.AI.Abstractions.Tools;

tools.Register("weather/get", "Get the weather", toolSchema, new WeatherToolHandler());

public sealed class WeatherToolHandler : IDynamicToolHandler
{
    public Task<AiToolResultContent> InvokeAsync(
        AiToolCallContent call, CancellationToken cancellationToken = default)
    {
        // Validate call.ArgumentsJson and execute the tool here.
        return Task.FromResult(new AiToolResultContent(call.CallId, "Sunny"));
    }
}
```

### Groups of existing AI tools

Wrap existing `Microsoft.Extensions.AI.AITool` instances in a `DynamicToolService`:

```csharp
using Microsoft.Extensions.AI;
using Simorgh.AI.Core.Tools;

var currentWeatherTool = AIFunctionFactory.Create(
    (string city) => $"Sunny in {city}",
    name: "current", description: "Get the current weather");
var forecastTool = AIFunctionFactory.Create(
    (string city) => $"Rain tomorrow in {city}",
    name: "forecast", description: "Get tomorrow's forecast");

var service = new DynamicToolService(
    identifier: "weather",
    description: "Tools for weather information",
    tools: [currentWeatherTool, forecastTool]);

var registry = new ToolRegistry().Register(service);
// Pass registry as AgentServiceOptions.Tools.
```

Each tool is registered as `serviceIdentifier/functionName`, such as `weather/current`.
The collection is copied, and the original functions are not renamed or disposed. Every
entry must be an invocable `AIFunction`; declaration-only and provider-hosted tools are
rejected at registration. A rejected service adds no tools to the registry.

Dynamic services implement `IToolService` and can be registered alongside attributed
services, delegate handlers, and `IDynamicToolHandler` instances. Calls go through the
normal middleware pipeline before invoking the original function with its argument
binding, serializer options, and cancellation token. Results are serialized to the
framework's string tool-result format, retaining the original call ID.

The group description stays separate from each function's description. Strategies can
inspect registered groups through `context.ToolServices` (`AiToolServiceDeclaration`
objects containing `Identifier`, `Description`, and `Tools`); this metadata is snapshotted
before startup and is also available as `registry.Services`. Standalone runtime tools stay
in the flat `availableTools` list. The `IToolCallingStrategy` contract is unchanged:
`Instant` advertises all tools, and `Lazy` continues to discover and load individual tools.
A custom strategy can use group metadata to decide which tools to expose or load.

## Build and package

```powershell
dotnet restore Simorgh.AI.sln
dotnet build Simorgh.AI.sln
dotnet pack Simorgh.AI.sln -c Release
```

The generated NuGet packages are written to each project's `bin/Release` directory.

The solution and package prefix are initial names and can be changed as the framework's feature set is defined.

## Model routing

`Simorgh.AI.Routing` provides an `IChatClient` adapter and extensible routing strategies.
Provide existing chat clients; the routing client does not own or dispose them.

```csharp
using Simorgh.AI.Routing;
using Simorgh.AI.Core.Agent;
using Simorgh.AI.Routing.Extensions;
using Simorgh.AI.Routing.Strategies;
using Simorgh.AI.Routing.Models;

var first = new RoutingTarget(primary, name: "primary", provider: "provider-a", model: "model-a");
var second = new RoutingTarget(backup, name: "backup", provider: "provider-b", model: "model-b");
var fallback = new FallbackRoutingStrategy(new[] { first, second });
var weighted = new WeightedRoutingStrategy(new[] {
    new WeightedTarget(first, weight: 3), new WeightedTarget(second, weight: 1)
});
// var mixture = new MixtureOfAgentsRoutingStrategy(
//     new[] { first, second }, judge, "Synthesize the strongest answer.");

// Adapt a strategy to IChatClient and initialize an agent.
IChatClient router = new RoutingChatClient(weighted);
var agent = AgentService.Initialize(fallback.AsChatClient(), toolCallingStrategy, agentOptions);
```

Each target carries an `IChatClient` and optional identity information. Strategy-specific
configuration uses its own types: weighted routing accepts `WeightedTarget` values,
and custom strategies can define their own
configuration types. Applications can deserialize their own JSON configuration into
these types after creating the chat clients.

Fallback tries clients in order for timeouts and transient HTTP errors (408, 429,
5xx, or transport errors). Other errors are propagated immediately. Pass a
`shouldFallback` predicate to customize this policy. After a streaming update is emitted,
later errors are propagated. Weighted selection uses relative positive weights. All failed fallback attempts are reported as an aggregate error.
The mixture-of-agents strategy is temporarily disabled.
