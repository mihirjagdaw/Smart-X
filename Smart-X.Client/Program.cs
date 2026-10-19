using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Smart_X.Client;
using Smart_X.Client.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// Registered as a singleton so the app-wide LiveDataService visibly shares one client (in WebAssembly a scoped service behaves the same).
builder.Services.AddSingleton(sp => new HttpClient { BaseAddress = new Uri("http://localhost:5256") });
builder.Services.AddSingleton<AppState>();
builder.Services.AddSingleton<LiveDataService>();

await builder.Build().RunAsync();
