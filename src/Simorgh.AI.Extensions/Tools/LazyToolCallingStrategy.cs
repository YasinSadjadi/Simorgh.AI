using System.Text.Json;
using Simorgh.AI.Abstractions.Agent;
using Simorgh.AI.Abstractions.Contents;
using Simorgh.AI.Abstractions.Messages;
using Simorgh.AI.Abstractions.Tools;

namespace Simorgh.AI.Extensions.Tools;

/// <summary>
///     Lazy tool-calling strategy for large tool registries. Instead of advertising every
///     registered tool, the model only sees two meta-tools: <c>list_tools</c> to discover
///     what exists, and <c>load_tool</c> to pull a tool into the conversation. Once loaded,
///     the real tool is advertised from the next round on, so its calls go through the
///     default execution path. Calling a tool that was never loaded returns an error
///     message telling the model to load it first. Per-run state (the loaded set and the
///     tool catalog) is kept in <see cref="AgentRunContext.Items" />, so one service
///     instance can serve concurrent runs safely.
/// </summary>
public sealed class LazyToolCallingStrategy : IToolCallingStrategy
{
    /// <summary>Name of the meta-tool that loads a registered tool into the conversation.</summary>
    public const string LoadToolName = "load_tool";

    /// <summary>Name of the meta-tool that lists the registered tools with their descriptions.</summary>
    public const string ListToolsName = "list_tools";

    private const string LoadedToolsKey = "Simorgh.AI.LazyToolCalling.LoadedTools";
    private const string CatalogKey = "Simorgh.AI.LazyToolCalling.Catalog";

    private static readonly string ListToolsSchema = JsonSerializer.Serialize(new
    {
        type = "object",
        properties = new
        {
            filter = new
            {
                type = "string",
                description = "Optional case-insensitive substring that identifiers or descriptions must contain.",
            },
        },
    });

    private static readonly string LoadToolSchema = JsonSerializer.Serialize(new
    {
        type = "object",
        properties = new
        {
            identifier = new
            {
                type = "string",
                description = "Identifier of the tool to load, as reported by list_tools.",
            },
        },
        required = new[] { "identifier" },
    });

    /// <inheritdoc />
    public int MaxToolRounds => 32;

    /// <inheritdoc />
    public Task<IReadOnlyList<AiToolDeclaration>> GetToolDeclarationsAsync(
        IReadOnlyList<AiToolDeclaration> availableTools,
        AgentRunContext context,
        int round,
        CancellationToken cancellationToken = default)
    {
        // The registry is stashed on the first round so InvokeToolsAsync can validate
        // load_tool calls and list_tools can serve the catalog; the contract does not
        // hand the registry to InvokeToolsAsync.
        context.Items[CatalogKey] = availableTools;

        var declarations = new List<AiToolDeclaration>
        {
            new(
                ListToolsName,
                "Lists the tools available in this environment with their identifiers and descriptions. "
                + "Call this before using any tool, then load the ones you need with load_tool.",
                ListToolsSchema),
            new(
                LoadToolName,
                "Loads a tool so that it becomes callable from the next round. "
                + "Call list_tools first to discover identifiers.",
                LoadToolSchema),
        };

        var loaded = GetLoadedTools(context);
        foreach (var tool in availableTools.Where(t => loaded.Contains(t.Identifier)))
            declarations.Add(tool);

        return Task.FromResult<IReadOnlyList<AiToolDeclaration>>(declarations);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AiMessage>> InvokeToolsAsync(
        AgentRunContext context,
        IReadOnlyList<AiToolCallContent> calls,
        Func<AiToolCallContent, CancellationToken, Task<AiToolResultContent>> invokeNext,
        CancellationToken cancellationToken = default)
    {
        var catalog = context.Items.TryGetValue(CatalogKey, out var value)
            ? value as IReadOnlyList<AiToolDeclaration> ?? []
            : [];
        var loaded = GetLoadedTools(context);

        var results = new List<AiToolResultContent>(calls.Count);
        foreach (var call in calls)
        {
            cancellationToken.ThrowIfCancellationRequested();
            results.Add(await InvokeAsync(context, call, catalog, loaded, invokeNext, cancellationToken)
                .ConfigureAwait(false));
        }

        return
        [
            new AiMessage(AiMessageRole.Tool, [.. results]),
        ];
    }

    private async Task<AiToolResultContent> InvokeAsync(
        AgentRunContext context,
        AiToolCallContent call,
        IReadOnlyList<AiToolDeclaration> catalog,
        HashSet<string> loaded,
        Func<AiToolCallContent, CancellationToken, Task<AiToolResultContent>> invokeNext,
        CancellationToken cancellationToken)
    {
        if (call.Name == ListToolsName)
            return ListTools(call, catalog, loaded);

        if (call.Name == LoadToolName)
            return LoadTool(context, call, catalog, loaded);

        if (!loaded.Contains(call.Name))
            return new AiToolResultContent(
                call.CallId,
                $"Tool '{call.Name}' is not loaded. Call {ListToolsName} to discover tools, "
                + $"then {LoadToolName} with its identifier before calling it.",
                true);

        return await invokeNext(call, cancellationToken).ConfigureAwait(false);
    }

    private static AiToolResultContent ListTools(
        AiToolCallContent call, IReadOnlyList<AiToolDeclaration> catalog, HashSet<string> loaded)
    {
        string? filter = null;
        try
        {
            using var document = JsonDocument.Parse(
                string.IsNullOrWhiteSpace(call.ArgumentsJson) ? "{}" : call.ArgumentsJson);
            if (document.RootElement.TryGetProperty("filter", out var filterElement) &&
                filterElement.ValueKind == JsonValueKind.String)
                filter = filterElement.GetString();
        }
        catch (JsonException)
        {
            return new AiToolResultContent(call.CallId, "Invalid JSON arguments.", true);
        }

        var matches = catalog
            .Where(t => filter is null
                        || t.Identifier.Contains(filter, StringComparison.OrdinalIgnoreCase)
                        || t.Description.Contains(filter, StringComparison.OrdinalIgnoreCase))
            .Select(t => new
            {
                identifier = t.Identifier,
                description = t.Description,
                loaded = loaded.Contains(t.Identifier),
            });

        return new AiToolResultContent(
            call.CallId, JsonSerializer.Serialize(matches.ToArray()));
    }

    private AiToolResultContent LoadTool(
        AgentRunContext context,
        AiToolCallContent call, IReadOnlyList<AiToolDeclaration> catalog, HashSet<string> loaded)
    {
        string? identifier;
        try
        {
            using var document = JsonDocument.Parse(
                string.IsNullOrWhiteSpace(call.ArgumentsJson) ? "{}" : call.ArgumentsJson);
            identifier = document.RootElement.TryGetProperty("identifier", out var element) &&
                element.ValueKind == JsonValueKind.String
                ? element.GetString()
                : null;
        }
        catch (JsonException)
        {
            return new AiToolResultContent(call.CallId, "Invalid JSON arguments.", true);
        }

        if (identifier is null || catalog.All(t => t.Identifier != identifier))
        {
            return new AiToolResultContent(
                call.CallId,
                $"Unknown tool '{identifier ?? "(missing identifier)"}'. "
                + $"Call {ListToolsName} to see the available tools.",
                true);
        }

        if (loaded.Add(identifier))
        {
            // The loaded set lives on the run context; the strategy instance itself stays
            // stateless so concurrent runs do not see each other's tools.
            context.Items[LoadedToolsKey] = loaded;
        }

        return new AiToolResultContent(
            call.CallId,
            $"Tool '{identifier}' is loaded and callable from the next round. "
            + "Do not call load_tool for it again.");
    }

    private static HashSet<string> GetLoadedTools(AgentRunContext context)
    {
        return context.Items.TryGetValue(LoadedToolsKey, out var value)
            ? new HashSet<string>((IEnumerable<string>)value!, StringComparer.Ordinal)
            : new HashSet<string>(StringComparer.Ordinal);
    }
}
