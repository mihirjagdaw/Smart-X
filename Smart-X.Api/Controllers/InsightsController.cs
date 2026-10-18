using Microsoft.AspNetCore.Mvc;
using Smart_X.Api.Processing;

namespace Smart_X.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class InsightsController : ControllerBase
    {
        private readonly InsightEngine _engine;

        public InsightsController(InsightEngine engine)
        {
            _engine = engine;
        }

        [HttpGet]
        public IActionResult GetInsights()
        {
            return Ok(_engine.Build());
        }
    }
}
