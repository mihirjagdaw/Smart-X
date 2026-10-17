using Microsoft.AspNetCore.Mvc;
using Smart_X.Api.Processing;
using Smart_X.Shared.DTOs;

namespace Smart_X.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class DevicesController : ControllerBase
    {
        private readonly DeviceRegistry _registry;
        private readonly SimulatorControl _simulatorControl;

        public DevicesController(DeviceRegistry registry, SimulatorControl simulatorControl)
        {
            _registry = registry;
            _simulatorControl = simulatorControl;
        }

        // Testing hook: makes the simulator stop (or resume) reporting for one device.
        [HttpPost("{mac}/simulate-offline")]
        public IActionResult ToggleSimulatedOffline(string mac)
        {
            if (!_registry.TryGet(mac, out var device))
            {
                return NotFound($"Device {mac} is not registered.");
            }

            return Ok(new SimulatedOfflineResponse(device.Mac, _simulatorControl.ToggleOffline(device.Mac)));
        }
    }
}
