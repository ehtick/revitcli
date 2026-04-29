using System.Threading.Tasks;

namespace RevitCli.Shared;

public interface IRevitOperations
{
    Task<StatusInfo> GetStatusAsync();
    Task<ElementInfo[]> QueryElementsAsync(string? category, string? filter);
    Task<ElementInfo?> GetElementByIdAsync(long id);
    Task<ExportProgress> ExportAsync(ExportRequest request);
    Task<ExportProgress> GetExportProgressAsync(string taskId);
    Task<SetResult> SetParametersAsync(SetRequest request);
    Task<AuditResult> RunAuditAsync(AuditRequest request);
    Task<ScheduleInfo[]> ListSchedulesAsync();
    Task<ScheduleData> ExportScheduleAsync(ScheduleExportRequest request);
    Task<ScheduleCreateResult> CreateScheduleAsync(ScheduleCreateRequest request);
    Task<ModelSnapshot> CaptureSnapshotAsync(SnapshotRequest request);
    Task<FamilyInfo[]> ListFamiliesAsync(FamilyListRequest request);
    Task<FamilyPurgeResult> PurgeFamiliesAsync(FamilyPurgeRequest request);
    Task<FamilyExportResult> ExportFamiliesAsync(FamilyExportRequest request);
}
