using System.Diagnostics;
using System.Reflection;
using Blazix.Registry;

namespace Blazix.Registry
{
    /// <summary>
    /// Development only. Runs the standalone Tailwind CLI in watch mode alongside the app, so a
    /// class you add to a .razor file is compiled into the CSS straight away and hot reload
    /// (dotnet watch, Visual Studio, Rider) pushes it to the browser. No Node.js involved.
    /// Tailwind.targets supplies the executable and paths as assembly metadata.
    /// </summary>
    public sealed class TailwindWatcher(IWebHostEnvironment environment, ILogger<TailwindWatcher> logger)
        : IHostedService, IDisposable
    {
        private Process? _process;

        public Task StartAsync(CancellationToken cancellationToken)
        {
            if (!environment.IsDevelopment())
            {
                return Task.CompletedTask;
            }

            var settings = typeof(TailwindWatcher).Assembly
                .GetCustomAttributes<AssemblyMetadataAttribute>()
                .Where(a => a.Key.StartsWith("Tailwind.", StringComparison.Ordinal))
                .ToDictionary(a => a.Key, a => a.Value ?? "");

            if (!settings.TryGetValue("Tailwind.Executable", out var executable) || !File.Exists(executable))
            {
                logger.LogWarning("Tailwind watcher not started: no Tailwind CLI found. Run a Debug build first.");
                return Task.CompletedTask;
            }

            var startInfo = new ProcessStartInfo(executable)
            {
                WorkingDirectory = environment.ContentRootPath,
                UseShellExecute = false,
                // Tailwind's --watch exits when stdin closes, so the watcher can never outlive the app,
                // even if the app is killed rather than shut down cleanly.
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            startInfo.ArgumentList.Add("--input");
            startInfo.ArgumentList.Add(settings["Tailwind.Input"]);
            startInfo.ArgumentList.Add("--output");
            startInfo.ArgumentList.Add(settings["Tailwind.Output"]);
            startInfo.ArgumentList.Add("--watch");

            _process = Process.Start(startInfo);
            if (_process is null)
            {
                logger.LogWarning("Tailwind watcher failed to start.");
                return Task.CompletedTask;
            }

            _process.OutputDataReceived += (_, e) => Log(e.Data);
            _process.ErrorDataReceived += (_, e) => Log(e.Data);
            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();

            logger.LogInformation("Tailwind watching {Input} -> {Output}", settings["Tailwind.Input"], settings["Tailwind.Output"]);
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            Dispose();
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            try
            {
                if (_process is { HasExited: false })
                {
                    _process.Kill(entireProcessTree: true);
                }
            }
            catch (InvalidOperationException)
            {
                // Exited between the check and the kill.
            }

            _process?.Dispose();
            _process = null;
        }

        private void Log(string? line)
        {
            if (!string.IsNullOrWhiteSpace(line))
            {
                logger.LogDebug("{TailwindOutput}", line);
            }
        }
    }
}

// In this namespace so AddTailwindWatcher() needs no extra using in Program.cs.
namespace Microsoft.Extensions.DependencyInjection
{
    public static class TailwindWatcherExtensions
    {
        /// <summary>Keeps Tailwind's output up to date while you develop. Does nothing outside Development.</summary>
        public static IServiceCollection AddTailwindWatcher(this IServiceCollection services) =>
            services.AddHostedService<TailwindWatcher>();
    }
}
