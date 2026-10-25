using Simorgh.AI.Abstractions.Agent;
using Simorgh.AI.Abstractions.Agent.Cancellation;
using Simorgh.AI.Abstractions.Agent.Middleware;
using Simorgh.AI.Core.Tools;

namespace Simorgh.AI.Core.Agent;

/// <summary>
///     Configuration for an <see cref="AgentService" />. All service-level settings live
///     here; per-run inputs (messages, stream, run id, max output tokens) belong to
///     <see cref="AgentRunRequest" /> instead.
/// </summary>
public sealed class AgentServiceOptions
{
    /// <summary>Tools the agent can call; <c>null</c> means the agent runs without tools.</summary>
    public ToolRegistry? Tools { get; set; }

    /// <summary>Optional middleware hooks around every run.</summary>
    public IEnumerable<AgentMiddleware>? Middlewares { get; set; }

    /// <summary>
    ///     Optional system instructions that shape the agent's behavior; sent as the
    ///     system message at the start of every run.
    /// </summary>
    public string? Instructions { get; set; }

    /// <summary>
    ///     Optional registry through which submitted runs can be cancelled by id,
    ///     including from other servers in a multi-server deployment.
    /// </summary>
    public IAgentRunRegistry? RunRegistry { get; set; }

    /// <summary>
    ///     Total token capacity of the model's context window, tracked on every run's
    ///     <see cref="AgentRunContext" />. When <c>null</c>, the window is treated as
    ///     unknown: no percentage is computed until a middleware sets it explicitly.
    /// </summary>
    public long? ContextWindowTokens { get; set; }
}
