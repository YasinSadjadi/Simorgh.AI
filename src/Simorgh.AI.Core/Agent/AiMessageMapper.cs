using System.Text.Json;
using Microsoft.Extensions.AI;
using Simorgh.AI.Abstractions.Contents;
using Simorgh.AI.Abstractions.Messages;

namespace Simorgh.AI.Core.Agent;

/// <summary>
///     Maps between Simorgh.AI's <see cref="AiMessage" /> model and Microsoft.Extensions.AI's
///     <see cref="ChatMessage" /> model.
/// </summary>
public static class AiMessageMapper
{
    /// <summary>Converts a Simorgh.AI message into a chat message for the provider.</summary>
    public static ChatMessage ToChatMessage(AiMessage message)
    {
        var contents = message.Contents.Select(ToAIContent).ToList();
        return new ChatMessage(ToChatRole(message.Role), contents);
    }

    /// <summary>
    ///     Converts a provider response into a Simorgh.AI agent message. Usage and finish
    ///     reason are lifted onto <see cref="AiResponse" /> as response metadata, not
    ///     message content.
    /// </summary>
    public static AiResponse ToAiResponse(ChatResponse response)
    {
        var contents = response.Messages.SelectMany(m => m.Contents).ToList();
        var usage = response.Usage is not null
            ? ToAiUsage(response.Usage)
            : ExtractUsage(contents);
        return ToAiResponse(contents, usage, response.FinishReason?.ToString());
    }

    /// <summary>
    ///     Converts accumulated contents (e.g. gathered from a streamed response) into a
    ///     Simorgh.AI agent message, merging consecutive text parts into a single
    ///     <see cref="AiTextContent" />. Any <see cref="UsageContent" /> is lifted onto
    ///     <see cref="AiResponse.Usage" /> as response metadata, not message content.
    /// </summary>
    public static AiResponse ToAiResponse(IReadOnlyList<AIContent> contents, string? finishReason = null)
    {
        return ToAiResponse(contents, ExtractUsage(contents), finishReason);
    }

    private static AiResponse ToAiResponse(IReadOnlyList<AIContent> contents, AiUsage? usage, string? finishReason)
    {
        var mapped = new List<AiContent>();
        foreach (var content in contents.Where(c => c is not UsageContent).Select(ToAiContent))
        {
            if (content is AiTextContent text &&
                mapped.Count > 0 && mapped[mapped.Count - 1] is AiTextContent previous)
                mapped[mapped.Count - 1] = new AiTextContent(previous.Text + text.Text);
            else
                mapped.Add(content);
        }

        return new AiResponse(new AiMessage(AiMessageRole.Agent, mapped), usage, finishReason);
    }

    /// <summary>Lifts the last reported <see cref="UsageContent" /> out of streamed contents.</summary>
    private static AiUsage? ExtractUsage(IReadOnlyList<AIContent> contents)
    {
        var usage = contents.OfType<UsageContent>().LastOrDefault();
        return usage is null ? null : ToAiUsage(usage.Details);
    }

    private static AiUsage ToAiUsage(UsageDetails details)
    {
        var inputDetails = new Dictionary<string, long>();
        AddDetail(inputDetails, "cached", details.CachedInputTokenCount);
        AddDetail(inputDetails, "text", details.InputTextTokenCount);
        AddDetail(inputDetails, "audio", details.InputAudioTokenCount);

        var outputDetails = new Dictionary<string, long>();
        AddDetail(outputDetails, "reasoning", details.ReasoningTokenCount);
        AddDetail(outputDetails, "text", details.OutputTextTokenCount);
        AddDetail(outputDetails, "audio", details.OutputAudioTokenCount);

        return new AiUsage(
            details.InputTokenCount ?? 0,
            details.OutputTokenCount ?? 0,
            inputDetails.Count == 0 ? null : inputDetails,
            outputDetails.Count == 0 ? null : outputDetails);
    }

    private static ChatRole ToChatRole(AiMessageRole role)
    {
        return role switch
        {
            AiMessageRole.System => ChatRole.System,
            AiMessageRole.User => ChatRole.User,
            AiMessageRole.Agent => ChatRole.Assistant,
            AiMessageRole.Tool => ChatRole.Tool,
            _ => throw new ArgumentOutOfRangeException(nameof(role), role, "Unknown role.")
        };
    }

    private static AIContent ToAIContent(AiContent content)
    {
        return content switch
        {
            AiTextContent text => new TextContent(text.Text),
            AiImageContent image => MediaToDataContent(image),
            AiAudioContent audio => MediaToDataContent(audio),
            AiVideoContent video => MediaToDataContent(video),
            AiFileContent file => MediaToDataContent(file),
            AiToolCallContent call => new FunctionCallContent(
                call.CallId,
                call.Name,
                JsonSerializer.Deserialize<Dictionary<string, object?>>(call.ArgumentsJson)),
            AiToolResultContent result => new FunctionResultContent(
                result.CallId,
                result.Result),
            AiThinkingContent thinking => new TextContent(thinking.Text),
            AiErrorContent => throw new NotSupportedException(
                "Errors are not sent to the provider as input content."),
            _ => throw new NotSupportedException($"Content type '{content.GetType()}' is not supported.")
        };
    }

    private static DataContent MediaToDataContent(AiMediaContent media)
    {
        if (media.Data.HasValue) return new DataContent(media.Data.Value, media.MimeType);

        if (media.Url is not null) return new DataContent(media.Url, media.MimeType);

        throw new ArgumentException(
            $"Media content of type '{media.GetType().Name}' has neither inline data nor a URL.");
    }

    private static AiContent ToAiContent(AIContent content)
    {
        return content switch
        {
            TextContent text => new AiTextContent(text.Text),
            FunctionCallContent call => new AiToolCallContent(
                call.CallId,
                call.Name,
                call.Arguments is null ? "{}" : JsonSerializer.Serialize(call.Arguments)),
            FunctionResultContent result => new AiToolResultContent(
                result.CallId,
                result.Result is null ? string.Empty : JsonSerializer.Serialize(result.Result)),
            ErrorContent error => new AiErrorContent(error.Message, error.ErrorCode),
            DataContent data => DataToAiMediaContent(data),
            UriContent uri => new AiFileContent(uri.MediaType ?? "application/octet-stream", url: uri.Uri),
            _ => new AiTextContent(content.ToString() ?? string.Empty)
        };
    }

    private static void AddDetail(Dictionary<string, long> details, string name, long? count)
        {
        if (count.HasValue) details[name] = count.Value;
    }

    private static AiContent DataToAiMediaContent(DataContent data)
    {
        var mimeType = data.MediaType;
        if (mimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            return new AiImageContent(mimeType, data.Data);

        if (mimeType.StartsWith("audio/", StringComparison.OrdinalIgnoreCase))
            return new AiAudioContent(mimeType, data.Data);

        if (mimeType.StartsWith("video/", StringComparison.OrdinalIgnoreCase))
            return new AiVideoContent(mimeType, data.Data);

        return new AiFileContent(mimeType, data.Data);
    }
}