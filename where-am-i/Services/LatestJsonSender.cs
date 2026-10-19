using System.Net;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace where_am_i.Services
{
    public enum SendOutcome { Success, Failed, Rejected, Canceled, Superseded }

    public sealed record RequestSendResult(
        SendOutcome Outcome, int Attempts, HttpStatusCode? StatusCode = null);

    /// <summary>
    /// Serializes snapshots for one endpoint; older pending requests and results are superseded.
    /// Success requires HTTP success and the API's data:true acknowledgement.
    /// This does not confirm that a browser received or applied the update.
    /// </summary>
    internal sealed class LatestJsonSender
    {
        private readonly string endpoint;
        private readonly HttpClient httpClient;
        private readonly SemaphoreSlim sendLock = new(1, 1);
        private readonly Func<TimeSpan, CancellationToken, Task> delay;
        private long revision;

        public LatestJsonSender(HttpClient httpClient, string endpoint,
            Func<TimeSpan, CancellationToken, Task> delay)
        {
            this.httpClient = httpClient;
            this.endpoint = endpoint;
            this.delay = delay;
        }

        public long ReserveLatest() => Interlocked.Increment(ref revision);
        public bool IsCurrent(long version) => version == Volatile.Read(ref revision);

        public async Task<RequestSendResult> PostLatestAsync<T>(T request, long version,
            CancellationToken cancellationToken, Action<int>? onAttempt = null)
        {
            var attempts = 0;
            var entered = false;
            try
            {
                await sendLock.WaitAsync(cancellationToken);
                entered = true;
                for (var attempt = 1; attempt <= 3; attempt++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (version != Volatile.Read(ref revision))
                        return new(SendOutcome.Superseded, attempts);
                    attempts = attempt;
                    HttpStatusCode? statusCode = null;
                    var retry = true;
                    try
                    {
                        onAttempt?.Invoke(attempt);
                        using var content = new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json");
                        using var response = await httpClient.PostAsync(endpoint, content, cancellationToken);
                        statusCode = response.StatusCode;
                        if (version != Volatile.Read(ref revision))
                            return new(SendOutcome.Superseded, attempts, statusCode);
                        if (response.IsSuccessStatusCode)
                        {
                            var accepted = await IsAcceptedAsync(response, cancellationToken);
                            if (version != Volatile.Read(ref revision))
                                return new(SendOutcome.Superseded, attempts, statusCode);
                            return new(accepted ? SendOutcome.Success : SendOutcome.Rejected,
                                attempts, statusCode);
                        }
                        retry = response.StatusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests ||
                            (int)response.StatusCode >= 500;
                    }
                    catch (HttpRequestException) { }
                    catch (IOException) { }
                    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                    {
                        // HttpClient timeout is retryable; a caller's cancellation is not.
                    }
                    if (version != Volatile.Read(ref revision))
                        return new(SendOutcome.Superseded, attempts, statusCode);
                    if (!retry || attempt == 3)
                        return new(SendOutcome.Failed, attempts, statusCode);
                    await delay(TimeSpan.FromSeconds(attempt), cancellationToken);
                }
                throw new InvalidOperationException("Unreachable retry state");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return new(SendOutcome.Canceled, attempts);
            }
            finally
            {
                if (entered) sendLock.Release();
            }
        }

        internal static async Task<bool> IsAcceptedAsync(HttpResponseMessage response, CancellationToken token)
        {
            try
            {
                using var stream = await response.Content.ReadAsStreamAsync(token);
                using var json = await JsonDocument.ParseAsync(stream, cancellationToken: token);
                return json.RootElement.ValueKind == JsonValueKind.Object &&
                    json.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.True;
            }
            catch (JsonException) { return false; }
        }
    }
}
