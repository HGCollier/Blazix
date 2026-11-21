using Blazix.Docs.Components.Docs;

namespace Blazix.Docs;

/// <summary>The docs' services, registered by both the host (for prerendering) and the WebAssembly client.</summary>
public static class DocsServices
{
    public static IServiceCollection AddDocsServices(this IServiceCollection services)
    {
        // The examples' toasts go through one manager per user, shown by the Toaster in MainLayout.
        services.AddScoped<ToastManager>();
        services.AddScoped<SiteScript>();
        services.AddScoped<DocsConfig>();
        return services;
    }
}
