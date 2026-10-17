using Microsoft.AspNetCore.Mvc;
using Smart_X.Api.Processing;

namespace Smart_X.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AlertsController : ControllerBase
    {
        private readonly AlertTracker _alerts;

        public AlertsController(AlertTracker alerts)
        {
            _alerts = alerts;
        }

        [HttpGet]
        public IActionResult GetAlerts()
        {
            return Ok(_alerts.Snapshot());
        }
    }
}
