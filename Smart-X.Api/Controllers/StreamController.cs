using Microsoft.AspNetCore.Mvc;
using Smart_X.Api.Processing;
using Smart_X.Shared.DTOs;

namespace Smart_X.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class StreamController : ControllerBase
    {
        private readonly TelemetryPipeline _pipeline;

        public StreamController(TelemetryPipeline pipeline)
        {
            _pipeline = pipeline;
        }

        [HttpGet("stats")]
        public IActionResult GetStats()
        {
            return Ok(new StreamStatsResponse(_pipeline.GetStats(), _pipeline.GetRecent()));
        }
    }
}
