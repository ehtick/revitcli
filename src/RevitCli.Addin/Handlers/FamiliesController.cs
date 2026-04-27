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
}
