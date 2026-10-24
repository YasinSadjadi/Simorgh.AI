namespace Simorgh.AI.Abstractions.Tools;

/// <summary>
///     A tool service: a group of tools exposed to agents. The service itself declares its
///     identifier and description; each tool is a public method annotated with
///     <see cref="AiToolMethodAttribute" />.
/// </summary>
public interface IToolService
{
    /// <summary>The identifier of this tool service.</summary>
    string Identifier { get; }

    /// <summary>What this tool service offers, shown to the model.</summary>
    string Description { get; }
}