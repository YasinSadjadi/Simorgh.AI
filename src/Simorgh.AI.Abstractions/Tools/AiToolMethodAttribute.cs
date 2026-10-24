namespace Simorgh.AI.Abstractions.Tools;

/// <summary>
///     Introduces a tool from a public method of an <see cref="IToolService" />.
///     Method parameters become the tool's arguments; JSON-serializable parameter and
///     return types are recommended.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class AiToolMethodAttribute(string toolName, string description) : Attribute
{
    /// <summary>The identifier the model uses to call this tool.</summary>
    public string ToolName { get; } = toolName;

    /// <summary>What the tool does, shown to the model.</summary>
    public string Description { get; } = description;
}