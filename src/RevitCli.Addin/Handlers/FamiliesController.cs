using System;
using System.Text.Json;
using System.Threading.Tasks;
using EmbedIO;
using EmbedIO.Routing;
using EmbedIO.WebApi;
using RevitCli.Shared;

namespace RevitCli.Addin.Handlers;

public class FamiliesController : WebApiController
{
    private readonly IRevitOperations _operations;

    public FamiliesController(IRevitOperations operations)
    {
        _operations = operations;
    }

    [Route(HttpVerbs.Get, "/families")]
    public async Task ListFamilies(
        [QueryField("unused")] bool unused,
        [QueryField("category")] string? category)
    {
        HttpContext.Response.ContentType = "application/json";
        using var writer = HttpContext.OpenResponseText();

        try
        {
            var request = new FamilyListRequest
            {
                IncludeUnplaced = unused,
                Category = string.IsNullOrWhiteSpace(category) ? null : category
            };

            var families = await _operations.ListFamiliesAsync(request);
            await writer.WriteAsync(JsonSerializer.Serialize(
                ApiResponse<FamilyInfo[]>.Ok(families)));
        }
        catch (ArgumentException ex)
        {
            HttpContext.Response.StatusCode = 400;
            await writer.WriteAsync(JsonSerializer.Serialize(
                ApiResponse<FamilyInfo[]>.Fail(ex.Message)));
        }
        catch (InvalidOperationException ex)
        {
            HttpContext.Response.StatusCode = 409;
            await writer.WriteAsync(JsonSerializer.Serialize(
                ApiResponse<FamilyInfo[]>.Fail(ex.Message)));
        }
        catch (Exception ex)
        {
            HttpContext.Response.StatusCode = 500;
            await writer.WriteAsync(JsonSerializer.Serialize(
                ApiResponse<FamilyInfo[]>.Fail(ex.Message)));
        }
    }

    [Route(HttpVerbs.Post, "/families/purge")]
    public async Task PurgeFamilies()
    {
        HttpContext.Response.ContentType = "application/json";
        using var writer = HttpContext.OpenResponseText();

        FamilyPurgeRequest? request;
        try
        {
            var body = await HttpContext.GetRequestBodyAsStringAsync();
            request = JsonSerializer.Deserialize<FamilyPurgeRequest>(body);
        }
        catch (JsonException ex)
        {
            HttpContext.Response.StatusCode = 400;
            await writer.WriteAsync(JsonSerializer.Serialize(
                ApiResponse<FamilyPurgeResult>.Fail($"Invalid JSON: {ex.Message}")));
            return;
        }

        if (request == null)
        {
            HttpContext.Response.StatusCode = 400;
            await writer.WriteAsync(JsonSerializer.Serialize(
                ApiResponse<FamilyPurgeResult>.Fail("Request body is required")));
            return;
        }

        try
        {
            var result = await _operations.PurgeFamiliesAsync(request);
            await writer.WriteAsync(JsonSerializer.Serialize(
                ApiResponse<FamilyPurgeResult>.Ok(result)));
        }
        catch (ArgumentException ex)
        {
            HttpContext.Response.StatusCode = 400;
            await writer.WriteAsync(JsonSerializer.Serialize(
                ApiResponse<FamilyPurgeResult>.Fail(ex.Message)));
        }
        catch (InvalidOperationException ex)
        {
            HttpContext.Response.StatusCode = 409;
            await writer.WriteAsync(JsonSerializer.Serialize(
                ApiResponse<FamilyPurgeResult>.Fail(ex.Message)));
        }
        catch (Exception ex)
        {
            HttpContext.Response.StatusCode = 500;
            await writer.WriteAsync(JsonSerializer.Serialize(
                ApiResponse<FamilyPurgeResult>.Fail(ex.Message)));
        }
    }

    [Route(HttpVerbs.Post, "/families/export")]
    public async Task ExportFamilies()
    {
        HttpContext.Response.ContentType = "application/json";
        using var writer = HttpContext.OpenResponseText();

        FamilyExportRequest? request;
        try
        {
            var body = await HttpContext.GetRequestBodyAsStringAsync();
            request = JsonSerializer.Deserialize<FamilyExportRequest>(body);
        }
        catch (JsonException ex)
        {
            HttpContext.Response.StatusCode = 400;
            await writer.WriteAsync(JsonSerializer.Serialize(
                ApiResponse<FamilyExportResult>.Fail($"Invalid JSON: {ex.Message}")));
            return;
        }

        if (request == null)
        {
            HttpContext.Response.StatusCode = 400;
            await writer.WriteAsync(JsonSerializer.Serialize(
                ApiResponse<FamilyExportResult>.Fail("Request body is required")));
            return;
        }

        try
        {
            var result = await _operations.ExportFamiliesAsync(request);
            await writer.WriteAsync(JsonSerializer.Serialize(
                ApiResponse<FamilyExportResult>.Ok(result)));
        }
        catch (ArgumentException ex)
        {
            HttpContext.Response.StatusCode = 400;
            await writer.WriteAsync(JsonSerializer.Serialize(
                ApiResponse<FamilyExportResult>.Fail(ex.Message)));
        }
        catch (InvalidOperationException ex)
        {
            HttpContext.Response.StatusCode = 409;
            await writer.WriteAsync(JsonSerializer.Serialize(
                ApiResponse<FamilyExportResult>.Fail(ex.Message)));
        }
        catch (Exception ex)
        {
            HttpContext.Response.StatusCode = 500;
            await writer.WriteAsync(JsonSerializer.Serialize(
                ApiResponse<FamilyExportResult>.Fail(ex.Message)));
        }
    }
}
