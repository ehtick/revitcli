using RevitCli.Addin.Services;

namespace RevitCli.Addin.Tests.Services;

public class StatusCapabilitiesTests
{
    private static readonly string[] CurrentAddinCapabilities =
    {
        "schedule",
        "schedule.list",
        "schedule.export",
        "schedule.create",
        "schedule.create.dry-run",
        "schedules",
        "schedules.ensure.dry-run",
        "schedules.batch-export",
        "views",
        "views.audit",
        "views.template-apply.dry-run",
        "views.clone-set.dry-run",
        "links",
        "links.audit",
        "links.repair",
        "links.repair.dry-run",
        "links.repair.apply",
        "model.map",
        "model.map.check",
        "model.map.fix",
        "model.map.fix.dry-run",
        "model.map.fix.apply",
        "snapshot",
        "snapshot.capture",
        "family",
        "family.list",
        "family.validate",
        "family.purge.dry-run",
        "family.purge.apply",
        "family.export.dry-run",
        "family.export.apply"
    };

    [Fact]
    public void BuildCapabilities_IncludesCurrentAddinCommandFamilies()
    {
        var capabilities = RealRevitOperations.BuildCapabilities(2026);

        AssertCurrentCapabilities(capabilities);
    }

    [Fact]
    public void BuildCapabilities_GatesPdfExportByRevitYear()
    {
        var revit2021 = RealRevitOperations.BuildCapabilities(2021);
        var revit2022 = RealRevitOperations.BuildCapabilities(2022);

        Assert.DoesNotContain("export.pdf", revit2021);
        Assert.Contains("export.pdf", revit2022);
    }

    [Fact]
    public async Task PlaceholderStatus_IncludesCurrentAddinCommandFamilies()
    {
        var status = await new PlaceholderRevitOperations().GetStatusAsync();

        AssertCurrentCapabilities(status.Capabilities);
    }

    private static void AssertCurrentCapabilities(IReadOnlyCollection<string> capabilities)
    {
        foreach (var capability in CurrentAddinCapabilities)
            Assert.Contains(capability, capabilities);

        Assert.DoesNotContain("schedules.compare", capabilities);
        Assert.DoesNotContain("schedules.create.apply", capabilities);
        Assert.DoesNotContain("views.template-apply", capabilities);
        Assert.DoesNotContain("views.clone-set", capabilities);
    }
}
