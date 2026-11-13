namespace Simorgh.AI.Abstractions.Tools;

/// <summary>
///     Describes a registered group of tools to a tool-calling strategy. The strategy
///     decides whether to use the group description for discovery, loading, or model input.
/// </summary>
public sealed class AiToolServiceDeclaration
{
    /// <summary>Creates a service declaration with a snapshot of its tool declarations.</summary>
    /// <param name="identifier">The service identifier.</param>
    /// <param name="description">The description of the group, separate from individual tools.</param>
    /// <param name="tools">The tools registered by this service.</param>
    public AiToolServiceDeclaration(string identifier, string description, IEnumerable<AiToolDeclaration> tools)
    {
        if (tools is null) throw new ArgumentNullException(nameof(tools));
        Identifier = identifier;
        Description = description;
        Tools = Array.AsReadOnly(tools.ToArray());
    }

    /// <summary>The service identifier.</summary>
    public string Identifier { get; }

    /// <summary>Group metadata; it is not automatically appended to tool descriptions.</summary>
    public string Description { get; }

    /// <summary>The tools registered by this service, with their complete identifiers.</summary>
    public IReadOnlyList<AiToolDeclaration> Tools { get; }
}
