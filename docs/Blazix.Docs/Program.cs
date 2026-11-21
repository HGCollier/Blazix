using Blazix.Docs;
using Blazix.Docs.Components;

var builder = WebApplication.CreateBuilder(args);

// The pages run on WebAssembly (../Blazix.Docs.Client); the server prerenders them, so it has the same services.
builder.Services.AddRazorComponents()
    .AddInteractiveWebAssemblyComponents();
builder.Services.AddDocsServices();

// Development only: recompiles Tailwind as you edit, so hot reload shows new classes.
builder.Services.AddTailwindWatcher();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseWebAssemblyDebugging();
}
else
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveWebAssemblyRenderMode()
    .AddAdditionalAssemblies(typeof(DocsServices).Assembly);

await app.RunAsync();
