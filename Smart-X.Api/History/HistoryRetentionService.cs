namespace Smart_X.Api.History
{
    /// <summary>
    /// Keeps the history from growing for ever: once an hour, readings older than History:RetentionDays are
    /// deleted from the database and dropped from the archive. (They are also removed at startup, by LoadAsync.)
    /// </summary>
    public class HistoryRetentionService : BackgroundService
    {
        private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(1);

        private readonly TelemetryArchive _archive;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IConfiguration _configuration;
        private readonly ILogger<HistoryRetentionService> _logger;

        public HistoryRetentionService(TelemetryArchive archive, IServiceScopeFactory scopeFactory, IConfiguration configuration, ILogger<HistoryRetentionService> logger)
        {
            _archive = archive;
            _scopeFactory = scopeFactory;
            _configuration = configuration;
            _logger = logger;
        }

        public static DateTimeOffset CutoffFrom(IConfiguration configuration) =>
            DateTimeOffset.UtcNow - TimeSpan.FromDays(configuration.GetValue("History:RetentionDays", 30));

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            using var timer = new PeriodicTimer(CheckInterval);
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<Data.AppDbContext>();
                    var deleted = await _archive.TrimAsync(db, CutoffFrom(_configuration), stoppingToken);
                    if (deleted > 0) _logger.LogInformation("History retention removed {Deleted} readings", deleted);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(ex, "History retention failed; it will try again next hour");
                }
            }
        }
    }
}
