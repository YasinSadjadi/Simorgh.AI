using Microsoft.Extensions.AI;
using Simorgh.AI.Abstractions.Tools;

namespace Simorgh.AI.Core.Tools;

/// <summary>
///     A named group of runtime tools. Register it with <see cref="ToolRegistry" />
///     to execute its functions through the same pipeline as attributed service tools.
/// </summary>
public sealed class DynamicToolService : IToolService
{
    /// <summary>Creates a service from an existing collection of AI tools.</summary>
    /// <param name="identifier">The service prefix used in registered tool names.</param>
    /// <param name="description">Group metadata available to tool-calling strategies.</param>
    /// <param name="tools">
    ///     The tools to snapshot. Registration requires each entry to be an invocable
    ///     <see cref="AIFunction" />; hosted tools and declaration-only tools are unsupported.
    /// </param>
    public DynamicToolService(string identifier, string description, IEnumerable<AITool> tools)
    {
        if (string.IsNullOrWhiteSpace(identifier))
            throw new ArgumentException("A tool service identifier is required.", nameof(identifier));
        if (description is null) throw new ArgumentNullException(nameof(description));
        if (tools is null) throw new ArgumentNullException(nameof(tools));

        Identifier = identifier;
        Description = description;
        Tools = Array.AsReadOnly(tools.ToArray());
    }

    /// <inheritdoc />
    public string Identifier { get; }

    /// <inheritdoc />
    public string Description { get; }

    /// <summary>The supplied tools, copied when the service was created.</summary>
    public IReadOnlyList<AITool> Tools { get; }
}
