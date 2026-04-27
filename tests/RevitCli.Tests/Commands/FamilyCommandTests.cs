using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using RevitCli.Client;
using RevitCli.Commands;
using RevitCli.Shared;
using Xunit;

namespace RevitCli.Tests.Commands;

public class FamilyCommandTests
{
    private static FamilyInfo[] SampleFamilies() => new[]
    {
        new FamilyInfo
        {
            Id = 5001,
            Name = "M_Single-Flush",
            Category = "Doors",
            IsInPlace = false,
            IsLoadable = true,
            FilePath = null,
            IsPlaced = true
        },
        new FamilyInfo
        {
            Id = 5002,
            Name = "M_Fixed",
            Category = "Windows",
            IsInPlace = false,
            IsLoadable = true,
            FilePath = null,
            IsPlaced = false
        }
    };

    [Fact]
    public async Task Ls_TableOutput_ListsAllFamiliesAndUsesFamiliesEndpoint()
    {
        var handler = new FamilyQueueHttpHandler();
        handler.Enqueue("/api/families", HttpStatusCode.OK,
            ApiResponse<FamilyInfo[]>.Ok(SampleFamilies()));
        var client = new RevitClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost:17839") });
        var writer = new StringWriter();

        var exitCode = await FamilyCommand.ExecuteListAsync(client, false, null, "table", writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("M_Single-Flush", output);
        Assert.Contains("M_Fixed", output);
        Assert.Contains("Doors", output);
        Assert.Single(handler.Requests);
        Assert.Equal("/api/families", handler.Requests[0].Path);
        Assert.Equal("", handler.Requests[0].Query);
    }

    [Fact]
    public async Task Ls_JsonOutput_PrintsRawArray()
    {
        var handler = new FamilyQueueHttpHandler();
        handler.Enqueue("/api/families", HttpStatusCode.OK,
            ApiResponse<FamilyInfo[]>.Ok(SampleFamilies()));
        var client = new RevitClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost:17839") });
        var writer = new StringWriter();

        var exitCode = await FamilyCommand.ExecuteListAsync(client, false, null, "json", writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("\"name\"", output);
        Assert.Contains("M_Single-Flush", output);
        // Confirm it round-trips back to FamilyInfo[].
        var parsed = JsonSerializer.Deserialize<FamilyInfo[]>(output);
        Assert.NotNull(parsed);
        Assert.Equal(2, parsed!.Length);
    }

    [Fact]
    public async Task Ls_CsvOutput_HasExpectedHeader()
    {
        var handler = new FamilyQueueHttpHandler();
        handler.Enqueue("/api/families", HttpStatusCode.OK,
            ApiResponse<FamilyInfo[]>.Ok(SampleFamilies()));
        var client = new RevitClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost:17839") });
        var writer = new StringWriter();

        var exitCode = await FamilyCommand.ExecuteListAsync(client, false, null, "csv", writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("Id,Name,Category,IsInPlace,IsPlaced,FilePath", output);
        Assert.Contains("5001,M_Single-Flush,Doors,false,true,", output);
    }

    [Fact]
    public async Task Ls_UnusedFlag_PassesUnusedTrueInQueryString()
    {
        var handler = new FamilyQueueHttpHandler();
        handler.Enqueue("/api/families", HttpStatusCode.OK,
            ApiResponse<FamilyInfo[]>.Ok(new[] { SampleFamilies()[1] }));
        var client = new RevitClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost:17839") });
        var writer = new StringWriter();

        var exitCode = await FamilyCommand.ExecuteListAsync(client, true, null, "table", writer);

        Assert.Equal(0, exitCode);
        Assert.Single(handler.Requests);
        Assert.Equal("/api/families", handler.Requests[0].Path);
        Assert.Contains("unused=true", handler.Requests[0].Query);
        Assert.Contains("M_Fixed", writer.ToString());
        Assert.DoesNotContain("M_Single-Flush", writer.ToString());
    }

    [Fact]
    public async Task Ls_CategoryFilter_IsPassedInQueryString()
    {
        var handler = new FamilyQueueHttpHandler();
        handler.Enqueue("/api/families", HttpStatusCode.OK,
            ApiResponse<FamilyInfo[]>.Ok(new[] { SampleFamilies()[0] }));
        var client = new RevitClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost:17839") });
        var writer = new StringWriter();

        var exitCode = await FamilyCommand.ExecuteListAsync(client, false, "Doors", "table", writer);

        Assert.Equal(0, exitCode);
        Assert.Single(handler.Requests);
        Assert.Equal("/api/families", handler.Requests[0].Path);
        Assert.Contains("category=Doors", handler.Requests[0].Query);
    }

    [Fact]
    public async Task Ls_BothFlags_BothPassedInQueryString()
    {
        var handler = new FamilyQueueHttpHandler();
        handler.Enqueue("/api/families", HttpStatusCode.OK,
            ApiResponse<FamilyInfo[]>.Ok(Array.Empty<FamilyInfo>()));
        var client = new RevitClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost:17839") });
        var writer = new StringWriter();

        var exitCode = await FamilyCommand.ExecuteListAsync(client, true, "Windows", "json", writer);

        Assert.Equal(0, exitCode);
        var query = handler.Requests[0].Query;
        Assert.Contains("unused=true", query);
        Assert.Contains("category=Windows", query);
    }

    [Fact]
    public async Task Ls_404Response_ReturnsErrorMentioningV18Addin()
    {
        var handler = new FamilyQueueHttpHandler();
        handler.Enqueue("/api/families", HttpStatusCode.NotFound,
            (ApiResponse<FamilyInfo[]>?)null);
        var client = new RevitClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost:17839") });
        var writer = new StringWriter();

        var exitCode = await FamilyCommand.ExecuteListAsync(client, false, null, "table", writer);

        Assert.Equal(1, exitCode);
        var output = writer.ToString();
        Assert.Contains("/api/families", output);
        Assert.Contains("v1.8", output);
    }

    [Fact]
    public async Task Ls_ServerDown_ReturnsConnectionError()
    {
        var handler = new ThrowingHandler();
        var client = new RevitClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost:17839") });
        var writer = new StringWriter();

        var exitCode = await FamilyCommand.ExecuteListAsync(client, false, null, "table", writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("not running", writer.ToString().ToLower());
    }

    [Fact]
    public async Task Ls_EmptyResult_PrintsEmptyTableMessage()
    {
        var handler = new FamilyQueueHttpHandler();
        handler.Enqueue("/api/families", HttpStatusCode.OK,
            ApiResponse<FamilyInfo[]>.Ok(Array.Empty<FamilyInfo>()));
        var client = new RevitClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost:17839") });
        var writer = new StringWriter();

        var exitCode = await FamilyCommand.ExecuteListAsync(client, false, null, "table", writer);

        Assert.Equal(0, exitCode);
        Assert.Contains("No families found", writer.ToString());
    }

    private sealed class FamilyQueueHttpHandler : HttpMessageHandler
    {
        private readonly Queue<(string Path, HttpStatusCode Status, string Body)> _responses = new();
        public List<(string Path, string Query)> Requests { get; } = new();

        public void Enqueue<T>(string path, HttpStatusCode status, ApiResponse<T>? response)
        {
            var body = response == null ? "" : JsonSerializer.Serialize(response);
            _responses.Enqueue((path, status, body));
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!;
            Requests.Add((uri.AbsolutePath, uri.Query.TrimStart('?')));
            var next = _responses.Dequeue();
            Assert.Equal(next.Path, uri.AbsolutePath);

            return Task.FromResult(new HttpResponseMessage(next.Status)
            {
                Content = new StringContent(next.Body, Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            throw new HttpRequestException("Connection refused");
        }
    }
}
