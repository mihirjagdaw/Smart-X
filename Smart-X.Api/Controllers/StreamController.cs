using Microsoft.AspNetCore.Mvc;
using Smart_X.Api.Processing;
using Smart_X.Shared.DTOs;

namespace Smart_X.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class StreamController : ControllerBase
    {
        private const int FeedLength = 100;

        private readonly TelemetryPipeline _pipeline;
        private readonly AlertTracker _alerts;
        private readonly CommandHistory _commands;
        private readonly InsightEngine _insights;
        private readonly DeviceRegistry _registry;

        public StreamController(TelemetryPipeline pipeline, AlertTracker alerts, CommandHistory commands, InsightEngine insights, DeviceRegistry registry)
        {
            _pipeline = pipeline;
            _alerts = alerts;
            _commands = commands;
            _insights = insights;
            _registry = registry;
        }

        [HttpGet("stats")]
        public IActionResult GetStats()
        {
            return Ok(new StreamStatsResponse(_pipeline.GetStats(), _pipeline.GetRecent()));
        }

        // Everything the Command Stream page shows, so the dashboard polls with one request.
        [HttpGet("snapshot")]
        public IActionResult GetSnapshot()
        {
            var alerts = _alerts.Snapshot();

            return Ok(new StreamSnapshot(
                _pipeline.GetStats(),
                _pipeline.GetRecent().Take(FeedLength).ToList(),
                alerts,
                _commands.Snapshot().Select(c => c.ToResponse()).ToList(),
                _insights.Build(),
                DeviceSummaries.Build(_registry.Snapshot(), alerts.Active)));
        }
    }
}
