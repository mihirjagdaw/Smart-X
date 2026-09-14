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
        private readonly IWebHostEnvironment _environment;

        public SensorsController(AppDbContext dbContext, IWebHostEnvironment environment)
        {
            _dbContext = dbContext;
            _environment = environment;
        }

        [HttpPost]
        public async Task<IActionResult> RegisterSensor([FromBody] CreateSensorRequest request)
        {
            var sensorProfile = new SensorProfile(0, request.MacAddress, request.Location, request.Category, request.Description, DateTimeOffset.UtcNow, isActive: true);

            _dbContext.Sensors.Add(sensorProfile);
            await _dbContext.SaveChangesAsync();
            return Ok(ToResponse(sensorProfile));
        }

        [HttpGet]
        public async Task<IActionResult> GetAllSensors()
        {
            var sensors = await _dbContext.Sensors
                .Select(s => new SensorProfileResponse(s.Id, s.MacAddress, s.Location, s.Category, s.Description, s.RegisteredAt, s.IsActive))
                .ToListAsync();
            return Ok(sensors);
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateSensor(int id, [FromBody] UpdateSensorRequest request)
        {
            var sensor = await _dbContext.Sensors.FindAsync(id);
            if (sensor == null)
            {
                return NotFound();
            }

            sensor.Location = request.Location;
            sensor.Category = request.Category;
            sensor.Description = request.Description;
            await _dbContext.SaveChangesAsync();

            return Ok(ToResponse(sensor));
        }

        [HttpPost("{id}/deactivate")]
        public async Task<IActionResult> DeactivateSensor(int id)
        {
            var sensor = await _dbContext.Sensors.FindAsync(id);
            if (sensor == null)
            {
                return NotFound();
            }

            sensor.IsActive = false;
            await _dbContext.SaveChangesAsync();

            return Ok(ToResponse(sensor));
        }

        [HttpPost("{id}/reactivate")]
        public async Task<IActionResult> ReactivateSensor(int id)
        {
            var sensor = await _dbContext.Sensors.FindAsync(id);
            if (sensor == null)
            {
                return NotFound();
            }

            sensor.IsActive = true;
            await _dbContext.SaveChangesAsync();

            return Ok(ToResponse(sensor));
        }

        private static SensorProfileResponse ToResponse(SensorProfile sensor) =>
            new(sensor.Id, sensor.MacAddress, sensor.Location, sensor.Category, sensor.Description, sensor.RegisteredAt, sensor.IsActive);

        [HttpPost("{id}/files")]
        public async Task<IActionResult> UploadSensorFile(int id, IFormFile file)
        {
            var sensor = await _dbContext.Sensors.FindAsync(id);
            if (sensor == null)
            {
                return NotFound();
            }
            else
            {
                if (file.Length > 5 * 1024 * 1024) // 5 MB limit
                {
                    return BadRequest("File size exceeds the maximum allowed size of 5 MB.");
                }
                string[] allowedExtensions = { ".jpg", ".jpeg", ".png", ".pdf", ".txt", ".log", ".json", ".xml", ".cfg", ".conf" };
                var extension = Path.GetExtension(file.FileName).ToLower();
                if (!allowedExtensions.Contains(extension))
                {
                    return BadRequest("File type is not allowed.");
                }

                var sensorFile = new SensorFile(0, id, file.FileName, file.ContentType, file.Length, DateTimeOffset.UtcNow);
                var uploadsFolder = Path.Combine(_environment.ContentRootPath, "uploads");
                var fullPath = Path.Combine(uploadsFolder, sensorFile.StoredFileName);

                using (var stream = new FileStream(fullPath, FileMode.Create))
                {
                    await file.CopyToAsync(stream);
                }

                _dbContext.SensorFiles.Add(sensorFile);
                await _dbContext.SaveChangesAsync();
                return Ok(new SensorFileResponse(sensorFile.Id, sensorFile.FileName, sensorFile.ContentType, sensorFile.SizeBytes, sensorFile.UploadedAt));
            }

        }

        [HttpGet("{id}/files")]
        public async Task<IActionResult> GetSensorFiles(int id)
        {
            var sensor = await _dbContext.Sensors.FindAsync(id);
            if (sensor == null)
            {
                return NotFound();
            }
            List<SensorFileResponse> files = await _dbContext.SensorFiles
                .Where(f => f.SensorId == id)
                .Select(f => new SensorFileResponse(f.Id, f.FileName, f.ContentType, f.SizeBytes, f.UploadedAt))
                .ToListAsync();
            return Ok(files);
        }

        [HttpGet("{id}/files/{fileId}")]
        public async Task<IActionResult> GetSensorFile(int id, int fileId)
        {
            var file = await _dbContext.SensorFiles.FindAsync(fileId);
            if (file == null || file.SensorId != id)
            {
                return NotFound();
            }

            var uploadsFolder = Path.Combine(_environment.ContentRootPath, "uploads");
            var fullPath = Path.Combine(uploadsFolder, file.StoredFileName);
            if (!System.IO.File.Exists(fullPath))
            {
                return NotFound();
            }
            return PhysicalFile(fullPath, file.ContentType, file.FileName);
        }
    }
}
