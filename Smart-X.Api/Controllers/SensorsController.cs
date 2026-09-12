using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Smart_X.Api.Data;
using Smart_X.Shared.DTOs;

namespace Smart_X.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class SensorsController : ControllerBase
    {
        private readonly AppDbContext _dbContext;

        public SensorsController(AppDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        [HttpPost]
        public async Task<IActionResult> RegisterSensor([FromBody] CreateSensorRequest request)
        {
            var sensorProfile = new SensorProfile(0, request.MacAddress, request.Location, request.Category, DateTimeOffset.UtcNow);

            _dbContext.Sensors.Add(sensorProfile);
            await _dbContext.SaveChangesAsync();
            return Ok(sensorProfile);
        }

        [HttpGet]
        public async Task<IActionResult> GetAllSensors()
        {
            return Ok(await _dbContext.Sensors.ToListAsync());
        }
    }
}
