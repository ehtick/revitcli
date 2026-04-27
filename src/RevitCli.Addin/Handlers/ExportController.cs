using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using EmbedIO;
using EmbedIO.Routing;
using EmbedIO.WebApi;
using RevitCli.Shared;

namespace RevitCli.Addin.Handlers;

public class ExportController : WebApiController
{
    private readonly IRevitOperations _operations;

    public ExportController(IRevitOperations operations)
    {
        _operations = operations;
    }

    [Route(HttpVerbs.Post, "/export")]
    public async Task Export()
    {
        HttpContext.Response.ContentType = "application/json";
        using var writer = HttpContext.OpenResponseText();

        ExportRequest? request;
        try
        {
            var body = await HttpContext.GetRequestBodyAsStringAsync();
            request = JsonSerializer.Deserialize<ExportRequest>(body);
        }
        catch (JsonException ex)
        {
            HttpContext.Response.StatusCode = 400;
            await writer.WriteAsync(JsonSerializer.Serialize(
                ApiResponse<ExportProgress>.Fail($"Invalid JSON: {ex.Message}")));
            return;
        }

        if (request == null)
        {
            HttpContext.Response.StatusCode = 400;
            await writer.WriteAsync(JsonSerializer.Serialize(
                ApiResponse<ExportProgress>.Fail("Request body is required")));
            return;
        }

        if (!string.IsNullOrWhiteSpace(request.OutputDir))
        {
            string normalized;
            try
            {
                // GetFullPath is the only canonical way to collapse "..", symlinks-as-text,
                // mixed separators, and trailing dots; the literal ".." check by itself was
                // bypassable with encoded variants and absolute paths.
                normalized = Path.GetFullPath(request.OutputDir);
            }
            catch (Exception ex) when (ex is ArgumentException or PathTooLongException
                                        or NotSupportedException or System.Security.SecurityException)
            {
                HttpContext.Response.StatusCode = 400;
                await writer.WriteAsync(JsonSerializer.Serialize(
                    ApiResponse<ExportProgress>.Fail($"OutputDir is not a valid path: {ex.Message}")));
                return;
            }

            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (string.IsNullOrEmpty(userProfile))
            {
                HttpContext.Response.StatusCode = 500;
                await writer.WriteAsync(JsonSerializer.Serialize(
                    ApiResponse<ExportProgress>.Fail("Could not determine user home directory.")));
                return;
            }

            var userProfileNormalized = Path.GetFullPath(userProfile)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            if (!normalized.StartsWith(userProfileNormalized, StringComparison.OrdinalIgnoreCase))
            {
                HttpContext.Response.StatusCode = 400;
                await writer.WriteAsync(JsonSerializer.Serialize(
                    ApiResponse<ExportProgress>.Fail("OutputDir must be within the user's home directory.")));
                return;
            }
            request.OutputDir = normalized;
        }

        try
        {
            var progress = await _operations.ExportAsync(request);
            await writer.WriteAsync(JsonSerializer.Serialize(
                ApiResponse<ExportProgress>.Ok(progress)));
        }
        catch (ArgumentException ex)
        {
            HttpContext.Response.StatusCode = 400;
            await writer.WriteAsync(JsonSerializer.Serialize(
                ApiResponse<ExportProgress>.Fail(ex.Message)));
        }
        catch (InvalidOperationException ex)
        {
            HttpContext.Response.StatusCode = 409;
            await writer.WriteAsync(JsonSerializer.Serialize(
                ApiResponse<ExportProgress>.Fail(ex.Message)));
        }
        catch (Exception ex)
        {
            HttpContext.Response.StatusCode = 500;
            await writer.WriteAsync(JsonSerializer.Serialize(
                ApiResponse<ExportProgress>.Fail(ex.Message)));
        }
    }

    [Route(HttpVerbs.Get, "/tasks/{taskId}")]
    public async Task GetProgress(string taskId)
    {
        HttpContext.Response.ContentType = "application/json";
        using var writer = HttpContext.OpenResponseText();

        try
        {
            var progress = await _operations.GetExportProgressAsync(taskId);
            await writer.WriteAsync(JsonSerializer.Serialize(
                ApiResponse<ExportProgress>.Ok(progress)));
        }
        catch (Exception ex)
        {
            HttpContext.Response.StatusCode = 500;
            await writer.WriteAsync(JsonSerializer.Serialize(
                ApiResponse<ExportProgress>.Fail(ex.Message)));
        }
    }
}
