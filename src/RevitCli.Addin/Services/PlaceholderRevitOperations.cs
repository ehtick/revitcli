using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using RevitCli.Shared;

namespace RevitCli.Addin.Services;

public class PlaceholderRevitOperations : IRevitOperations
{
    private static bool ShouldReturnRequiredParameterPlaceholder(AuditRequest request)
    {
        if (request.RequiredParameters?.Count > 0)
            return true;

        return request.Rules?.Exists(rule => string.Equals(rule, "required-parameter", StringComparison.OrdinalIgnoreCase)) == true;
    }

    public Task<StatusInfo> GetStatusAsync()
    {
        return Task.FromResult(new StatusInfo
        {
            RevitVersion = "2025",
            RevitYear = 2025,
            // Placeholder protocol tests must not masquerade as the production Add-in.
            AddinVersion = "0.0.0",
            DocumentName = "Placeholder.rvt",
            Capabilities = new List<string>
            {
                "status", "query", "query.filter", "query.id",
                "set", "set.dry-run", "audit",
                "export.dwg", "export.pdf", "export.ifc"
            }
        });
    }

    public Task<ElementInfo[]> QueryElementsAsync(string? category, string? filter)
    {
        return Task.FromResult(Array.Empty<ElementInfo>());
    }

    public Task<ElementInfo?> GetElementByIdAsync(long id)
    {
        return Task.FromResult<ElementInfo?>(new ElementInfo { Id = id, Name = $"Element {id}" });
    }

    public Task<ExportProgress> ExportAsync(ExportRequest request)
    {
        return Task.FromResult(new ExportProgress
        {
            TaskId = Guid.NewGuid().ToString("N").Substring(0, 8),
            Status = "completed",
            Progress = 100
        });
    }

    public Task<ExportProgress> GetExportProgressAsync(string taskId)
    {
        return Task.FromResult(new ExportProgress
        {
            TaskId = taskId,
            Status = "completed",
            Progress = 100
        });
    }

    public Task<SetResult> SetParametersAsync(SetRequest request)
    {
        return Task.FromResult(new SetResult { Affected = 0 });
    }

    public Task<AuditResult> RunAuditAsync(AuditRequest request)
    {
        if (!ShouldReturnRequiredParameterPlaceholder(request))
        {
            return Task.FromResult(new AuditResult
            {
                Passed = 5,
                Failed = 0,
                Issues = new List<AuditIssue>()
            });
        }

        return Task.FromResult(new AuditResult
        {
            Passed = 4,
            Failed = 1,
            Issues = new List<AuditIssue>
            {
                new AuditIssue
                {
                    Rule = "required-parameter",
                    Severity = "warning",
                    Message = "Placeholder structured audit issue.",
                    Category = "doors",
                    Parameter = "Mark",
                    Target = "doors",
                    CurrentValue = "",
                    ExpectedValue = "D-100",
                    Source = "structured"
                }
            }
        });
    }

    public Task<ScheduleInfo[]> ListSchedulesAsync()
    {
        return Task.FromResult(new[]
        {
            new ScheduleInfo { Id = 1001, Name = "Door Schedule", Category = "Doors", FieldCount = 5, RowCount = 12 },
            new ScheduleInfo { Id = 1002, Name = "Room Schedule", Category = "Rooms", FieldCount = 4, RowCount = 8 }
        });
    }

    public Task<ScheduleData> ExportScheduleAsync(ScheduleExportRequest request)
    {
        return Task.FromResult(new ScheduleData
        {
            Columns = new List<string> { "Name", "Level", "Type" },
            Rows = new List<Dictionary<string, string>>(),
            TotalRows = 0
        });
    }

    public Task<ScheduleCreateResult> CreateScheduleAsync(ScheduleCreateRequest request)
    {
        return Task.FromResult(new ScheduleCreateResult
        {
            ViewId = 2001,
            Name = request.Name,
            FieldCount = request.Fields?.Count ?? 0,
            RowCount = 0,
            PlacedOnSheet = null
        });
    }

    public Task<FamilyInfo[]> ListFamiliesAsync(FamilyListRequest request)
    {
        var families = new[]
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
                IsPlaced = true
            },
            new FamilyInfo
            {
                Id = 5003,
                Name = "Placeholder InPlace Wall",
                Category = "Walls",
                IsInPlace = true,
                IsLoadable = false,
                FilePath = null,
                IsPlaced = false
            }
        };

        IEnumerable<FamilyInfo> filtered = families;
        if (!string.IsNullOrWhiteSpace(request?.Category))
        {
            filtered = filtered.Where(f =>
                string.Equals(f.Category, request!.Category, StringComparison.OrdinalIgnoreCase));
        }
        if (request?.IncludeUnplaced == true)
        {
            filtered = filtered.Where(f => !f.IsPlaced);
        }

        return Task.FromResult(filtered.ToArray());
    }

    public Task<ModelSnapshot> CaptureSnapshotAsync(SnapshotRequest request)
    {
        var snapshot = new ModelSnapshot
        {
            SchemaVersion = 1,
            TakenAt = "2026-04-23T00:00:00Z",
            Revit = new SnapshotRevit
            {
                Version = "2025",
                Document = "Placeholder.rvt",
                DocumentPath = "/tmp/Placeholder.rvt"
            },
            Model = new SnapshotModel { SizeBytes = 0, FileHash = "" },
            Categories = new Dictionary<string, List<SnapshotElement>>
            {
                ["walls"] = new()
                {
                    new SnapshotElement
                    {
                        Id = 1001, Name = "Placeholder wall", TypeName = "W1",
                        Parameters = new() { ["Mark"] = "W1" },
                        Hash = "placeholder111111"
                    }
                }
            },
            Sheets = new()
            {
                new SnapshotSheet
                {
                    Number = "A-01", Name = "Placeholder sheet",
                    ViewId = 2001, PlacedViewIds = new() { 3001 },
                    Parameters = new(),
                    MetaHash = "placeholder_sheet",
                    ContentHash = ""
                }
            },
            Schedules = new()
            {
                new SnapshotSchedule
                {
                    Id = 4001, Name = "Placeholder schedule",
                    Category = "walls", RowCount = 1, Hash = "placeholder_sch"
                }
            },
            Summary = new SnapshotSummary
            {
                ElementCounts = new() { ["walls"] = 1 },
                SheetCount = 1, ScheduleCount = 1
            }
        };
        return Task.FromResult(snapshot);
    }
}
