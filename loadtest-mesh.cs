#:property PublishAot=false
using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;

// Load test for a running API: registers N devices, sends readings at the rate the simulator would, then
// reports how the ingest queue, the topology and the dashboard responses behave.
//   dotnet run loadtest-mesh.cs -- [devices=200] [seconds=30] [apiUrl=http://localhost:5256]
// Run it against a scratch database, because it registers real devices:
//   $env:ConnectionStrings__SmartXDb = "Data Source=scratch.db"; dotnet run --project Smart-X.Api

CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
var devices = args.Length > 0 ? int.Parse(args[0]) : 200;
var seconds = args.Length > 1 ? int.Parse(args[1]) : 30;
var baseUrl = args.Length > 2 ? args[2] : "http://localhost:5256";

using var http = new HttpClient { BaseAddress = new Uri(baseUrl), Timeout = TimeSpan.FromSeconds(60) };
var blocks = Math.Max(1, (int)Math.Ceiling(devices / 40.0));

string Mac(int i) => $"AA:BB:{i / 256 % 256:X2}:{i % 256:X2}:00:01";

// Registering ------------------------------------------------------------------------------------------------
var sw = Stopwatch.StartNew();
var failed = 0;
for (var i = 0; i < devices; i++)
{
    var category = i % 3;   // 0 Environmental, 1 PowerConsumption, 2 Actuator
    var response = await http.PostAsJsonAsync("api/Sensors", new
    {
        MacAddress = Mac(i),
        Location = $"Estate/Block {(char)('A' + i % blocks % 26)}{(blocks > 26 ? i % blocks / 26 : "")}/Row {i / blocks % 10}",
        Category = category,
        Description = $"Load {i}"
    });
    if (!response.IsSuccessStatusCode && failed++ == 0) Console.WriteLine($"First rejection: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
}
Console.WriteLine($"Registered {devices - failed}/{devices} devices in {sw.ElapsedMilliseconds} ms");

// Sizes and times of the responses the client polls --------------------------------------------------------
async Task<(long Bytes, double Ms)> Measure(string url, bool gzip = false)
{
    using var request = new HttpRequestMessage(HttpMethod.Get, url);
    if (gzip) request.Headers.AcceptEncoding.ParseAdd("gzip");
    var timer = Stopwatch.StartNew();
    using var reply = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
    var body = await reply.Content.ReadAsStreamAsync();
    long bytes = 0;
    var buffer = new byte[81920];
    int read;
    while ((read = await body.ReadAsync(buffer)) > 0) bytes += read;
    return (bytes, timer.Elapsed.TotalMilliseconds);
}

async Task Report(string label, string url)
{
    var times = new List<double>();
    long bytes = 0;
    for (var i = 0; i < 10; i++)
    {
        var (b, ms) = await Measure(url);
        bytes = b;
        times.Add(ms);
    }
    var (gz, _) = await Measure(url, gzip: true);
    times.Sort();
    Console.WriteLine($"{label,-28} {bytes / 1024.0,8:0.0} KB  (compressed {gz / 1024.0,7:0.0} KB)  median {times[times.Count / 2],6:0.0} ms");
}

await Task.Delay(2000);   // let the topology service pick the devices up
Console.WriteLine();
await Report("GET api/Topology", "api/Topology");
await Report("GET api/Stream/snapshot", "api/Stream/snapshot");
await Report("GET api/Devices/search", "api/Devices/search?record=false");
await Report("GET api/Topology/route", $"api/Topology/route/{Mac(devices - 1)}");

// Ingest: every device reports once per 5 s, like the simulator does ------------------------------------------
Console.WriteLine($"\nSending {devices} readings every 5 s for {seconds} s...");
var rng = new Random(11);
var run = Stopwatch.StartNew();
var sent = 0;
var peakQueue = 0;
while (run.Elapsed.TotalSeconds < seconds)
{
    var tick = Stopwatch.StartNew();
    for (var from = 0; from < devices; from += 500)
    {
        var batch = Enumerable.Range(from, Math.Min(500, devices - from)).Select(i => (i % 3) switch
        {
            0 => new { DeviceId = Mac(i), Category = 0, ValueType = "float", FloatValue = (float?)(40 + rng.NextDouble() * 10), IntValue = (int?)null, BoolValue = (bool?)null },
            1 => new { DeviceId = Mac(i), Category = 1, ValueType = "int", FloatValue = (float?)null, IntValue = (int?)(1000 + rng.Next(400)), BoolValue = (bool?)null },
            _ => new { DeviceId = Mac(i), Category = 2, ValueType = "bool", FloatValue = (float?)null, IntValue = (int?)null, BoolValue = (bool?)(rng.Next(2) == 0) }
        }).ToList();
        var reply = await http.PostAsJsonAsync("api/Telemetry/batch", batch);
        reply.EnsureSuccessStatusCode();
        sent += batch.Count;
    }

    var stats = await http.GetFromJsonAsync<JsonElement>("api/Stream/stats");
    var queued = stats.GetProperty("stats").GetProperty("standardDepth").GetInt32();
    peakQueue = Math.Max(peakQueue, queued);
    Console.WriteLine($"  t={run.Elapsed.TotalSeconds,5:0}s  sent {sent,6}  waiting in queue {queued,6}");

    var rest = TimeSpan.FromSeconds(5) - tick.Elapsed;
    if (rest > TimeSpan.Zero) await Task.Delay(rest);
}

// Let the queue drain and see how long that takes.
var drain = Stopwatch.StartNew();
while (drain.Elapsed.TotalSeconds < 120)
{
    var stats = await http.GetFromJsonAsync<JsonElement>("api/Stream/stats");
    if (stats.GetProperty("stats").GetProperty("standardDepth").GetInt32() == 0) break;
    await Task.Delay(500);
}
var processed = (await http.GetFromJsonAsync<JsonElement>("api/Stream/stats")).GetProperty("stats").GetProperty("processedStandard").GetInt64();
Console.WriteLine($"\nSent {sent}, processed {processed}, peak queue {peakQueue}, drained in {drain.Elapsed.TotalSeconds:0.0} s after sending stopped");

// Burst: the most the processor can absorb, found by sending a flood and timing how long it takes to clear.
{
    const int Burst = 5000;
    var before = (await http.GetFromJsonAsync<JsonElement>("api/Stream/stats")).GetProperty("stats").GetProperty("processedStandard").GetInt64();
    var timer = Stopwatch.StartNew();
    for (var b = 0; b < Burst / 500; b++)
    {
        var batch = Enumerable.Range(0, 500).Select(i => new { DeviceId = Mac((b * 500 + i) % devices / 3 * 3), Category = 0, ValueType = "float", FloatValue = (float?)(40 + rng.NextDouble() * 10), IntValue = (int?)null, BoolValue = (bool?)null }).ToList();
        (await http.PostAsJsonAsync("api/Telemetry/batch", batch)).EnsureSuccessStatusCode();
    }
    var accepted = timer.Elapsed.TotalSeconds;
    long done;
    do
    {
        await Task.Delay(100);
        done = (await http.GetFromJsonAsync<JsonElement>("api/Stream/stats")).GetProperty("stats").GetProperty("processedStandard").GetInt64() - before;
    } while (done < Burst && timer.Elapsed.TotalSeconds < 300);
    Console.WriteLine($"\nBurst of {Burst}: accepted in {accepted:0.0} s, all processed after {timer.Elapsed.TotalSeconds:0.0} s = {done / timer.Elapsed.TotalSeconds:0} readings/s");
}

Console.WriteLine();
await Report("GET api/Topology (loaded)", "api/Topology");
await Report("GET api/Stream/snapshot", "api/Stream/snapshot");
