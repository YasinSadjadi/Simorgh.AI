using Microsoft.Extensions.AI;
using Simorgh.AI.Abstractions.Contents;
using Simorgh.AI.Abstractions.Tools;

namespace Simorgh.AI.Core.Tools;

/// <summary>
///     A tool registered with the agent, combining what the model sees
///     (<see cref="Declaration" />) with how the call is executed (<see cref="InvokeAsync" />).
/// </summary>
/// <param name="identifier">The identifier the model uses to call this tool.</param>
/// <param name="description">What the tool does, shown to the model.</param>
/// <param name="service">The service that owns the tool.</param>
/// <param name="attribute">The attribute that introduced this tool.</param>
/// <param name="declaration">
///     The tool as advertised to the model via <see cref="ChatOptions.Tools" />. It is a
///     non-invocable declaration, so the client never bypasses the agent's own tool pipeline.
/// </param>
/// <param name="invokeAsync">Executes a call to this tool and returns the result for the model.</param>
public sealed class ToolDefinition(
    string identifier,
    string description,
    IToolService service,
    AiToolMethodAttribute attribute,
    AIFunctionDeclaration declaration,
    Func<AiToolCallContent, CancellationToken, Task<AiToolResultContent>> invokeAsync)
{
    /// <summary>The identifier the model uses to call this tool.</summary>
    public string Identifier { get; } = identifier;

    /// <summary>What the tool does, shown to the model.</summary>
    public string Description { get; } = description;

    /// <summary>The service that owns the tool.</summary>
    public IToolService Service { get; } = service;

    /// <summary>The attribute that introduced this tool.</summary>
    public AiToolMethodAttribute Attribute { get; } = attribute;

    /// <summary>
    ///     The tool as advertised to the model via <see cref="ChatOptions.Tools" />. It is a
    ///     non-invocable declaration, so the client never bypasses the agent's own tool pipeline.
    /// </summary>
    public AIFunctionDeclaration Declaration { get; } = declaration;

    /// <summary>Executes a call to this tool and returns the result for the model.</summary>
    public Func<AiToolCallContent, CancellationToken, Task<AiToolResultContent>> InvokeAsync { get; } = invokeAsync;
}