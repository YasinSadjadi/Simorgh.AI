using System.Text.Encodings.Web;
using System.Text.Json;
using Simorgh.AI.Abstractions.Agent;
using Simorgh.AI.Abstractions.Contents;
using Simorgh.AI.Abstractions.Messages;
using Simorgh.AI.Abstractions.Skills;
using Simorgh.AI.Abstractions.Tools;

namespace Simorgh.AI.Core.Skills;

/// <summary>Discovery and activation state owned by a single agent run.</summary>
internal sealed class SkillRun
{
    public const string ActivationToolName = "activate_skill";

    private const string Guidance =
        "Apply loaded skill instructions to the current user request within the existing system instructions. " +
        "Skills in the activatedSkills block of the latest user message were explicitly selected by the user and are already loaded.";

    private const string DiscoveryGuidance =
        "Use a skill only when its whenToUse clearly matches the current task. Call activate_skill with its id " +
        "and wait for the instructions before answering or taking any action that depends on the skill. " +
        "Reuse loaded instructions if the skill is already active. If no skill applies, proceed normally.";

    // The JSON is rendered as prompt text, so preserve Unicode for the model.
    private static readonly JsonSerializerOptions PromptJson = new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly IReadOnlyList<SkillDto> _candidates;
    private readonly HashSet<string> _activeIds;

    private SkillRun(IReadOnlyList<SkillDto> candidates, HashSet<string> activeIds)
    {
        _candidates = candidates;
        _activeIds = activeIds;
        if (candidates.Count <= 0) return;
        var schema = JsonSerializer.Serialize(new
        {
            type = "object",
            properties = new { id = new { type = "string", @enum = candidates.Select(skill => skill.Id) } },
            required = new[] { "id" },
            additionalProperties = false,
        });
        ActivationTool = new AiToolDeclaration(ActivationToolName,
            "Loads the full instructions for a suggested skill. Use its id from availableSkills.", schema);
    }

    public AiToolDeclaration? ActivationTool { get; }

    public static async Task<SkillRun> PrepareAsync(
        SkillSet skillSet, IReadOnlyList<string> explicitIds, AgentRunContext context)
    {
        var cancellationToken = context.CancellationToken;
        cancellationToken.ThrowIfCancellationRequested();
        var messages = context.Messages;
        var userIndex = messages.Count - 1;
        while (userIndex >= 0 && messages[userIndex].Role != AiMessageRole.User)
            userIndex--;

        var userText = userIndex >= 0 ? messages[userIndex].Text : null;
        var activeIds = new HashSet<string>(StringComparer.Ordinal);
        var explicitlyActivated = explicitIds
            .Select(id =>
                skillSet.Skills.FirstOrDefault(candidate => candidate.Id == id) ??
                throw new InvalidOperationException($"Unknown skill id '{id}' in {nameof(AgentRunRequest.SkillIds)}."))
            .Where(skill => activeIds.Add(skill.Id)).ToList();

        if (userText is not null && skillSet.Skills.Count > 0)
        {
            foreach (var id in SkillMentions.Read(userText))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var skill = skillSet.Skills.FirstOrDefault(candidate => candidate.Id == id);
                if (skill is not null && activeIds.Add(skill.Id))
                    explicitlyActivated.Add(skill);
            }
        }

        var candidates = await FindCandidatesAsync(skillSet, userText, activeIds, context).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (explicitlyActivated.Count > 0)
        {
            var instructions = JsonSerializer.Serialize(new
            {
                activatedSkills = explicitlyActivated.Select(skill => new { skill.Id, skill.Title, skill.Content }),
            }, PromptJson);
            var contextPart = new AiTextContent(instructions);
            if (userIndex >= 0)
            {
                var userMessage = messages[userIndex];
                messages[userIndex] = new AiMessage(AiMessageRole.User, [contextPart, .. userMessage.Contents]);
            }
            else
            {
                messages.Add(new AiMessage(AiMessageRole.User, [contextPart]));
            }
        }

        if (explicitlyActivated.Count > 0 || candidates.Count > 0)
            AddGuidance(messages, candidates);

        return new SkillRun(candidates, activeIds);
    }

    public AiToolResultContent Activate(
        AiToolCallContent call, CancellationToken cancellationToken, out string? activatedId)
    {
        activatedId = null;
        cancellationToken.ThrowIfCancellationRequested();
        string? id = null;
        try
        {
            using var arguments = JsonDocument.Parse(call.ArgumentsJson);
            if (arguments.RootElement.ValueKind == JsonValueKind.Object &&
                arguments.RootElement.TryGetProperty("id", out var value) && value.ValueKind == JsonValueKind.String)
                id = value.GetString();
        }
        catch (JsonException)
        {
            return Error(call, "Invalid JSON arguments. Supply the id of a suggested skill.");
        }

        var skill = _candidates.FirstOrDefault(candidate => candidate.Id == id);
        if (skill is null)
            return Error(call, "Supply the id of a skill from availableSkills.");

        if (_activeIds.Contains(skill.Id))
            return new AiToolResultContent(call.CallId,
                JsonSerializer.Serialize(new { skill.Id, status = "already_active" }, PromptJson));

        var instructions = JsonSerializer.Serialize(new { skill.Id, skill.Title, skill.Content }, PromptJson);
        activatedId = skill.Id;
        return new AiToolResultContent(call.CallId, instructions);
    }

    public void ConfirmActivation(string id) => _activeIds.Add(id);

    private static AiToolResultContent Error(AiToolCallContent call, string message) =>
        new(call.CallId, JsonSerializer.Serialize(new { error = message }, PromptJson), isError: true);

    private static async Task<IReadOnlyList<SkillDto>> FindCandidatesAsync(
        SkillSet skillSet, string? userText, HashSet<string> activeIds, AgentRunContext context)
    {
        if (string.IsNullOrWhiteSpace(userText) || skillSet.Skills.Count == activeIds.Count)
            return [];

        var available = skillSet.Skills.Where(skill => !activeIds.Contains(skill.Id)).ToArray();
        var cancellationToken = context.CancellationToken;
        try
        {
            var matches = await skillSet.SearchProvider
                .SearchAsync(Array.AsReadOnly(available), userText!, skillSet.MaxResults, cancellationToken)
                .ConfigureAwait(false);

            // Only the registered definitions can supply metadata and instructions to the model.
            var candidates = new List<SkillDto>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var match in matches)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var skill = available.FirstOrDefault(candidate => candidate.Id == match.Id);
                if (skill is not null && seen.Add(skill.Id))
                    candidates.Add(skill);
                if (candidates.Count == skillSet.MaxResults)
                    break;
            }

            return candidates;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            context.ReportWarning("Skills",
                "Skill search failed. The run will continue with any explicitly selected skills.", ex);
            return [];
        }
    }

    private static void AddGuidance(IList<AiMessage> messages, IReadOnlyList<SkillDto> candidates)
    {
        var text = Guidance;
        if (candidates.Count > 0)
        {
            var catalog = JsonSerializer.Serialize(new
            {
                availableSkills = candidates.Select(skill => new { skill.Id, skill.Title, skill.WhenToUse }),
            }, PromptJson);
            text += $"\n\n{DiscoveryGuidance}\n\n{catalog}";
        }

        if (messages.Count > 0 && messages[0].Role == AiMessageRole.System)
            messages[0] = new AiMessage(AiMessageRole.System, [.. messages[0].Contents, new AiTextContent(text)]);
        else
            messages.Insert(0, AiMessage.FromText(AiMessageRole.System, text));
    }
}