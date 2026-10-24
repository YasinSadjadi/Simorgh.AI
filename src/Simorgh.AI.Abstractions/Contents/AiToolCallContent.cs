namespace Simorgh.AI.Abstractions.Contents;

/// <summary>
///     A request from the model to invoke a tool, produced during an agent run.
/// </summary>
/// <param name="callId">Provider-specific id correlating this call with its <see cref="AiToolResultContent" />.</param>
/// <param name="name">Name of the tool to invoke.</param>
/// <param name="argumentsJson">Tool arguments as a JSON object string.</param>
public sealed class AiToolCallContent(string callId, string name, string argumentsJson) : AiContent
{
    /// <summary>Provider-specific id correlating this call with its <see cref="AiToolResultContent" />.</summary>
    public string CallId { get; } = callId;

    /// <summary>Name of the tool to invoke.</summary>
    public string Name { get; } = name;

    /// <summary>Tool arguments as a JSON object string.</summary>
    public string ArgumentsJson { get; } = argumentsJson;
}