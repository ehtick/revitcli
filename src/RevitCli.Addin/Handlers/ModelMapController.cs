using System;
using System.Text.Json;
using System.Threading.Tasks;
using EmbedIO;
using EmbedIO.Routing;
using EmbedIO.WebApi;
using RevitCli.Shared;

namespace RevitCli.Addin.Handlers;

public class ModelMapController : WebApiController
{
    private readonly IRevitOperations _operations;

    public ModelMapController(IRevitOperations operations)
    {
        _operations = operations;
    }

    [Route(HttpVerbs.Get, "/model/map")]
    public async Task ListModelMapElements()
    {
        HttpContext.Response.ContentType = "application/json";
        using var writer = HttpContext.OpenResponseText();

        try
        {
            var elements = await _operations.ListModelMapElementsAsync();
            await writer.WriteAsync(JsonSerializer.Serialize(
                ApiResponse<ModelMapElementInfo[]>.Ok(elements)));
        }
        catch (InvalidOperationException ex)
        {
            HttpContext.Response.StatusCode = 400;
            await writer.WriteAsync(JsonSerializer.Serialize(
                ApiResponse<ModelMapElementInfo[]>.Fail(ex.Message)));
        }
        catch (Exception ex)
        {
            HttpContext.Response.StatusCode = 500;
            await writer.WriteAsync(JsonSerializer.Serialize(
                ApiResponse<ModelMapElementInfo[]>.Fail(ex.Message)));
        }
    }

    [Route(HttpVerbs.Post, "/model/map/fix")]
    public async Task FixModelMap()
    {
        HttpContext.Response.ContentType = "application/json";
        using var writer = HttpContext.OpenResponseText();

        ModelMapFixRequest? request;
        try
        {
            var body = await HttpContext.GetRequestBodyAsStringAsync();
            request = JsonSerializer.Deserialize<ModelMapFixRequest>(body);
        }
        catch (JsonException ex)
        {
            HttpContext.Response.StatusCode = 400;
            await writer.WriteAsync(JsonSerializer.Serialize(
                ApiResponse<ModelMapFixResult>.Fail($"Invalid JSON: {ex.Message}")));
            return;
        }

        if (request == null)
        {
            HttpContext.Response.StatusCode = 400;
            await writer.WriteAsync(JsonSerializer.Serialize(
                ApiResponse<ModelMapFixResult>.Fail("Request body is required")));
            return;
        }

        try
        {
            var result = await _operations.ApplyModelMapFixAsync(request);
            await writer.WriteAsync(JsonSerializer.Serialize(
                ApiResponse<ModelMapFixResult>.Ok(result)));
        }
        catch (ArgumentException ex)
        {
            HttpContext.Response.StatusCode = 400;
            await writer.WriteAsync(JsonSerializer.Serialize(
                ApiResponse<ModelMapFixResult>.Fail(ex.Message)));
        }
        catch (InvalidOperationException ex)
        {
            HttpContext.Response.StatusCode = 409;
            await writer.WriteAsync(JsonSerializer.Serialize(
                ApiResponse<ModelMapFixResult>.Fail(ex.Message)));
        }
        catch (Exception ex)
        {
            HttpContext.Response.StatusCode = 500;
            await writer.WriteAsync(JsonSerializer.Serialize(
                ApiResponse<ModelMapFixResult>.Fail(ex.Message)));
        }
    }
}
