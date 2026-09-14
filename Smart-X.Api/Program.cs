using Microsoft.EntityFrameworkCore;
using Smart_X.Api;
using Smart_X.Api.Data;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
builder.Services.AddControllers();

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("SmartXDb")));

builder.Services.AddSingleton<TelemetryHistoryStore>();
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
    scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.Migrate();
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
