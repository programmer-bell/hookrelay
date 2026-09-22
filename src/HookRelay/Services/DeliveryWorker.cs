using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using HookRelay.Config;
using HookRelay.Data;
using HookRelay.Domain;
using Microsoft.Extensions.Options;

namespace HookRelay.Services;

public sealed class DeliveryWorker : BackgroundService
{
    public const string HttpClientName = "hookrelay.delivery";

    private const int BatchSize = 10;

    private readonly DeliveryRepository _deliveries;
    private readonly TargetUrlValidator _validator;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly EventBus _eventBus;
    private readonly RetryPolicy _retryPolicy;
    private readonly AppOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<DeliveryWorker> _logger;

    public DeliveryWorker(
        DeliveryRepository deliveries,
        TargetUrlValidator validator,
        IHttpClientFactory httpClientFactory,
        EventBus eventBus,
        RetryPolicy retryPolicy,
        IOptions<AppOptions> options,
        TimeProvider timeProvider,
        ILogger<DeliveryWorker> logger)
    {
        _deliveries = deliveries;
        _validator = validator;
        _httpClientFactory = httpClientFactory;
        _eventBus = eventBus;
        _retryPolicy = retryPolicy;
        _options = options.Value;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var inFlight = new List<DeliveryJob>();
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await TryRecoverStuckAsync(stoppingToken);

                var jobs = await _deliveries.ClaimDueAsync(BatchSize, CancellationToken.None);
                foreach (var job in jobs)
                {
                    if (stoppingToken.IsCancellationRequested)
                    {
                        inFlight.Add(job);
                        break;
                    }

                    inFlight.Add(job);
                    await ProcessClaimAsync(job, stoppingToken);
                    inFlight.Remove(job);
                }

                if (jobs.Count == 0)
                {
                    await Task.Delay(TimeSpan.FromSeconds(_options.PollIntervalSeconds), stoppingToken);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        finally
        {
            // Graceful stop: never leave claimed deliveries stuck in 'delivering'.
            foreach (var job in inFlight)
            {
                try
                {
                    await _deliveries.MarkRetryAsync(job.DeliveryId, _timeProvider.GetUtcNow().UtcDateTime, CancellationToken.None);
                }
                catch (Exception ex)
                {
                    LogRequeueFailed(_logger, job.DeliveryId, ex);
                }
            }
        }
    }

    private async Task ProcessClaimAsync(DeliveryJob job, CancellationToken ct)
    {
        var validation = await _validator.ValidateAsync(job.TargetUrl, ct);
        if (!validation.IsValid)
        {
            await _deliveries.RecordAttemptAsync(
                job.DeliveryId,
                statusCode: null,
                error: string.Join("; ", validation.Errors),
                durationMs: 0,
                _timeProvider.GetUtcNow().UtcDateTime,
                ct);
            await MarkDoneAsync(job, success: false, exhausted: true, ct);
            LogMarkedDead(_logger, job.DeliveryId, null);
            return;
        }

        int? statusCode = null;
        string? error = null;
        var stopwatch = Stopwatch.StartNew();
        try
        {
            statusCode = await SendAsync(job, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException or IOException)
        {
            var message = ex.Message;
            error = message.Length > 500 ? message[..500] : message;
        }

        stopwatch.Stop();

        await _deliveries.RecordAttemptAsync(
            job.DeliveryId,
            statusCode,
            error,
            (int)stopwatch.ElapsedMilliseconds,
            _timeProvider.GetUtcNow().UtcDateTime,
            ct);

        var attemptsDone = job.AttemptCount + 1;
        var success = statusCode is >= 200 and < 300;
        await MarkDoneAsync(job, success, RetryPolicy.IsExhausted(attemptsDone, _options.MaxAttempts), ct);

        if (statusCode is null)
        {
            LogAttemptFailed(_logger, job.DeliveryId, error, null);
        }
        else
        {
            LogAttemptSucceeded(_logger, job.DeliveryId, statusCode.Value, stopwatch.ElapsedMilliseconds, null);
        }
    }

    private async Task<int> SendAsync(DeliveryJob job, CancellationToken ct)
    {
        var client = _httpClientFactory.CreateClient(HttpClientName);
        using var request = new HttpRequestMessage(HttpMethod.Post, job.TargetUrl);

        request.Headers.TryAddWithoutValidation("X-HookRelay-Id", job.RequestId.ToString());
        var unixSeconds = _timeProvider.GetUtcNow().ToUnixTimeSeconds();
        request.Headers.TryAddWithoutValidation("X-HookRelay-Timestamp", unixSeconds.ToString(CultureInfo.InvariantCulture));
        request.Headers.TryAddWithoutValidation(
            "X-HookRelay-Signature",
            "sha256=" + HmacSigner.Sign(job.SigningSecret, $"{unixSeconds}.{job.Body}"));

        var forwardedHeaders = ParseForwardHeaders(job.Headers, out var contentType);
        foreach (var header in forwardedHeaders)
        {
            request.Headers.TryAddWithoutValidation(header.Name, header.Value);
        }

        request.Content = new StringContent(job.Body, Encoding.UTF8, contentType ?? "text/plain");

        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        return (int)response.StatusCode;
    }

    private static List<ForwardHeader> ParseForwardHeaders(string headersJson, out string? contentType)
    {
        var forward = new List<ForwardHeader>();
        contentType = null;

        if (string.IsNullOrWhiteSpace(headersJson))
        {
            return forward;
        }

        try
        {
            using var document = JsonDocument.Parse(headersJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return forward;
            }

            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (string.Equals(property.Name, "Content-Type", StringComparison.OrdinalIgnoreCase))
                {
                    contentType = property.Value.GetString() ?? string.Empty;
                    continue;
                }

                if (property.Name is "Host" or "Content-Length" or "Connection" or "Transfer-Encoding" or "Authorization" or "Cookie")
                {
                    continue;
                }

                forward.Add(new ForwardHeader(property.Name, property.Value.GetString() ?? string.Empty));
            }
        }
        catch (JsonException)
        {
        }

        return forward;
    }

    private async Task MarkDoneAsync(DeliveryJob job, bool success, bool exhausted, CancellationToken ct)
    {
        if (success)
        {
            await _deliveries.MarkSucceededAsync(job.DeliveryId, ct);
            PublishStatus(job, DeliveryStatus.Succeeded);
        }
        else if (exhausted)
        {
            await _deliveries.MarkDeadAsync(job.DeliveryId, ct);
            PublishStatus(job, DeliveryStatus.Dead);
            LogDeliveryDead(_logger, job.DeliveryId, job.AttemptCount + 1, null);
        }
        else
        {
            var attemptsDone = job.AttemptCount + 1;
            var nextAttemptAt = _timeProvider.GetUtcNow().UtcDateTime
                                + _retryPolicy.NextDelay(attemptsDone, Random.Shared);
            await _deliveries.MarkRetryAsync(job.DeliveryId, nextAttemptAt, ct);
            PublishStatus(job, DeliveryStatus.Pending);
        }
    }

    private void PublishStatus(DeliveryJob job, string status) =>
        _eventBus.Publish(new DeliveryStatusChangedEvent(job.DeliveryId, job.RequestId, job.Slug, status));

    private async Task TryRecoverStuckAsync(CancellationToken ct)
    {
        try
        {
            var recovered = await _deliveries.RecoverStuckAsync(ct);
            if (recovered > 0)
            {
                LogRecovered(_logger, recovered, null);
            }
        }
        catch (Exception ex)
        {
            LogRecoverFailed(_logger, ex);
        }
    }

    private sealed record ForwardHeader(string Name, string Value);

    private static readonly Action<ILogger, Guid, Exception?> LogRequeueFailed =
        LoggerMessage.Define<Guid>(LogLevel.Error, new EventId(1, "RequeueFailed"), "Delivery {DeliveryId} could not be requeued on shutdown");

    private static readonly Action<ILogger, Guid, Exception?> LogMarkedDead =
        LoggerMessage.Define<Guid>(LogLevel.Warning, new EventId(2, "MarkedDead"), "Delivery {DeliveryId} marked dead: target URL failed SSRF validation");

    private static readonly Action<ILogger, Guid, string?, Exception?> LogAttemptFailed =
        LoggerMessage.Define<Guid, string?>(LogLevel.Warning, new EventId(3, "AttemptFailed"), "Delivery {DeliveryId} attempt failed: {Error}");

    private static readonly Action<ILogger, Guid, int, long, Exception?> LogAttemptSucceeded =
        LoggerMessage.Define<Guid, int, long>(LogLevel.Information, new EventId(4, "AttemptSucceeded"), "Delivery {DeliveryId} attempt returned {StatusCode} in {DurationMs} ms");

    private static readonly Action<ILogger, Guid, int, Exception?> LogDeliveryDead =
        LoggerMessage.Define<Guid, int>(LogLevel.Warning, new EventId(9, "DeliveryDead"), "Delivery {DeliveryId} dead after {AttemptCount} attempts");

    private static readonly Action<ILogger, int, Exception?> LogRecovered =
        LoggerMessage.Define<int>(LogLevel.Warning, new EventId(5, "RecoveredStuck"), "Recovered {Count} deliveries stuck in 'delivering'");

    private static readonly Action<ILogger, Exception> LogRecoverFailed =
        LoggerMessage.Define(LogLevel.Error, new EventId(6, "RecoverFailed"), "Could not recover stuck deliveries");
}
