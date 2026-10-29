using Simorgh.AI.Routing.Abstractions;
using Simorgh.AI.Routing.Internal;
using Simorgh.AI.Routing.Models;
using System.Runtime.CompilerServices;
using System.Net.Http;
using Microsoft.Extensions.AI;

namespace Simorgh.AI.Routing.Strategies;

/// <summary>Attempts clients in order until one succeeds.</summary>
public sealed class FallbackRoutingStrategy : IChatRoutingStrategy
{
    private readonly RoutingTarget[] _targets;
    private readonly Func<Exception, bool> _shouldFallback;

    /// <summary>Creates a fallback policy. By default only timeouts and HTTP transport errors are retried.</summary>
    public FallbackRoutingStrategy(IEnumerable<RoutingTarget> targets, Func<Exception, bool>? shouldFallback = null)
    {
        _targets = RoutingValidation.Targets(targets);
        _shouldFallback = shouldFallback ?? IsTransient;
    }

    private static bool IsTransient(Exception exception)
    {
        if (exception is TimeoutException || exception is OperationCanceledException)
            return true;
        if (exception is not HttpRequestException httpError)
            return false;

        // StatusCode is unavailable on the netstandard2.0 reference surface.
        var status = httpError.GetType().GetProperty("StatusCode")?.GetValue(httpError);
        if (status is null)
            return true; // Transport failure without an HTTP response.
        var code = Convert.ToInt32(status);
        return code == 408 || code == 429 || code >= 500 && code <= 599;
    }

    public async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        var input = messages.ToArray();
        var errors = new List<Exception>();
        foreach (var target in _targets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return await target.Client.GetResponseAsync(input, options, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (_shouldFallback(ex))
            {
                errors.Add(ex);
            }
        }
        throw new AggregateException("All fallback clients failed.", errors);
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var input = messages.ToArray();
        var errors = new List<Exception>();
        foreach (var target in _targets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IAsyncEnumerator<ChatResponseUpdate>? enumerator = null;
            var started = false;
            try
            {
                Exception? failure = null;
                try
                {
                    enumerator = target.Client.GetStreamingResponseAsync(input, options, cancellationToken).GetAsyncEnumerator(cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex) when (_shouldFallback(ex))
                {
                    failure = ex;
                }
                if (failure is not null)
                {
                    errors.Add(failure);
                    continue;
                }
                while (true)
                {
                    var more = false;
                    failure = null;
                    try
                    {
                        more = await enumerator!.MoveNextAsync().ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex) when (!started && _shouldFallback(ex))
                    {
                        failure = ex;
                    }
                    if (failure is not null)
                    {
                        errors.Add(failure);
                        break;
                    }
                    if (!more)
                        yield break;
                    started = true;
                    yield return enumerator!.Current;
                }
            }
            finally
            {
                if (enumerator is not null)
                    await enumerator.DisposeAsync().ConfigureAwait(false);
            }
        }
        throw new AggregateException("All fallback clients failed.", errors);
    }
}
