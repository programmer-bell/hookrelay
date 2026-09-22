using HookRelay.Config;
using HookRelay.Data;
using Microsoft.Extensions.Options;

namespace HookRelay.Services;

public sealed class DataRetentionWorker : BackgroundService
{
    private readonly CaptureRepository _captures;
    private readonly RetentionOptions _options;
    private readonly ILogger<DataRetentionWorker> _logger;

    public DataRetentionWorker(
        CaptureRepository captures,
        IOptions<RetentionOptions> options,
        ILogger<DataRetentionWorker> logger)
    {
        _captures = captures;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromHours(_options.CheckIntervalHours);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var cutoff = DateTime.UtcNow.AddDays(-_options.RetentionDays);
                var deleted = await _captures.DeleteOlderThanAsync(cutoff, stoppingToken);
                if (deleted > 0)
                {
                    LogPurged(_logger, deleted, _options.RetentionDays, null);
                }
            }
            catch (Exception ex)
            {
                LogPurgeFailed(_logger, ex);
            }

            await LongDelayAsync(interval, stoppingToken);
        }
    }

    private static async Task LongDelayAsync(TimeSpan delay, CancellationToken stoppingToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        try
        {
            await Task.Delay(delay, linked.Token);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    private static readonly Action<ILogger, int, int, Exception?> LogPurged =
        LoggerMessage.Define<int, int>(
            LogLevel.Information,
            new EventId(0, "RetentionPurged"),
            "Retention purged {Count} captured requests older than {Days} days");

    private static readonly Action<ILogger, Exception> LogPurgeFailed =
        LoggerMessage.Define(
            LogLevel.Error,
            new EventId(1, "RetentionPurgeFailed"),
            "Could not purge expired captured requests");
}
