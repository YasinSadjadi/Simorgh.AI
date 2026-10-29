using Simorgh.AI.Abstractions.Contents;

namespace Simorgh.AI.Abstractions.Tools;

/// <summary>Executes a tool registered from a runtime definition.</summary>
public interface IDynamicToolHandler
{
    /// <summary>
    /// Executes the call and returns its result with the same call ID. Implementations are
    /// responsible for validating the arguments and may use any execution mechanism.
    /// </summary>
    Task<AiToolResultContent> InvokeAsync(AiToolCallContent call, CancellationToken cancellationToken = default);
}
