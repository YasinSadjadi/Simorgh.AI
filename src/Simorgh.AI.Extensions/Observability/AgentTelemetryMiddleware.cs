using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Simorgh.AI.Abstractions.Agent;
using Simorgh.AI.Abstractions.Agent.Middleware;
using Simorgh.AI.Abstractions.Contents;
using Simorgh.AI.Abstractions.Messages;

namespace Simorgh.AI.Extensions.Observability;

/// <summary>
///     Observability middleware: logs each run, model round, and tool invocation through
///     <see cref="ILogger" />, publishes metrics on the <see cref="AgentTelemetryMeter" />
///     meter, and raises <see cref="RunCompleted" /> with an <see cref="AgentRunTelemetry" />
///     summary. The live summary is also available to later middleware via
///     <see cref="AgentRunContext.Items" /> under the <see cref="TelemetryItemKey" /> key.
///     A single instance may be shared across concurrent runs; per-run state lives in the
///     run context.
/// </summary>
public sealed class AgentTelemetryMiddleware(
    ILogger<AgentTelemetryMiddleware>? logger = null,
    Action<AgentRunTelemetry>? onRunCompleted = null)
    : AgentMiddleware
{
    /// <summary>Key under which the run's <see cref="AgentRunTelemetry" /> is stored in <see cref="AgentRunContext.Items" />.</summary>
    public const string TelemetryItemKey = "Simorgh.AI.Telemetry";

    /// <summary>Raised once when a run ends, whether it succeeded, failed, or was cancelled.</summary>
    public event Action<AgentRunTelemetry>? RunCompleted = onRunCompleted;

    /// <inheritdoc />
    public override Task<AgentResult> OnStartupAsync(
        AgentRunContext context, CancellationToken cancellationToken = default)
    {
        var telemetry = new AgentRunTelemetry { RunId = context.RunId };
        var stopwatch = Stopwatch.StartNew();
        context.Items[TelemetryItemKey] = new Collector(telemetry, stopwatch);
        // Middleware failures that do not break the run are reported as warnings; log them
        // as they happen so they are not lost even when the run's consumers ignore them.
        context.WarningReported += warning => logger?.LogWarning(
            "Agent run {RunId} warning from {Source}: {Message}",
            context.RunId, warning.Source, warning.Message);
        logger?.LogInformation("Agent run {RunId} started.", context.RunId);
        return Task.FromResult(AgentResult.Ok());
    }

    /// <inheritdoc />
    public override Task<AgentResult> OnBeforeAgentResponseAsync(
        AgentRunContext context, CancellationToken cancellationToken = default)
    {
        logger?.LogInformation("Agent run {RunId} model round starting.", context.RunId);
        return Task.FromResult(AgentResult.Ok());
    }

    /// <inheritdoc />
    public override Task<AgentResult<AiMessage>> OnAfterAgentResponseAsync(
        AgentRunContext context, AiMessage response, AgentResponseUsage? usage,
        Exception? error = null, CancellationToken cancellationToken = default)
    {
        var collector = GetCollector(context);
        collector?.ModelRoundFinished(usage);

        if (error is not null)
        {
            collector?.RecordError(error.Message);
            logger?.LogWarning(
                "Agent run {RunId} model round {Round} failed: {Error}",
                context.RunId, collector?.Telemetry.ModelRounds, error.Message);
            return Task.FromResult(AgentResult<AiMessage>.Ok(response));
        }

        logger?.LogInformation(
            "Agent run {RunId} model round {Round} completed (input: {InputTokens} tokens, cached: {CachedTokens} tokens, output: {OutputTokens} tokens, reasoning: {ReasoningTokens} tokens).",
            context.RunId, collector?.Telemetry.ModelRounds, usage?.InputTokens,
            usage?.CachedInputTokens, usage?.OutputTokens, usage?.ReasoningOutputTokens);
        return Task.FromResult(AgentResult<AiMessage>.Ok(response));
    }

    /// <inheritdoc />
    public override Task<AgentResult<AiToolResultContent?>> OnAfterToolCallAsync(
        AgentRunContext context, AiToolCallContent call, AiToolResultContent? result,
        Exception? error = null, CancellationToken cancellationToken = default)
    {
        var collector = GetCollector(context);
        var isError = error is not null || result?.IsError == true;
        collector?.ToolFinished(call.Name, isError);
        if (isError)
            logger?.LogWarning("Agent run {RunId} tool '{ToolName}' failed: {Error}",
                context.RunId, call.Name, error?.Message ?? result?.Result);
        else
            logger?.LogInformation("Agent run {RunId} tool '{ToolName}' completed.",
                context.RunId, call.Name);
        return Task.FromResult(AgentResult<AiToolResultContent?>.Ok(result));
    }

    /// <inheritdoc />
    public override Task<AgentResult> OnEndAsync(
        AgentRunContext context, AgentRunEnd end, CancellationToken cancellationToken = default)
    {
        if (context.Items[TelemetryItemKey] is not Collector collector)
            return Task.FromResult(AgentResult.Ok());

        if (end.Reason == AgentRunEndReason.Cancelled)
            collector.MarkCancelled();
        if (end.Error is not null)
            collector.RecordError(end.Error.Message);

        var telemetry = collector.Finish();
        var outcome = telemetry.Cancelled ? "cancelled" : telemetry.Error is null ? "completed" : "failed";
        var tags = new KeyValuePair<string, object?>("outcome", outcome);

        AgentTelemetryMeter.Runs.Add(1, tags);
        AgentTelemetryMeter.RunDuration.Record(telemetry.Duration.TotalSeconds, tags);
        AgentTelemetryMeter.ModelRounds.Add(telemetry.ModelRounds, tags);

        if (telemetry.Error is not null || telemetry.Cancelled)
            logger?.LogWarning(
                "Agent run {RunId} {Outcome} after {Duration} ms: {Error}. Rounds: {Rounds}, tool calls: {ToolCalls} ({ToolErrors} failed), tokens: {TotalTokens}.",
                telemetry.RunId, outcome, telemetry.Duration.TotalMilliseconds,
                telemetry.Error ?? "cancelled", telemetry.ModelRounds, telemetry.ToolCalls,
                telemetry.ToolErrors, telemetry.TotalTokens);
        else
            logger?.LogInformation(
                "Agent run {RunId} completed in {Duration} ms. Rounds: {Rounds}, tool calls: {ToolCalls} ({ToolErrors} failed), tokens: {TotalTokens} (input: {InputTokens}, output: {OutputTokens}).",
                telemetry.RunId, telemetry.Duration.TotalMilliseconds, telemetry.ModelRounds,
                telemetry.ToolCalls, telemetry.ToolErrors, telemetry.TotalTokens,
                telemetry.InputTokens, telemetry.OutputTokens);

        RunCompleted?.Invoke(telemetry);
        return Task.FromResult(AgentResult.Ok());
    }

    private static Collector? GetCollector(AgentRunContext context)
    {
        return context.Items.TryGetValue(TelemetryItemKey, out var value) ? value as Collector : null;
    }

    /// <summary>Mutable per-run state kept in <see cref="AgentRunContext.Items" />.</summary>
    private sealed class Collector(AgentRunTelemetry telemetry, Stopwatch runStopwatch)
    {
        public AgentRunTelemetry Telemetry { get; } = telemetry;

        public void RecordError(string message)
        {
            Telemetry.Error ??= message;
        }

        public void MarkCancelled()
        {
            Telemetry.Cancelled = true;
        }

        public void ModelRoundFinished(AgentResponseUsage? usage)
        {
            Telemetry.ModelRounds++;
            if (usage is null) return;
            Telemetry.InputTokens += usage.InputTokens;
            Telemetry.OutputTokens += usage.OutputTokens;
            if (usage.InputTokens > 0) AgentTelemetryMeter.InputTokens.Add(usage.InputTokens);
            if (usage.OutputTokens > 0) AgentTelemetryMeter.OutputTokens.Add(usage.OutputTokens);
            if (usage.CachedInputTokens is > 0) AgentTelemetryMeter.CachedInputTokens.Add(usage.CachedInputTokens.Value);
        }

        public void ToolFinished(string name, bool isError)
        {
            Telemetry.ToolCalls++;
            if (isError) Telemetry.ToolErrors++;
            AgentTelemetryMeter.ToolCalls.Add(1,
                new KeyValuePair<string, object?>("tool_name", name),
                new KeyValuePair<string, object?>("outcome", isError ? "error" : "ok"));
        }

        public AgentRunTelemetry Finish()
        {
            runStopwatch.Stop();
            Telemetry.Duration = runStopwatch.Elapsed;
            return Telemetry;
        }
    }
}
