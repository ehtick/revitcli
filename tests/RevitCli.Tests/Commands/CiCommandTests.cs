using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using RevitCli.Commands;
using RevitCli.Diagnostics;
using Xunit;

namespace RevitCli.Tests.Commands;

/// <summary>
/// End-to-end coverage for <c>revitcli ci doctor</c>. We exercise the public
/// <see cref="CiCommand.ExecuteDoctorAsync(TextWriter, Func{string, string?}?)"/>
/// seam so tests do not have to spin up the full <c>System.CommandLine</c>
/// pipeline or mutate the real process environment.
/// </summary>
public class CiCommandTests
{
    private static Func<string, string?> Lookup(params (string Name, string? Value)[] entries)
    {
        var map = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var (name, value) in entries)
            map[name] = value;
        return name => map.TryGetValue(name, out var v) ? v : null;
    }

    [Fact]
    public async Task Doctor_NoCiDetected_PrintsReferenceTemplate_AndExitsZero()
    {
        var writer = new StringWriter();
        var exit = await CiCommand.ExecuteDoctorAsync(writer, Lookup());

        Assert.Equal(0, exit);
        var output = writer.ToString();
        Assert.Contains("RevitCli CI Doctor", output);
        Assert.Contains("Provider    : none", output);
        Assert.Contains("No CI environment detected", output);
        // Reference snippet is GitHub Actions
        Assert.Contains("GitHub Actions", output, StringComparison.OrdinalIgnoreCase);
        // Both snippet boundaries are present
        Assert.Contains("--- snippet (begin) ---", output);
        Assert.Contains("--- snippet (end) ---", output);
        // Gotchas section rendered
        Assert.Contains("Known gotchas:", output);
    }

    [Fact]
    public async Task Doctor_GitHubActions_PrintsProviderAndSnippet()
    {
        var writer = new StringWriter();
        var exit = await CiCommand.ExecuteDoctorAsync(
            writer,
            Lookup(("GITHUB_ACTIONS", "true"), ("RUNNER_OS", "Linux")));

        Assert.Equal(0, exit);
        var output = writer.ToString();
        Assert.Contains("Provider    : GitHub Actions (github-actions)", output);
        Assert.Contains("Runner OS   : Linux", output);
        Assert.Contains("Recommended workflow snippet for GitHub Actions:", output);
        Assert.Contains("revitcli-check.yml", output); // appears in the GH Actions snippet
        // CLI version line is always shown
        Assert.Contains("CLI version :", output);
    }

    [Fact]
    public async Task Doctor_Jenkins_PrintsJenkinsfileSnippet()
    {
        var writer = new StringWriter();
        var exit = await CiCommand.ExecuteDoctorAsync(
            writer,
            Lookup(("JENKINS_HOME", "/var/jenkins_home"), ("NODE_NAME", "linux-agent-1")));

        Assert.Equal(0, exit);
        var output = writer.ToString();
        Assert.Contains("Provider    : Jenkins (jenkins)", output);
        Assert.Contains("Runner OS   : linux-agent-1", output);
        Assert.Contains("Jenkinsfile", output);
    }

    [Fact]
    public async Task Doctor_AzureDevOps_PrintsAzurePipelinesSnippet()
    {
        var writer = new StringWriter();
        var exit = await CiCommand.ExecuteDoctorAsync(
            writer,
            Lookup(("TF_BUILD", "True"), ("Agent_OS", "Windows_NT")));

        Assert.Equal(0, exit);
        var output = writer.ToString();
        Assert.Contains("Provider    : Azure Pipelines (azure-devops)", output);
        Assert.Contains("azure-pipelines.yml", output);
    }

    [Fact]
    public async Task Doctor_GitLab_PrintsGitlabSnippet()
    {
        var writer = new StringWriter();
        var exit = await CiCommand.ExecuteDoctorAsync(
            writer,
            Lookup(("GITLAB_CI", "true")));

        Assert.Equal(0, exit);
        var output = writer.ToString();
        Assert.Contains("Provider    : GitLab CI (gitlab-ci)", output);
        Assert.Contains(".gitlab-ci.yml", output);
    }

    [Fact]
    public async Task Doctor_GenericCi_PrintsFallbackSnippet()
    {
        var writer = new StringWriter();
        var exit = await CiCommand.ExecuteDoctorAsync(
            writer,
            Lookup(("CI", "true")));

        Assert.Equal(0, exit);
        var output = writer.ToString();
        Assert.Contains("Generic CI", output);
        // Generic falls back to the GitHub reference template.
        Assert.Contains("revitcli-check.yml", output);
    }

    [Fact]
    public void Create_RegistersDoctorSubcommand()
    {
        var ci = CiCommand.Create();

        Assert.Equal("ci", ci.Name);
        Assert.Contains(ci.Subcommands, c => c.Name == "doctor");
    }
}
