using Microsoft.EntityFrameworkCore;
using Smart_X.Api;
using Smart_X.Api.Data;
using Smart_X.Api.History;
using Smart_X.Api.Network;
using Smart_X.Api.Processing;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
builder.Services.AddControllers();

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("SmartXDb")));

builder.Services.AddSingleton<TelemetryHistoryStore>();
builder.Services.AddSingleton<TelemetryArchive>();
builder.Services.AddSingleton<DeviceRegistry>();
builder.Services.AddSingleton<TelemetryPipeline>();
builder.Services.AddSingleton<AlertTracker>();
builder.Services.AddSingleton<SimulatorControl>();
builder.Services.AddSingleton<CommandHistory>();
builder.Services.AddSingleton<ActionHistory>();
builder.Services.AddSingleton<ActionFeed>();
builder.Services.AddSingleton<IActionRecorder>(sp => sp.GetRequiredService<ActionFeed>());
builder.Services.AddSingleton<InsightEngine>();
builder.Services.AddSingleton<PredictionService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<PredictionService>());
builder.Services.AddHostedService<TelemetryProcessorService>();
builder.Services.AddHostedService<DisconnectMonitorService>();
builder.Services.AddHostedService<HistoryRetentionService>();
builder.Services.AddSingleton(builder.Configuration.GetSection("Mesh").Get<MeshOptions>() ?? new MeshOptions());
builder.Services.AddSingleton<TopologyService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<TopologyService>());
builder.Services.AddHostedService<TelemetrySimulatorService>();

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowClient", policy =>
        policy.WithOrigins("http://localhost:5128")
              .AllowAnyHeader()
              .AllowAnyMethod());
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
    app.Services.GetRequiredService<DeviceRegistry>().Load(db.Sensors.AsNoTracking().ToList());

    // Rebuild each device's history tree from the database before any request or background service runs.
    var archive = app.Services.GetRequiredService<TelemetryArchive>();
    var loadStarted = System.Diagnostics.Stopwatch.StartNew();
    var loaded = await archive.LoadAsync(db, HistoryRetentionService.CutoffFrom(app.Configuration));
    app.Logger.LogInformation("Telemetry archive loaded: {Readings} readings for {Devices} devices in {Elapsed} ms",
        loaded, archive.DeviceCount, loadStarted.ElapsedMilliseconds);
}

Directory.CreateDirectory(Path.Combine(app.Environment.ContentRootPath, "uploads"));

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwaggerUI(options =>
        options.SwaggerEndpoint("/openapi/v1.json", "Smart-X API v1"));
}

app.UseHttpsRedirection();

app.UseCors("AllowClient");

app.MapControllers();

app.Run();
