using Microsoft.AspNetCore.Mvc;
using Smart_X.Api.Processing;

namespace Smart_X.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class InsightsController : ControllerBase
    {
        private readonly PredictionService _predictions;

        public InsightsController(PredictionService predictions)
        {
            _predictions = predictions;
        }

        // The predictions are generated in the background; this only returns the latest ones.
        [HttpGet]
        public IActionResult GetInsights()
        {
            return Ok(_predictions.Latest);
        }
    }
}
