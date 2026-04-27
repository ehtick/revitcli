using System;
using System.Collections.Generic;
using RevitCli.Diagnostics;
using Xunit;

namespace RevitCli.Tests.Diagnostics;

/// <summary>
/// Locks down the env-var → provider mapping that <c>revitcli ci doctor</c>
/// relies on. We test through the explicit <c>lookup</c> seam so the suite
/// never mutates the real process environment (matters in CI where these
/// vars are already set and would shadow each other).
/// </summary>
public class CiEnvironmentTests
{
    /// <summary>
    /// Build a lookup that returns the supplied values and <c>null</c> for
    /// everything else. Mirrors the production
    /// <see cref="Environment.GetEnvironmentVariable(string)"/> contract
    /// (unset vars return null).
    /// </summary>
    private static Func<string, string?> Lookup(params (string Name, string? Value)[] entries)
    {
        var map = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var (name, value) in entries)
            map[name] = value;
        return name => map.TryGetValue(name, out var v) ? v : null;
    }

    [Fact]
    public void Detect_GitHubActions_WhenGithubActionsTrue()
    {
        var detection = CiEnvironment.Detect(Lookup(("GITHUB_ACTIONS", "true"), ("RUNNER_OS", "Linux")));

        Assert.True(detection.IsCi);
        Assert.Equal(CiEnvironment.GitHubActions, detection.Provider);
        Assert.Equal("GitHub Actions", detection.DisplayName);
        Assert.Equal("Linux", detection.RunnerOs);
    }

    [Fact]
    public void Detect_GitLab_WhenGitlabCiTrue()
    {
        var detection = CiEnvironment.Detect(Lookup(("GITLAB_CI", "true")));

        Assert.True(detection.IsCi);
        Assert.Equal(CiEnvironment.GitLabCi, detection.Provider);
        Assert.Equal("GitLab CI", detection.DisplayName);
    }

    [Fact]
    public void Detect_Jenkins_WhenJenkinsHomeSet()
    {
        var detection = CiEnvironment.Detect(Lookup(("JENKINS_HOME", "/var/jenkins_home")));

        Assert.True(detection.IsCi);
        Assert.Equal(CiEnvironment.Jenkins, detection.Provider);
        Assert.Equal("Jenkins", detection.DisplayName);
    }

    [Fact]
    public void Detect_AzureDevOps_WhenTfBuildTrue()
    {
        var detection = CiEnvironment.Detect(Lookup(("TF_BUILD", "True"), ("Agent_OS", "Windows_NT")));

        Assert.True(detection.IsCi);
        Assert.Equal(CiEnvironment.AzureDevOps, detection.Provider);
        Assert.Equal("Azure Pipelines", detection.DisplayName);
        Assert.Equal("Windows_NT", detection.RunnerOs);
    }

    [Fact]
    public void Detect_Travis_WhenTravisTrue()
    {
        var detection = CiEnvironment.Detect(Lookup(("TRAVIS", "true"), ("TRAVIS_OS_NAME", "linux")));

        Assert.True(detection.IsCi);
        Assert.Equal(CiEnvironment.TravisCi, detection.Provider);
        Assert.Equal("Travis CI", detection.DisplayName);
    }

    [Fact]
    public void Detect_GenericCi_WhenOnlyCiFlagSet()
    {
        var detection = CiEnvironment.Detect(Lookup(("CI", "true")));

        Assert.True(detection.IsCi);
        Assert.Equal(CiEnvironment.Generic, detection.Provider);
    }

    [Fact]
    public void Detect_None_WhenNoSignalsPresent()
    {
        var detection = CiEnvironment.Detect(Lookup());

        Assert.False(detection.IsCi);
        Assert.Equal(CiEnvironment.None, detection.Provider);
        Assert.Null(detection.RunnerOs);
    }

    [Fact]
    public void Detect_GithubBeatsGitlab_WhenBothSet()
    {
        // Order contract: GitHub Actions wins over GitLab when both flags are
        // present (e.g. mirroring setups). Documented in DetectionOrder.
        var detection = CiEnvironment.Detect(Lookup(("GITHUB_ACTIONS", "true"), ("GITLAB_CI", "true")));

        Assert.Equal(CiEnvironment.GitHubActions, detection.Provider);
    }

    [Theory]
    [InlineData(CiEnvironment.GitHubActions, "github")]
    [InlineData(CiEnvironment.GitLabCi, "gitlab")]
    [InlineData(CiEnvironment.Jenkins, "Jenkinsfile")]
    [InlineData(CiEnvironment.AzureDevOps, "azure-pipelines")]
    [InlineData(CiEnvironment.TravisCi, ".travis.yml")]
    public void SuggestSnippet_IsNonEmpty_AndMentionsProvider(string provider, string mustContain)
    {
        var snippet = CiEnvironment.SuggestSnippet(provider);

        Assert.False(string.IsNullOrWhiteSpace(snippet));
        Assert.Contains(mustContain, snippet, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SuggestSnippet_UnknownProvider_FallsBackToGithub()
    {
        var snippet = CiEnvironment.SuggestSnippet("totally-made-up");

        Assert.Equal(CiEnvironment.GitHubActionsSnippet, snippet);
    }

    [Fact]
    public void Gotchas_AlwaysIncludeRevitWindowsConstraint()
    {
        foreach (var provider in CiEnvironment.DetectionOrder)
        {
            var notes = CiEnvironment.Gotchas(provider);

            Assert.NotEmpty(notes);
            // Common gotchas must appear for every provider.
            Assert.Contains(notes, n => n.Contains("Windows runner", StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public void ResolveRunnerOs_FromDetection_WhenProvided()
    {
        var detection = new CiEnvironment.Detection { RunnerOs = "macOS-13" };

        Assert.Equal("macOS-13", CiEnvironment.ResolveRunnerOs(detection));
    }

    [Fact]
    public void ResolveRunnerOs_FromHostFallback_WhenDetectionEmpty()
    {
        var detection = new CiEnvironment.Detection();

        var os = CiEnvironment.ResolveRunnerOs(detection);

        // We cannot assert a specific value cross-platform; just confirm the
        // fallback returns a non-empty platform label.
        Assert.False(string.IsNullOrWhiteSpace(os));
    }

    [Fact]
    public void DetectionOrder_StartsWithGithubActions()
    {
        // First-match-wins: lock the order GitHub → GitLab → Jenkins → Azure → Travis → Generic.
        Assert.Equal(CiEnvironment.GitHubActions, CiEnvironment.DetectionOrder[0]);
        Assert.Equal(CiEnvironment.Generic, CiEnvironment.DetectionOrder[^1]);
    }

    [Fact]
    public void GetCliVersion_ReturnsNonEmpty()
    {
        var version = CiEnvironment.GetCliVersion();

        Assert.False(string.IsNullOrWhiteSpace(version));
        Assert.DoesNotContain("+", version); // commit suffix stripped
    }
}
