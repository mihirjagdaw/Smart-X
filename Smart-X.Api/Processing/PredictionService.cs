using Smart_X.Shared.DTOs;

namespace Smart_X.Api.Processing
{
    /// <summary>
    /// Generates the suggestions and warnings in the background, so no request has to wait for them. It sleeps
    /// until an action arrives (or a second passes, to keep the trend warnings fresh), learns from the actions,
    /// recomputes the predictions on a worker thread, and publishes the result. Requests only read the latest one.
    /// </summary>
    public class PredictionService : BackgroundService
    {
        private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(1);

        private readonly ActionFeed _feed;
        private readonly ActionHistory _history;
        private readonly InsightEngine _engine;
        private readonly ILogger<PredictionService> _logger;

        private volatile InsightsResponse _latest = new(new List<Suggestion>(), new List<DeviceWarning>());

        public PredictionService(ActionFeed feed, ActionHistory history, InsightEngine engine, ILogger<PredictionService> logger)
        {
            _feed = feed;
            _history = history;
            _engine = engine;
            _logger = logger;
        }

        /// <summary>The most recently generated predictions. Reading it never does any work.</summary>
        public InsightsResponse Latest => _latest;

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                while (_feed.Reader.TryRead(out var action))
                {
                    _history.Record(action.Key, action.At);
                }

                try
                {
                    _latest = await Task.Run(_engine.Build, stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(ex, "Prediction refresh failed; keeping the previous predictions");
                }

                await WaitForActionOrTimeoutAsync(stoppingToken);
            }
        }

        private async Task WaitForActionOrTimeoutAsync(CancellationToken stoppingToken)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            timeout.CancelAfter(RefreshInterval);

            try
            {
                await _feed.Reader.WaitToReadAsync(timeout.Token);
            }
            catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
            {
                // The refresh interval passed with nothing new, which is the normal quiet case.
            }
        }
    }
}
