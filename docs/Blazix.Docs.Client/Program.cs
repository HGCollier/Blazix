using Blazix.Docs;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

// The docs in the browser. The host (../Blazix.Docs) prerenders each page with the same services, then this takes over.
var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.Services.AddDocsServices();

await builder.Build().RunAsync();
