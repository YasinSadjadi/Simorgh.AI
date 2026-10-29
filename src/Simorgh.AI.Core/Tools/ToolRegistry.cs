using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Simorgh.AI.Abstractions.Contents;
using Simorgh.AI.Abstractions.Tools;

namespace Simorgh.AI.Core.Tools;

/// <summary>
///     Collects tools from <see cref="IToolService" /> objects. Each public method annotated
///     with <see cref="AiToolMethodAttribute" /> becomes one tool; the service's
///     <see cref="IToolService.Identifier" /> and <see cref="IToolService.Description" /> describe
///     the group the tool belongs to.
/// </summary>
public sealed class ToolRegistry
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly Dictionary<string, ToolDefinition> _tools = new(StringComparer.Ordinal);

    /// <summary>All registered tools, keyed by identifier.</summary>
    public IReadOnlyCollection<ToolDefinition> Tools => [.. _tools.Values];

    /// <summary>The tools as advertised to the model via <see cref="ChatOptions.Tools" />.</summary>
    public IList<AITool> ChatTools => [.. _tools.Values.Select(AITool (t) => t.Declaration)];

    /// <summary>Registers every tool found on <paramref name="service" />.</summary>
    public ToolRegistry Register(IToolService service)
    {
        if (service is null) throw new ArgumentNullException(nameof(service));

        foreach (var method in service.GetType()
                     .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                     .Where(m => m.GetCustomAttribute<AiToolMethodAttribute>() is not null))
        {
            var attribute = method.GetCustomAttribute<AiToolMethodAttribute>()!;
            var closedMethod = method;
            var identifier = $"{service.Identifier}/{attribute.ToolName}";
            Add(new ToolDefinition(identifier, attribute.Description, service, attribute,
                AIFunctionFactory.Create(closedMethod, service, identifier, attribute.Description).AsDeclarationOnly(),
                (call, ct) => InvokeMethodAsync(service, closedMethod, call, ct)));
        }

        return this;
    }

    /// <summary>
    /// Registers a tool described at runtime. The handler may call an API, a local function,
    /// or any other implementation; it participates in the same agent pipeline as service tools.
    /// </summary>
    /// <param name="identifier">The complete tool name used by the model.</param>
    /// <param name="description">The tool description shown to the model.</param>
    /// <param name="jsonSchema">A JSON Schema object describing the tool arguments.</param>
    /// <param name="invokeAsync">Executes the call and returns a result with the same call ID.</param>
    public ToolRegistry Register(
        string identifier,
        string description,
        string jsonSchema,
        Func<AiToolCallContent, CancellationToken, Task<AiToolResultContent>> invokeAsync)
    {
        if (string.IsNullOrWhiteSpace(identifier)) throw new ArgumentException("A tool identifier is required.", nameof(identifier));
        if (description is null) throw new ArgumentNullException(nameof(description));
        if (jsonSchema is null) throw new ArgumentNullException(nameof(jsonSchema));
        if (invokeAsync is null) throw new ArgumentNullException(nameof(invokeAsync));

        using var document = JsonDocument.Parse(jsonSchema);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
            throw new ArgumentException("The tool schema must be a JSON object.", nameof(jsonSchema));

        var declaration = AIFunctionFactory.CreateDeclaration(identifier, description, document.RootElement.Clone());
        Add(new ToolDefinition(identifier, description, null, null, declaration, invokeAsync));
        return this;
    }

    /// <summary>Registers a runtime tool with a handler implementation.</summary>
    /// <param name="identifier">The complete tool name used by the model.</param>
    /// <param name="description">The tool description shown to the model.</param>
    /// <param name="jsonSchema">A JSON Schema object describing the tool arguments.</param>
    /// <param name="handler">The implementation that executes calls to the tool.</param>
    public ToolRegistry Register(string identifier, string description, string jsonSchema, IDynamicToolHandler handler)
    {
        if (handler is null) throw new ArgumentNullException(nameof(handler));
        return Register(identifier, description, jsonSchema, handler.InvokeAsync);
    }

    /// <summary>Gets the tool registered under <paramref name="identifier" />, if any.</summary>
    public bool TryGet(string identifier, out ToolDefinition tool)
    {
        return _tools.TryGetValue(identifier, out tool!);
    }

    private void Add(ToolDefinition tool)
    {
        if (_tools.ContainsKey(tool.Identifier))
            throw new InvalidOperationException($"A tool with identifier '{tool.Identifier}' is already registered.");

        _tools[tool.Identifier] = tool;
    }

    private static async Task<AiToolResultContent> InvokeMethodAsync(
        IToolService service,
        MethodInfo method,
        AiToolCallContent call,
        CancellationToken cancellationToken)
    {
        using var document =
            JsonDocument.Parse(string.IsNullOrWhiteSpace(call.ArgumentsJson) ? "{}" : call.ArgumentsJson);
        var arguments = document.RootElement;

        var parameters = method.GetParameters();
        var invokeArgs = new object?[parameters.Length];
        for (var i = 0; i < parameters.Length; i++)
        {
            var parameter = parameters[i];
            if (parameter.ParameterType == typeof(CancellationToken))
            {
                invokeArgs[i] = cancellationToken;
                continue;
            }

            if (arguments.TryGetProperty(parameter.Name!, out var value) &&
                value.ValueKind is not JsonValueKind.Undefined and not JsonValueKind.Null)
                invokeArgs[i] = value.Deserialize(parameter.ParameterType, JsonOptions);
            else
                invokeArgs[i] = parameter.HasDefaultValue ? parameter.DefaultValue : null;
        }

        var result = method.Invoke(service, invokeArgs);

        var resultText = result switch
        {
            null => string.Empty,
            Task task => await SerializeTaskResultAsync(task).ConfigureAwait(false),
            _ => JsonSerializer.Serialize(result, JsonOptions)
        };

        return new AiToolResultContent(call.CallId, resultText);
    }

    private static async Task<string> SerializeTaskResultAsync(Task task)
    {
        await task.ConfigureAwait(false);
        var result = task.GetType().GetProperty("Result")!.GetValue(task);
        return result is null ? string.Empty : JsonSerializer.Serialize(result, JsonOptions);
    }
}

