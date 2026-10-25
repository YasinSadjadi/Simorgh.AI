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
    public override Task OnStartupAsync(AgentRunContext context, CancellationToken cancellationToken = default)
    {
        var telemetry = new AgentRunTelemetry { RunId = context.RunId };
        var stopwatch = Stopwatch.StartNew();
        context.Items[TelemetryItemKey] = new Collector(telemetry, stopwatch);
        logger?.LogInformation("Agent run {RunId} started.", context.RunId);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public override async Task<AiMessage> InvokeResponseAsync(AgentRunContext context, AgentResponseDelegate next)
    {
        var collector = GetCollector(context);
        collector?.ModelStopwatch.Restart();
        try
        {
            var response = await next(context).ConfigureAwait(false);
            collector?.ModelStopwatch.Stop();

            var usage = response.Contents.OfType<AiUsageContent>().FirstOrDefault();
            collector?.ModelRoundFinished(usage);

            logger?.LogInformation(
                "Agent run {RunId} model round {Round} completed in {Duration} ms (input: {InputTokens} tokens, output: {OutputTokens} tokens).",
                context.RunId, collector?.Telemetry.ModelRounds, collector?.ModelStopwatch.ElapsedMilliseconds,
                usage?.InputTokens, usage?.OutputTokens);
            return response;
        }
        catch (OperationCanceledException)
        {
            if (collector is not null) collector.Telemetry.Cancelled = true;
            throw;
        }
        catch (Exception ex)
        {
            if (collector is not null && collector.Telemetry.Error is null) collector.Telemetry.Error = ex.Message;
            throw;
        }
    }

    /// <inheritdoc />
    public override async Task<AiToolResultContent> InvokeToolAsync(
        AgentRunContext context, AiToolCallContent call, AgentToolInvokeDelegate next)
    {
        var collector = GetCollector(context);
        var stopwatch = collector is null ? null : Stopwatch.StartNew();
        AiToolResultContent result;
        try
        {
            result = await next(context, call).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            if (collector is not null) collector.Telemetry.Cancelled = true;
            throw;
        }
        catch (Exception ex)
        {
            stopwatch?.Stop();
            collector?.ToolFinished(call.Name, stopwatch?.Elapsed ?? TimeSpan.Zero, isError: true);
            logger?.LogError(ex, "Agent run {RunId} tool '{ToolName}' threw after {Duration} ms.",
                context.RunId, call.Name, stopwatch?.ElapsedMilliseconds ?? 0);
            throw;
        }

        stopwatch?.Stop();
        collector?.ToolFinished(call.Name, stopwatch?.Elapsed ?? TimeSpan.Zero, result.IsError);
        var durationMs = stopwatch?.ElapsedMilliseconds ?? 0;
        if (result.IsError)
            logger?.LogWarning("Agent run {RunId} tool '{ToolName}' failed in {Duration} ms: {Error}",
                context.RunId, call.Name, durationMs, result.Result);
        else
            logger?.LogInformation("Agent run {RunId} tool '{ToolName}' completed in {Duration} ms.",
                context.RunId, call.Name, durationMs);
        return result;
    }

    /// <inheritdoc />
    public override Task OnEndAsync(AgentRunContext context, CancellationToken cancellationToken = default)
    {
        if (context.Items[TelemetryItemKey] is not Collector collector)
            return Task.CompletedTask;

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
        return Task.CompletedTask;
    }

    private static Collector? GetCollector(AgentRunContext context)
    {
        return context.Items.TryGetValue(TelemetryItemKey, out var value) ? value as Collector : null;
    }

    /// <summary>Mutable per-run state kept in <see cref="AgentRunContext.Items" />.</summary>
    private sealed class Collector(AgentRunTelemetry telemetry, Stopwatch runStopwatch)
    {
        public AgentRunTelemetry Telemetry { get; } = telemetry;

        public Stopwatch ModelStopwatch { get; } = new();

        public void ModelRoundFinished(AiUsageContent? usage)
        {
            Telemetry.ModelRounds++;
            Telemetry.ModelDuration += ModelStopwatch.Elapsed;
            if (usage is null) return;
            Telemetry.InputTokens += usage.InputTokens;
            Telemetry.OutputTokens += usage.OutputTokens;
            AgentTelemetryMeter.ModelCallDuration.Record(ModelStopwatch.Elapsed.TotalSeconds);
            if (usage.InputTokens > 0) AgentTelemetryMeter.InputTokens.Add(usage.InputTokens);
            if (usage.OutputTokens > 0) AgentTelemetryMeter.OutputTokens.Add(usage.OutputTokens);
        }

        public void ToolFinished(string name, TimeSpan duration, bool isError)
        {
            Telemetry.ToolCalls++;
            Telemetry.ToolDuration += duration;
            if (isError) Telemetry.ToolErrors++;
            AgentTelemetryMeter.ToolCalls.Add(1,
                new KeyValuePair<string, object?>("tool_name", name),
                new KeyValuePair<string, object?>("outcome", isError ? "error" : "ok"));
            AgentTelemetryMeter.ToolCallDuration.Record(duration.TotalSeconds);
        }

        public AgentRunTelemetry Finish()
        {
            runStopwatch.Stop();
            Telemetry.Duration = runStopwatch.Elapsed;
            return Telemetry;
        }
    }
}
