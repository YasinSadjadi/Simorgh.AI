namespace Simorgh.AI.Abstractions.Tools;

/// <summary>
///     A tool service: a group of tools exposed to agents. The service itself declares its
///     identifier and description. Tools may be public methods annotated with
///     <see cref="AiToolMethodAttribute" /> or supplied by a dynamic tool service.
/// </summary>
public interface IToolService
{
    /// <summary>The identifier of this tool service.</summary>
    string Identifier { get; }

    /// <summary>What this service offers; tool-calling strategies decide how to use it.</summary>
    string Description { get; }
}
