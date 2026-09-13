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
