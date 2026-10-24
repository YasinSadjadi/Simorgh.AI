namespace Simorgh.AI.Abstractions.Tools;

/// <summary>
///     What a model is told about a tool: its identifier, description, and optionally a
///     JSON Schema describing its arguments.
/// </summary>
/// <param name="identifier">The identifier the model uses to call the tool.</param>
/// <param name="description">What the tool does, shown to the model.</param>
/// <param name="jsonSchema">JSON Schema describing the tool's arguments, when available.</param>
public sealed class AiToolDeclaration(string identifier, string description, string? jsonSchema = null)
{
    /// <summary>The identifier the model uses to call the tool.</summary>
    public string Identifier { get; } = identifier;

    /// <summary>What the tool does, shown to the model.</summary>
    public string Description { get; } = description;

    /// <summary>JSON Schema describing the tool's arguments, when available.</summary>
    public string? JsonSchema { get; } = jsonSchema;
}
