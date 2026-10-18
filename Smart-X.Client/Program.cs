using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Smart_X.Client;
using Smart_X.Client.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// Singleton, not scoped: the live-data service is a singleton and shares this client.
builder.Services.AddSingleton(sp => new HttpClient { BaseAddress = new Uri("http://localhost:5256") });
builder.Services.AddSingleton<AppState>();
builder.Services.AddSingleton<LiveDataService>();

await builder.Build().RunAsync();
