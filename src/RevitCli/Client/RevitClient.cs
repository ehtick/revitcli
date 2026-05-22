using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using RevitCli.Config;
using RevitCli.Shared;

namespace RevitCli.Client;

public class RevitClient : IDisposable
{
    public const int DefaultPort = ServerInfo.DefaultPort;
    public const string DefaultBaseUrl = "http://127.0.0.1:17839";
    private static readonly TimeSpan DefaultRequestTimeout = TimeSpan.FromSeconds(30);

    private readonly HttpClient _http;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public bool Verbose { get; set; }

    private async Task<string> SendAndRead(HttpResponseMessage response, string method, string url)
    {
        var json = await response.Content.ReadAsStringAsync();
        if (Verbose)
        {
            Console.Error.WriteLine($"[HTTP] {method} {url} -> {(int)response.StatusCode}");
        }
        return json;
    }

    public RevitClient(HttpClient http)
    {
        _http = http;
    }

    public RevitClient(string baseUrl = DefaultBaseUrl, string token = "")
        : this(baseUrl, token, ResolveTimeout())
    {
    }

    public RevitClient(string baseUrl, string token, TimeSpan timeout)
    {
        _http = new HttpClient
        {
            BaseAddress = new System.Uri(baseUrl),
            Timeout = timeout
        };
        if (!string.IsNullOrEmpty(token))
            _http.DefaultRequestHeaders.Add("X-RevitCli-Token", token);
    }

    private static TimeSpan ResolveTimeout()
    {
        var raw = Environment.GetEnvironmentVariable("REVITCLI_HTTP_TIMEOUT_SECONDS");
        if (!string.IsNullOrWhiteSpace(raw)
            && int.TryParse(raw, System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out var seconds)
            && seconds > 0
            && seconds <= 3600)
        {
            return TimeSpan.FromSeconds(seconds);
        }
        return DefaultRequestTimeout;
    }

    public void Dispose()
    {
        _http.Dispose();
    }

    public static (string Url, string Token) DiscoverServerUrl(string configuredUrl)
    {
        try
        {
            var serverInfoPath = CliConfig.ServerInfoPath;
            if (File.Exists(serverInfoPath))
            {
                var json = File.ReadAllText(serverInfoPath);
                var info = JsonSerializer.Deserialize<ServerInfo>(json);
                if (info != null && info.Port >= 1024 && info.Port <= 65535)
                {
                    // Verify the process is still alive
                    try
                    {
                        using var proc = System.Diagnostics.Process.GetProcessById(info.Pid);
                        if (!proc.HasExited)
                            return ($"http://127.0.0.1:{info.Port}", info.Token ?? "");
                    }
                    catch (System.ArgumentException) { /* process not found */ }
                    catch (System.ComponentModel.Win32Exception) { /* access denied — treat as unavailable */ }
                }
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException
                                    or UnauthorizedAccessException or System.Security.SecurityException)
        {
            Console.Error.WriteLine($"[RevitCli] Could not read server.json: {ex.Message}");
        }
        return (configuredUrl, "");
    }

    // ── Generic request helpers ────────────────────────────────────

    /// <summary>
    /// Execute a GET request and deserialize the response.
    /// Handles connection failures and communication errors uniformly.
    /// </summary>
    private async Task<ApiResponse<T>> GetAsync<T>(string url)
    {
        try
        {
            var response = await _http.GetAsync(url);
            var json = await SendAndRead(response, "GET", url);
            return JsonSerializer.Deserialize<ApiResponse<T>>(json, JsonOptions)!;
        }
        catch (HttpRequestException ex)
        {
            if (Verbose) Console.Error.WriteLine($"[HTTP] Connection failed: {ex.Message}");
            return ApiResponse<T>.Fail("Revit is not running or plugin is not loaded.");
        }
        catch (Exception ex) when (ex is TaskCanceledException or JsonException)
        {
            return ApiResponse<T>.Fail($"Communication error: {ex.Message}");
        }
    }

    /// <summary>
    /// Execute a POST request with a JSON body and deserialize the response.
    /// Handles connection failures and communication errors uniformly.
    /// </summary>
    private async Task<ApiResponse<T>> PostAsync<T>(string url, object request)
    {
        try
        {
            var content = new StringContent(
                JsonSerializer.Serialize(request),
                Encoding.UTF8,
                "application/json");
            var response = await _http.PostAsync(url, content);
            var json = await SendAndRead(response, "POST", url);
            return JsonSerializer.Deserialize<ApiResponse<T>>(json, JsonOptions)!;
        }
        catch (HttpRequestException ex)
        {
            if (Verbose) Console.Error.WriteLine($"[HTTP] Connection failed: {ex.Message}");
            return ApiResponse<T>.Fail("Revit is not running or plugin is not loaded.");
        }
        catch (Exception ex) when (ex is TaskCanceledException or JsonException)
        {
            return ApiResponse<T>.Fail($"Communication error: {ex.Message}");
        }
    }

    // ── Public API methods ─────────────────────────────────────────

    public Task<ApiResponse<StatusInfo>> GetStatusAsync()
        => GetAsync<StatusInfo>("/api/status");

    public Task<ApiResponse<ElementInfo[]>> QueryElementsAsync(string? category, string? filter)
    {
        var parts = new List<string>();
        if (category != null) parts.Add($"category={System.Uri.EscapeDataString(category)}");
        if (filter != null) parts.Add($"filter={System.Uri.EscapeDataString(filter)}");
        var url = $"/api/elements?{string.Join("&", parts)}";
        return GetAsync<ElementInfo[]>(url);
    }

    public Task<ApiResponse<ElementInfo>> QueryElementByIdAsync(long id)
        => GetAsync<ElementInfo>($"/api/elements/{id}");

    public Task<ApiResponse<ExportProgress>> ExportAsync(ExportRequest request)
        => PostAsync<ExportProgress>("/api/export", request);

    public Task<ApiResponse<ExportProgress>> GetExportProgressAsync(string taskId)
        => GetAsync<ExportProgress>($"/api/tasks/{taskId}");

    public Task<ApiResponse<SetResult>> SetParameterAsync(SetRequest request)
        => PostAsync<SetResult>("/api/elements/set", request);

    public Task<ApiResponse<AuditResult>> AuditAsync(AuditRequest request)
        => PostAsync<AuditResult>("/api/audit", request);

    public Task<ApiResponse<ScheduleInfo[]>> ListSchedulesAsync()
        => GetAsync<ScheduleInfo[]>("/api/schedules");

    public Task<ApiResponse<ScheduleData>> ExportScheduleAsync(ScheduleExportRequest request)
        => PostAsync<ScheduleData>("/api/schedules/export", request);

    public Task<ApiResponse<ScheduleCreateResult>> CreateScheduleAsync(ScheduleCreateRequest request)
        => PostAsync<ScheduleCreateResult>("/api/schedules/create", request);

    public Task<ApiResponse<ViewInfo[]>> ListViewsAsync()
        => GetAsync<ViewInfo[]>("/api/views");

    public Task<ApiResponse<LinkInfo[]>> ListLinksAsync()
        => GetAsync<LinkInfo[]>("/api/links");

    public Task<ApiResponse<LinkRepairResult>> ApplyLinkRepairAsync(LinkRepairRequest request)
        => PostAsync<LinkRepairResult>("/api/links/repair", request);

    public Task<ApiResponse<ModelMapElementInfo[]>> ListModelMapElementsAsync()
        => GetAsync<ModelMapElementInfo[]>("/api/model/map");

    public Task<ApiResponse<ModelMapFixResult>> ApplyModelMapFixAsync(ModelMapFixRequest request)
        => PostAsync<ModelMapFixResult>("/api/model/map/fix", request);

    public async Task<ApiResponse<FamilyInfo[]>> ListFamiliesAsync(FamilyListRequest request)
    {
        try
        {
            var parts = new List<string>();
            if (request.IncludeUnplaced)
                parts.Add("unused=true");
            if (!string.IsNullOrWhiteSpace(request.Category))
                parts.Add($"category={System.Uri.EscapeDataString(request.Category!)}");
            var url = parts.Count == 0
                ? "/api/families"
                : $"/api/families?{string.Join("&", parts)}";

            var response = await _http.GetAsync(url);
            var json = await SendAndRead(response, "GET", url);

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return ApiResponse<FamilyInfo[]>.Fail(
                    "/api/families endpoint not found — this command requires the v1.8 add-in. " +
                    "Update the Revit add-in to use `family ls`.");
            }

            return JsonSerializer.Deserialize<ApiResponse<FamilyInfo[]>>(json, JsonOptions)!;
        }
        catch (HttpRequestException ex)
        {
            if (Verbose) Console.Error.WriteLine($"[HTTP] Connection failed: {ex.Message}");
            return ApiResponse<FamilyInfo[]>.Fail("Revit is not running or plugin is not loaded.");
        }
        catch (Exception ex) when (ex is TaskCanceledException or JsonException)
        {
            return ApiResponse<FamilyInfo[]>.Fail($"Communication error: {ex.Message}");
        }
    }

    /// <summary>
    /// Drop the listed family ids from the active document. The CLI is
    /// responsible for selecting the ids (--unused / --category / --keep);
    /// the addin runs the deletion in a single Revit transaction.
    /// </summary>
    public Task<ApiResponse<FamilyPurgeResult>> PurgeFamiliesAsync(FamilyPurgeRequest request)
        => PostAsync<FamilyPurgeResult>("/api/families/purge", request);

    /// <summary>
    /// Save the listed families as standalone .rfa files under
    /// <see cref="FamilyExportRequest.OutputDir"/>. In-place families
    /// are reported as failures (Revit can't <c>EditFamily</c> on them).
    /// </summary>
    public Task<ApiResponse<FamilyExportResult>> ExportFamiliesAsync(FamilyExportRequest request)
        => PostAsync<FamilyExportResult>("/api/families/export", request);

    public async Task<ApiResponse<ModelSnapshot>> CaptureSnapshotAsync(SnapshotRequest request)
    {
        try
        {
            var url = "/api/snapshot";
            var content = new StringContent(
                JsonSerializer.Serialize(request),
                Encoding.UTF8,
                "application/json");
            var response = await _http.PostAsync(url, content);
            var json = await SendAndRead(response, "POST", url);
            return JsonSerializer.Deserialize<ApiResponse<ModelSnapshot>>(json, JsonOptions)!;
        }
        catch (HttpRequestException ex)
        {
            if (Verbose) Console.Error.WriteLine($"[HTTP] Connection failed: {ex.Message}");
            return ApiResponse<ModelSnapshot>.Fail("Revit is not running or plugin is not loaded.");
        }
        catch (Exception ex) when (ex is TaskCanceledException or JsonException)
        {
            return ApiResponse<ModelSnapshot>.Fail($"Communication error: {ex.Message}");
        }
    }
}
