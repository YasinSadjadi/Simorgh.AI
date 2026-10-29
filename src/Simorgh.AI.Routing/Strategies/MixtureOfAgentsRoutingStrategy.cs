#if false
using Simorgh.AI.Routing.Abstractions;
using Simorgh.AI.Routing.Internal;
using Simorgh.AI.Routing.Models;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace Simorgh.AI.Routing.Strategies;

/// <summary>Queries members concurrently and lets a chat client judge synthesize their answers.</summary>
public sealed class MixtureOfAgentsRoutingStrategy : IChatRoutingStrategy
{
    private readonly RoutingTarget[] _members;
    private readonly IChatClient _judge;
    private readonly string _prompt;
    public MixtureOfAgentsRoutingStrategy(IEnumerable<RoutingTarget> members, IChatClient judge, string prompt)
    {
        _members = RoutingValidation.Targets(members);
        _judge = judge ?? throw new ArgumentNullException(nameof(judge));
        _prompt = string.IsNullOrWhiteSpace(prompt) ? throw new ArgumentException("A judge prompt is required.", nameof(prompt)) : prompt;
    }
    private async Task<ChatMessage[]> BuildJudgeMessages(IEnumerable<ChatMessage> messages, ChatOptions? options, CancellationToken cancellationToken)
    {
        var input = messages.ToArray();
        async Task<(ChatResponse? Response, Exception? Error)> Query(RoutingTarget member)
        {
            try
            {
                return (await member.Client.GetResponseAsync(input, options, cancellationToken).ConfigureAwait(false), null);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                return (null, ex);
            }
        }
        var results = await Task.WhenAll(_members.Select(Query)).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        var successful = results.Where(r => r.Response is not null).ToArray();
        if (successful.Length == 0) throw new AggregateException("All mixture members failed.", results.Select(r => r.Error!));
        var answers = string.Join("\n\n", successful.Select((r, i) => $"Response {i + 1}:\n{r.Response!.Text}"));
        return input.Concat(new[] { new ChatMessage(ChatRole.User, $"{_prompt}\n\n{answers}") }).ToArray();
    }
    public async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        => await _judge.GetResponseAsync(await BuildJudgeMessages(messages, options, cancellationToken).ConfigureAwait(false), options, cancellationToken).ConfigureAwait(false);
    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var judgeMessages = await BuildJudgeMessages(messages, options, cancellationToken).ConfigureAwait(false);
        await foreach (var update in _judge.GetStreamingResponseAsync(judgeMessages, options, cancellationToken).WithCancellation(cancellationToken).ConfigureAwait(false))
            yield return update;
    }
}

#endif
