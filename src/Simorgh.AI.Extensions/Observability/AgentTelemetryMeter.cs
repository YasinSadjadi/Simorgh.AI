using System.Diagnostics.Metrics;

namespace Simorgh.AI.Extensions.Observability;

/// <summary>
///     The <c>Simorgh.AI</c> <see cref="Meter" /> and its instruments. Any listener
///     (OpenTelemetry, dotnet-counters, MeterListener) that subscribes to the meter
///     named <c>Simorgh.AI</c> receives the metrics published by
///     <see cref="AgentTelemetryMiddleware" />.
/// </summary>
public static class AgentTelemetryMeter
{
    /// <summary>Meter name to subscribe to: <c>Simorgh.AI</c>.</summary>
    public const string MeterName = "Simorgh.AI";

    /// <summary>Unit used for token instruments: <c>{token}</c>.</summary>
    public const string TokenUnit = "{token}";

    private static readonly Meter Meter = new(MeterName, typeof(AgentTelemetryMeter).Assembly.GetName().Version?.ToString());

    /// <summary>Total agent runs by outcome (<c>completed</c>, <c>failed</c>, <c>cancelled</c>).</summary>
    public static readonly Counter<long> Runs = Meter.CreateCounter<long>(
        "simorgh.agent.runs", unit: "{run}", description: "Agent runs by outcome.");

    /// <summary>Agent run duration.</summary>
    public static readonly Histogram<double> RunDuration = Meter.CreateHistogram<double>(
        "simorgh.agent.run.duration", unit: "s", description: "Wall-clock duration of an agent run.");

    /// <summary>Model rounds executed.</summary>
    public static readonly Counter<long> ModelRounds = Meter.CreateCounter<long>(
        "simorgh.agent.model.rounds", unit: "{round}", description: "Model rounds executed by agent runs.");

    /// <summary>Model call duration.</summary>
    public static readonly Histogram<double> ModelCallDuration = Meter.CreateHistogram<double>(
        "simorgh.agent.model.call_duration", unit: "s", description: "Duration of a single model call.");

    /// <summary>Tool invocations by tool name and outcome (<c>ok</c>, <c>error</c>).</summary>
    public static readonly Counter<long> ToolCalls = Meter.CreateCounter<long>(
        "simorgh.agent.tool.calls", unit: "{call}", description: "Tool invocations by name and outcome.");

    /// <summary>Tool invocation duration.</summary>
    public static readonly Histogram<double> ToolCallDuration = Meter.CreateHistogram<double>(
        "simorgh.agent.tool.call_duration", unit: "s", description: "Duration of a single tool invocation.");

    /// <summary>Input tokens consumed.</summary>
    public static readonly Counter<long> InputTokens = Meter.CreateCounter<long>(
        "simorgh.agent.tokens.input", unit: TokenUnit, description: "Input tokens consumed by agent runs.");

    /// <summary>Output tokens produced.</summary>
    public static readonly Counter<long> OutputTokens = Meter.CreateCounter<long>(
        "simorgh.agent.tokens.output", unit: TokenUnit, description: "Output tokens produced by agent runs.");

    /// <summary>Input tokens served from the provider's cache.</summary>
    public static readonly Counter<long> CachedInputTokens = Meter.CreateCounter<long>(
        "simorgh.agent.tokens.input.cached", unit: TokenUnit,
        description: "Input tokens served from the provider's cache.");
}
