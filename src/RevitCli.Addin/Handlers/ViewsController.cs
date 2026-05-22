using System;
using System.Text.Json;
using System.Threading.Tasks;
using EmbedIO;
using EmbedIO.Routing;
using EmbedIO.WebApi;
using RevitCli.Shared;

namespace RevitCli.Addin.Handlers;

public class ViewsController : WebApiController
{
    private readonly IRevitOperations _operations;

    public ViewsController(IRevitOperations operations)
    {
        _operations = operations;
    }

    [Route(HttpVerbs.Get, "/views")]
    public async Task ListViews()
    {
        HttpContext.Response.ContentType = "application/json";
        using var writer = HttpContext.OpenResponseText();

        try
        {
            var views = await _operations.ListViewsAsync();
            await writer.WriteAsync(JsonSerializer.Serialize(
                ApiResponse<ViewInfo[]>.Ok(views)));
        }
        catch (InvalidOperationException ex)
        {
            HttpContext.Response.StatusCode = 400;
            await writer.WriteAsync(JsonSerializer.Serialize(
                ApiResponse<ViewInfo[]>.Fail(ex.Message)));
        }
        catch (Exception ex)
        {
            HttpContext.Response.StatusCode = 500;
            await writer.WriteAsync(JsonSerializer.Serialize(
                ApiResponse<ViewInfo[]>.Fail(ex.Message)));
        }
    }
}
