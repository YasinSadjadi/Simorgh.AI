namespace Simorgh.AI.Abstractions.Contents;

/// <summary>
///     The outcome of an <see cref="AiToolCallContent" />, fed back to the model.
/// </summary>
/// <param name="callId">Id of the matching <see cref="AiToolCallContent" />.</param>
/// <param name="result">Tool output as text (typically JSON).</param>
/// <param name="isError">Indicates the tool invocation failed; <paramref name="result" /> then carries the error message.</param>
public sealed class AiToolResultContent(string callId, string result, bool isError = false) : AiContent
{
    /// <summary>Id of the matching <see cref="AiToolCallContent" />.</summary>
    public string CallId { get; } = callId;

    /// <summary>Tool output as text (typically JSON).</summary>
    public string Result { get; } = result;

    /// <summary>Indicates the tool invocation failed; <see cref="Result" /> then carries the error message.</summary>
    public bool IsError { get; } = isError;
}