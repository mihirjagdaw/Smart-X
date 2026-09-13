using Microsoft.AspNetCore.Mvc;
using Smart_X.Api.Data;
using Smart_X.Shared;
using Smart_X.Shared.DTOs;

namespace Smart_X.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class TelemetryController : ControllerBase
    {
        private readonly AppDbContext _dbContext;
        private readonly TelemetryHistoryStore _telemetryHistoryStore;

        public TelemetryController(AppDbContext dbContext, TelemetryHistoryStore telemetryHistoryStore)
        {
            _dbContext = dbContext;
            _telemetryHistoryStore = telemetryHistoryStore;
        }

        [HttpPost]
        public async Task<IActionResult> IngestTelemetry(TelemetryIngestRequest request)
        {
            float normalizedValue;

            switch (request.ValueType)
            {
                case "float":
                    if (request.FloatValue is not float floatValue)
                    {
                        return BadRequest("ValueType is 'float' but FloatValue was not provided.");
                    }
                    var floatPacket = new TelemetryPacket<float>(request.DeviceId, request.Category, floatValue);
                    normalizedValue = floatPacket.Value;
                    break;
                case "int":
                    if (request.IntValue is not int intValue)
                    {
                        return BadRequest("ValueType is 'int' but IntValue was not provided.");
                    }
                    var intPacket = new TelemetryPacket<int>(request.DeviceId, request.Category, intValue);
                    normalizedValue = (float)intPacket.Value;
                    break;
                case "bool":
                    if (request.BoolValue is not bool boolValue)
                    {
                        return BadRequest("ValueType is 'bool' but BoolValue was not provided.");
                    }
                    var boolPacket = new TelemetryPacket<bool>(request.DeviceId, request.Category, boolValue);
                    normalizedValue = System.Convert.ToSingle(boolPacket.Value);
                    break;
                default:
                    return BadRequest("Invalid ValueType");
            }

            var reading = new SensorReading(request.DeviceId, request.Category, normalizedValue);
            _telemetryHistoryStore.AddBatch(request.DeviceId, request.Category, new[] { reading.Value });
            var record = new TelemetryRecord(0, request.DeviceId, request.Category, reading.Value, reading.Timestamp);
            _dbContext.TelemetryRecords.Add(record);
            await _dbContext.SaveChangesAsync();
            return Ok(reading);
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

    }
}

// Reference List:
// https://stackoverflow.com/questions/1042099/how-do-i-convert-int-decimal-to-float-in-c
// https://discussions.unity.com/t/how-to-typecast-boolean-to-float-and-viceversa/420059/2