using System.Diagnostics;
using Xunit;

namespace Marv.Core.Tests.Integration.PluginLoading;

/// <summary>
/// Shared fixture that publishes the Marv host and plugin projects, then copies
/// only the executable and plugin DLLs to a temporary directory. The output is
/// built once and shared across all plugin loading integration tests.
/// </summary>
public class PublishedOutputFixture : IAsyncLifetime
{
    private string _outputDir = null!;

    /// <summary>The directory containing the published Marv executable.</summary>
    public string HostDir => _outputDir;

    /// <summary>The directory containing the published plugin DLLs.</summary>
    public string PluginDir => Path.Combine(_outputDir, "plugins");

    /// <summary>All expected plugin DLL names (without path).</summary>
    public static IReadOnlyList<string> ExpectedPluginFileNames =>
    [
        "Marv.Plugins.Auth.dll",
        "Marv.Plugins.AuthConsumer.dll",
        "Marv.Plugins.CannedResponses.dll",
        "Marv.Plugins.Greet.dll",
        "Marv.Plugins.Moderation.dll",
    ];

    public async Task InitializeAsync()
    {
        _outputDir = Path.Combine(Path.GetTempPath(), $"marv-test-{Guid.NewGuid():N}");
        var pluginDir = Path.Combine(_outputDir, "plugins");
        Directory.CreateDirectory(pluginDir);

        var repoRoot = FindRepoRoot();

        // Publish the host project (PublishSingleFile bundles everything into one executable)
        var hostPublishDir = Path.Combine(_outputDir, "_publish_host");
        await RunDotnetAsync(
            $"publish {Path.Combine(repoRoot, "src", "Marv", "Marv.csproj")} -c Release -o {hostPublishDir}",
            repoRoot);

        // Copy only the executable (not the entire publish tree)
        var executableName = OperatingSystem.IsWindows() ? "Marv.exe" : "Marv";
        var executablePath = Path.Combine(hostPublishDir, executableName);
        if (File.Exists(executablePath))
            File.Copy(executablePath, Path.Combine(_outputDir, executableName));

        // Build each plugin and copy only its DLL
        var pluginProjects = Directory.GetDirectories(
            Path.Combine(repoRoot, "src", "plugins"));

        foreach (var pluginProjectDir in pluginProjects)
        {
            var projectName = Path.GetFileName(pluginProjectDir);
            var csproj = Path.Combine(pluginProjectDir, $"{projectName}.csproj");
            if (!File.Exists(csproj))
                continue;

            await RunDotnetAsync($"build {csproj} -c Release", repoRoot);

            var dllPath = Path.Combine(
                pluginProjectDir, "bin", "Release", "net10.0", $"{projectName}.dll");
            if (File.Exists(dllPath))
                File.Copy(dllPath, Path.Combine(pluginDir, $"{projectName}.dll"));
        }

        // Clean up the intermediate host publish directory
        Directory.Delete(hostPublishDir, recursive: true);
    }

    public Task DisposeAsync()
    {
        if (Directory.Exists(_outputDir))
            Directory.Delete(_outputDir, recursive: true);
        return Task.CompletedTask;
    }

    private static string FindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir, "Marv.slnx")))
                return dir;
            dir = Directory.GetParent(dir)?.FullName;
        }
        throw new InvalidOperationException(
            "Could not find repository root (no Marv.slnx found in parent directories).");
    }

    private static async Task RunDotnetAsync(string arguments, string workingDirectory)
    {
        var psi = new ProcessStartInfo("dotnet", arguments)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to start: dotnet {arguments}");

        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
        {
            var stderr = await process.StandardError.ReadToEndAsync();
            throw new InvalidOperationException(
                $"'dotnet {arguments}' failed (exit code {process.ExitCode}):\n{stderr}");
        }
    }
}
