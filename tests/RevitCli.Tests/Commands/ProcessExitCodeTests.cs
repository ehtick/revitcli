using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using RevitCli.Commands;
using Xunit;

namespace RevitCli.Tests.Commands;

[Collection("Sequential")]
public sealed class ProcessExitCodeTests
{
    [Fact]
    public async Task Program_ReturnsHandlerEnvironmentExitCode()
    {
        var assemblyPath = typeof(DoctorCommand).Assembly.Location;
        var exePath = Path.ChangeExtension(assemblyPath, ".exe");
        var startInfo = OperatingSystem.IsWindows() && File.Exists(exePath)
            ? new ProcessStartInfo(exePath, "doctor --check-version 2023")
            : new ProcessStartInfo("dotnet", $"\"{assemblyPath}\" doctor --check-version 2023");

        startInfo.WorkingDirectory = Path.GetDirectoryName(assemblyPath)!;
        startInfo.RedirectStandardOutput = true;
        startInfo.RedirectStandardError = true;
        startInfo.UseShellExecute = false;

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start RevitCli process.");
        var stdout = await process.StandardOutput.ReadToEndAsync();
        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        Assert.Equal(1, process.ExitCode);
        Assert.Contains("Unsupported Revit version 2023", stdout + stderr);
    }
}
