# Simorgh.AI

Simorgh.AI is a .NET AI framework under development. The solution is split into small class libraries so its public contracts, framework implementation, and integrations can evolve independently and be published as NuGet packages.

## Projects

- `Simorgh.AI.Abstractions`: public contracts shared by the framework and integrations; references `Microsoft.Extensions.AI.Abstractions`.
- `Simorgh.AI.Core`: the framework's core implementation; references Abstractions.
- `Simorgh.AI.Integrations`: provider and service integrations; references Abstractions.
- `Simorgh.AI.Extensions`: ready-made tool-calling strategies and observability (`AgentTelemetryMiddleware`: ILogger-based run/round/tool logging, token accounting, and `System.Diagnostics.Metrics` metrics on the `Simorgh.AI` meter, OpenTelemetry-compatible).

All projects currently target .NET 10. Package IDs follow the project names and start at version `0.1.0`.
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

## Build and package

```powershell
dotnet restore Simorgh.AI.sln
dotnet build Simorgh.AI.sln
dotnet pack Simorgh.AI.sln -c Release
```

The generated NuGet packages are written to each project's `bin/Release` directory.

The solution and package prefix are initial names and can be changed as the framework's feature set is defined.
