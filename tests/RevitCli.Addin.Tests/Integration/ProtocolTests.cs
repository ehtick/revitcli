using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading.Tasks;
using RevitCli.Addin.Server;
using RevitCli.Addin.Services;
using RevitCli.Client;
using RevitCli.Shared;
using Xunit;

namespace RevitCli.Addin.Tests.Integration;

/// <summary>
/// Verifies the HTTP protocol layer: request routing, JSON serialization/deserialization,
/// and server lifecycle. Uses PlaceholderRevitOperations — does NOT test real Revit API.
/// These become true integration tests when PlaceholderRevitOperations is swapped
/// for RealRevitOperations on Windows + Revit.
/// </summary>
public class ProtocolTests : IDisposable
{
    private readonly ApiServer _server;
    private readonly RevitClient _client;
    private readonly int _actualPort;
    private readonly string _tempDir;
    private readonly string _serverInfoPath;
    private readonly string _realUserServerInfoPath;

    public ProtocolTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"revitcli_protocol_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _serverInfoPath = Path.Combine(_tempDir, "server.json");
        _realUserServerInfoPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".revitcli", "server.json");
        var requestedPort = GetAvailablePort();
        var operations = new PlaceholderRevitOperations();
        _server = new ApiServer(requestedPort, operations, serverInfoPath: _serverInfoPath);
        _server.Start();

        var info = ReadServerInfo();
        _actualPort = info.Port;
        _client = new RevitClient($"http://localhost:{_actualPort}", info.Token);
    }

    public void Dispose()
    {
        _client.Dispose();
        _server.Dispose();
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    private static int GetAvailablePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    [Fact]
    public void ProtocolServer_UsesTemporaryServerInfoPath()
    {
        Assert.NotEqual(_realUserServerInfoPath, _serverInfoPath);
        Assert.StartsWith(_tempDir, _serverInfoPath);
        Assert.True(File.Exists(_serverInfoPath));
    }

    [Fact]
    public void Start_WritesServerInfoToConfiguredPath()
    {
        Assert.True(File.Exists(_serverInfoPath));
        var info = ReadServerInfo();
        Assert.Equal(_actualPort, info.Port);
        Assert.False(string.IsNullOrWhiteSpace(info.Token));
    }

    [Fact]
    public async Task Start_WhenRequestedPortIsBusy_PublishesListeningFallbackPort()
    {
        var busyPort = GetAvailablePort();
        using var busyListener = new HttpListener();
        busyListener.Prefixes.Add($"http://localhost:{busyPort}/");
        busyListener.Start();

        var tempDir = Path.Combine(Path.GetTempPath(), $"revitcli_protocol_busy_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        var serverInfoPath = Path.Combine(tempDir, "server.json");

        using var server = new ApiServer(busyPort, new PlaceholderRevitOperations(), serverInfoPath: serverInfoPath);
        server.Start();

        try
        {
            var info = JsonSerializer.Deserialize<ServerInfo>(File.ReadAllText(serverInfoPath));
            Assert.NotNull(info);
            Assert.NotEqual(busyPort, info!.Port);

            using var client = new RevitClient($"http://localhost:{info.Port}", info.Token);
            var result = await client.GetStatusAsync();

            Assert.True(result.Success);
            Assert.NotNull(result.Data);
            Assert.Equal("2025", result.Data.RevitVersion);
        }
        finally
        {
            server.Dispose();
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    [Fact]
    public void Dispose_DoesNotDeleteServerInfoOwnedByDifferentToken()
    {
        var info = ReadServerInfo();
        info.Token = "different-token";
        File.WriteAllText(_serverInfoPath, JsonSerializer.Serialize(info));

        _server.Dispose();

        Assert.True(File.Exists(_serverInfoPath));
    }

    [Fact]
    public async Task Status_ReturnsPlaceholderData()
    {
        var result = await _client.GetStatusAsync();

        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.Equal("2025", result.Data.RevitVersion);
    }

    [Fact]
    public async Task QueryElements_ReturnsEmptyArray()
    {
        var result = await _client.QueryElementsAsync("walls", null);

        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.Empty(result.Data);
    }

    [Fact]
    public async Task QueryElementById_ReturnsPlaceholder()
    {
        var result = await _client.QueryElementByIdAsync(42);

        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.Equal(42, result.Data.Id);
    }

    [Fact]
    public async Task Export_ReturnsCompletedProgress()
    {
        var request = new ExportRequest
        {
            Format = "dwg",
            Sheets = new() { "A1" },
            OutputDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".revitcli", "test-exports")
        };

        var result = await _client.ExportAsync(request);

        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.Equal("completed", result.Data.Status);
        Assert.Equal(100, result.Data.Progress);
    }

    [Fact]
    public async Task SetParameter_ReturnsZeroAffected()
    {
        var request = new SetRequest
        {
            Category = "doors",
            Param = "Fire Rating",
            Value = "60min",
            DryRun = true
        };

        var result = await _client.SetParameterAsync(request);

        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.Equal(0, result.Data.Affected);
    }

    [Fact]
    public async Task Audit_ReturnsPlaceholderResult()
    {
        var request = new AuditRequest
        {
            RequiredParameters = new()
            {
                new RequiredParameterSpec
                {
                    Category = "doors",
                    Parameter = "Mark",
                    RequireNonEmpty = true,
                    Severity = "warning"
                }
            }
        };

        var result = await _client.AuditAsync(request);

        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.Equal(4, result.Data.Passed);
        Assert.Equal(1, result.Data.Failed);
        var issue = Assert.Single(result.Data.Issues);
        Assert.Equal("doors", issue.Category);
        Assert.Equal("Mark", issue.Parameter);
        Assert.Equal("doors", issue.Target);
        Assert.Equal("", issue.CurrentValue);
        Assert.Equal("D-100", issue.ExpectedValue);
        Assert.Equal("structured", issue.Source);
    }

    [Fact]
    public async Task Audit_NamingRequestRemainsPassingAndEmpty()
    {
        var request = new AuditRequest { Rules = new() { "naming" } };

        var result = await _client.AuditAsync(request);

        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.Equal(5, result.Data.Passed);
        Assert.Equal(0, result.Data.Failed);
        Assert.Empty(result.Data.Issues);
    }

    [Fact]
    public async Task CaptureSnapshot_ReturnsPlaceholderSnapshot()
    {
        var result = await _client.CaptureSnapshotAsync(new SnapshotRequest());

        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.Equal(1, result.Data.SchemaVersion);
        Assert.Equal("2025", result.Data.Revit.Version);
        Assert.True(result.Data.Categories.ContainsKey("walls"));
        Assert.Single(result.Data.Categories["walls"]);
        Assert.Equal(1001, result.Data.Categories["walls"][0].Id);
        Assert.Single(result.Data.Sheets);
        Assert.Single(result.Data.Schedules);
        Assert.Equal(1, result.Data.Summary.ElementCounts["walls"]);
    }

    [Fact]
    public async Task CaptureSnapshot_WithIncludeCategoriesFilter_RequestReaches()
    {
        var result = await _client.CaptureSnapshotAsync(new SnapshotRequest
        {
            IncludeCategories = new List<string> { "walls" },
            IncludeSheets = false
        });

        // Placeholder currently ignores filter; assert response is still well-formed.
        Assert.True(result.Success);
        Assert.NotNull(result.Data);
    }

    [Fact]
    public async Task PurgeFamilies_PostEndpoint_ReturnsStructuredResult()
    {
        var result = await _client.PurgeFamiliesAsync(new FamilyPurgeRequest
        {
            Ids = new List<long> { 5003 }
        });

        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.Single(result.Data!.Skipped);
        Assert.Equal(5003, result.Data.Skipped[0].Id);
    }

    [Fact]
    public async Task ExportFamilies_PostEndpoint_ReturnsStructuredResult()
    {
        var outputDir = Path.Combine(_tempDir, "families");
        var result = await _client.ExportFamiliesAsync(new FamilyExportRequest
        {
            Ids = new List<long> { 5001 },
            OutputDir = outputDir,
            Overwrite = true
        });

        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.Equal(Path.GetFullPath(outputDir), result.Data!.OutputDir);
        Assert.Single(result.Data.Exported);
        Assert.Equal(5001, result.Data.Exported[0].Id);
    }

    private ServerInfo ReadServerInfo()
    {
        var info = JsonSerializer.Deserialize<ServerInfo>(File.ReadAllText(_serverInfoPath));
        Assert.NotNull(info);
        return info!;
    }
}
