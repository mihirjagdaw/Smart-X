using Microsoft.AspNetCore.Mvc;
using Smart_X.Api.Processing;
using Smart_X.Shared;
using Smart_X.Shared.DTOs;

namespace Smart_X.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class TelemetryController : ControllerBase
    {
        private const int MaxBatchSize = 500;

        private readonly TelemetryHistoryStore _telemetryHistoryStore;
        private readonly DeviceRegistry _registry;
        private readonly TelemetryPipeline _pipeline;

        public TelemetryController(TelemetryHistoryStore telemetryHistoryStore, DeviceRegistry registry, TelemetryPipeline pipeline)
        {
            _telemetryHistoryStore = telemetryHistoryStore;
            _registry = registry;
            _pipeline = pipeline;
        }

        [HttpPost]
        public IActionResult IngestTelemetry(TelemetryIngestRequest request)
        {
            var (packet, error, status) = BuildPacket(request);
            if (packet is null)
            {
                return StatusCode(status, error);
            }

            _pipeline.Enqueue(packet);
            return Accepted(new SensorReadingResponse(packet.Mac, packet.Category, packet.Value, packet.Timestamp));
        }

        [HttpPost("batch")]
        public IActionResult IngestBatch(List<TelemetryIngestRequest> requests)
        {
            if (requests.Count > MaxBatchSize)
            {
                return BadRequest($"A batch can hold at most {MaxBatchSize} readings.");
            }

            var accepted = 0;
            var rejected = new List<string>();
            foreach (var request in requests)
            {
                var (packet, error, _) = BuildPacket(request);
                if (packet is null)
                {
                    rejected.Add($"{request.DeviceId}: {error}");
                    continue;
                }

                _pipeline.Enqueue(packet);
                accepted++;
            }

            return Accepted(new BatchIngestResponse(accepted, rejected));
        }

        // Validates a request against the live registry (O(1) MAC lookup), normalizes the typed value
        // through the generic TelemetryPacket<T>, and ranks it: critical readings jump the queue.
        private (Packet? Packet, string? Error, int Status) BuildPacket(TelemetryIngestRequest request)
        {
            if (!_registry.TryGet(request.DeviceId, out var device))
            {
                return (null, $"Device {request.DeviceId} is not registered.", StatusCodes.Status404NotFound);
            }

            if (!device.IsActive)
            {
                return (null, $"Device {request.DeviceId} is deactivated.", StatusCodes.Status400BadRequest);
            }

            float normalizedValue;

            switch (request.ValueType)
            {
                case "float":
                    if (request.FloatValue is not float floatValue)
                    {
                        return (null, "ValueType is 'float' but FloatValue was not provided.", StatusCodes.Status400BadRequest);
                    }
                    var floatPacket = new TelemetryPacket<float>(request.DeviceId, request.Category, floatValue);
                    normalizedValue = floatPacket.Value;
                    break;
                case "int":
                    if (request.IntValue is not int intValue)
                    {
                        return (null, "ValueType is 'int' but IntValue was not provided.", StatusCodes.Status400BadRequest);
                    }
                    var intPacket = new TelemetryPacket<int>(request.DeviceId, request.Category, intValue);
                    normalizedValue = (float)intPacket.Value;
                    break;
                case "bool":
                    if (request.BoolValue is not bool boolValue)
                    {
                        return (null, "ValueType is 'bool' but BoolValue was not provided.", StatusCodes.Status400BadRequest);
                    }
                    var boolPacket = new TelemetryPacket<bool>(request.DeviceId, request.Category, boolValue);
                    normalizedValue = System.Convert.ToSingle(boolPacket.Value);
                    break;
                default:
                    return (null, "Invalid ValueType", StatusCodes.Status400BadRequest);
            }

            // The registry is the source of truth for MAC casing and category, not the request body.
            var rank = AlertRules.Evaluate(device.Category, normalizedValue) == Severity.Critical
                ? PacketRank.Critical
                : PacketRank.Normal;

            return (new Packet(device.Mac, device.Category, normalizedValue, DateTimeOffset.UtcNow, rank), null, 0);
        }

        [HttpGet("{deviceId}")]
        public IActionResult GetHistory(string deviceId)
        {
            return Ok(_telemetryHistoryStore.GetHistory(deviceId));
        }

        [HttpGet]
        public IActionResult GetAllLatestReadings()
        {
            return Ok(_telemetryHistoryStore.GetAllLatestReadings());
        }

        [HttpGet("{deviceId}/trend")]
        public IActionResult GetTrend(string deviceId)
        {
            var result = _telemetryHistoryStore.GetTrend(deviceId);
            if (result is null)
            {
                return NotFound("Not enough history yet to compute a trend for this device.");
            }
            return Ok(new TrendResponse(result.Value.Delta.Value, result.Value.Trend));
        }

        [HttpGet("total/{category}")]
        public IActionResult GetCategoryTotal(SensorCategory category)
        {
            var total = _telemetryHistoryStore.GetCategoryTotal(category);
            if (total is null)
            {
                return NotFound("No active sensors reporting for this category yet.");
            }
            return Ok(total);
        }

    }
}

// Reference List:
// https://stackoverflow.com/questions/1042099/how-do-i-convert-int-decimal-to-float-in-c
// https://discussions.unity.com/t/how-to-typecast-boolean-to-float-and-viceversa/420059/2
