using Microsoft.AspNetCore.Mvc;
using Smart_X.Api.Processing;
using Smart_X.Shared;
using Smart_X.Shared.DTOs;

namespace Smart_X.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class CommandsController : ControllerBase
    {
        private readonly DeviceRegistry _registry;
        private readonly TelemetryPipeline _pipeline;
        private readonly CommandHistory _history;
        private readonly ActionHistory _actions;

        public CommandsController(DeviceRegistry registry, TelemetryPipeline pipeline, CommandHistory history, ActionHistory actions)
        {
            _registry = registry;
            _pipeline = pipeline;
            _history = history;
            _actions = actions;
        }

        [HttpPost]
        public IActionResult Issue(CommandRequest request)
        {
            if (!_registry.TryGet(request.DeviceId, out var device))
            {
                return NotFound($"Device {request.DeviceId} is not registered.");
            }

            if (device.Category != SensorCategory.Actuator)
            {
                return BadRequest($"{device.Description} is not an actuator, so it cannot take commands.");
            }

            if (!device.IsActive)
            {
                return BadRequest($"Device {request.DeviceId} is deactivated.");
            }

            var action = request.On ? "Turn ON" : "Turn OFF";
            return Ok(Apply(new List<DeviceState> { device }, request.On, $"{action} {device.Description}").ToResponse());
        }

        [HttpPost("all-off")]
        public IActionResult ShutDownAll()
        {
            var targets = _registry.Snapshot().Where(d => d.IsActive && d.Category == SensorCategory.Actuator).ToList();
            if (targets.Count == 0)
            {
                return NotFound("There are no active actuators to shut down.");
            }

            return Ok(Apply(targets, on: false, $"Shut down all actuators ({targets.Count})").ToResponse());
        }

        [HttpPost("undo")]
        public IActionResult Undo()
        {
            var record = _history.TryPop();
            if (record is null)
            {
                return NotFound("There is nothing to undo.");
            }

            foreach (var change in record.Changes)
            {
                Revert(change);
            }

            return Ok(record.ToResponse());
        }

        [HttpGet]
        public IActionResult GetHistory()
        {
            return Ok(_history.Snapshot().Select(c => c.ToResponse()).ToList());
        }

        // Holds each device at the commanded state (the simulator honours OverrideValue) and queues a
        // reading so the dashboard shows the change at once. Command packets use the priority lane.
        private CommandRecord Apply(List<DeviceState> targets, bool on, string description)
        {
            var value = on ? 1f : 0f;
            var changes = new List<DeviceChange>();

            foreach (var device in targets)
            {
                changes.Add(new DeviceChange(device.Mac, device.OverrideValue, device.LastValue));
                _registry.Update(device.Mac, d => d with { OverrideValue = value });
                _pipeline.Enqueue(new Packet(device.Mac, device.Category, value, DateTimeOffset.UtcNow, PacketRank.Command));
                _actions.Record(ActionKey.Command(device.Mac, on));
            }

            return _history.Push(description, changes);
        }

        private void Revert(DeviceChange change)
        {
            // The device may have been deleted since the command was issued.
            if (!_registry.TryGet(change.Mac, out var device)) return;

            _registry.Update(change.Mac, d => d with { OverrideValue = change.PreviousOverride });
            _pipeline.Enqueue(new Packet(device.Mac, device.Category, change.PreviousValue ?? 0f, DateTimeOffset.UtcNow, PacketRank.Command));
        }

    }
}
