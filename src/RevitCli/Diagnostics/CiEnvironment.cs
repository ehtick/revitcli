using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;

namespace RevitCli.Diagnostics;

/// <summary>
/// Pure-detection helper used by <c>revitcli ci doctor</c> to identify the
/// surrounding CI provider and produce a ready-to-paste workflow snippet.
///
/// Detection is deterministic and based on the well-known environment variables
/// each provider sets. The first match wins (the order below mirrors the order
/// documented in <c>docs/roadmap-2026q2-q3.md</c> §4 and is exposed via
/// <see cref="DetectionOrder"/> for tests).
/// </summary>
public static class CiEnvironment
{
    public const string GitHubActions = "github-actions";
    public const string GitLabCi = "gitlab-ci";
    public const string Jenkins = "jenkins";
    public const string AzureDevOps = "azure-devops";
    public const string TravisCi = "travis-ci";
    public const string Generic = "generic-ci";
    public const string None = "none";

    /// <summary>
    /// Order in which providers are evaluated. Exposed for the test suite so
    /// the ordering contract can be locked in.
    /// </summary>
    public static IReadOnlyList<string> DetectionOrder { get; } = new[]
    {
        GitHubActions,
        GitLabCi,
        Jenkins,
        AzureDevOps,
        TravisCi,
        Generic,
    };

    /// <summary>
    /// Snapshot of the detected environment.
    /// </summary>
    public sealed class Detection
    {
        public string Provider { get; init; } = None;
        public string DisplayName { get; init; } = "No CI environment detected";
        public string? RunnerOs { get; init; }
        public bool IsCi { get; init; }
    }

    /// <summary>
    /// Detect the active CI provider from process environment variables.
    /// Lookup is delegated to <paramref name="lookup"/> so tests can supply a
    /// scoped mapping without mutating the real process environment.
    /// </summary>
    public static Detection Detect(Func<string, string?>? lookup = null)
    {
        lookup ??= Environment.GetEnvironmentVariable;

        // Order matters: most specific provider markers first. Each branch
        // documents the canonical env var the provider sets per its docs.
        if (string.Equals(lookup("GITHUB_ACTIONS"), "true", StringComparison.OrdinalIgnoreCase))
        {
            return new Detection
            {
                Provider = GitHubActions,
                DisplayName = "GitHub Actions",
                RunnerOs = lookup("RUNNER_OS"),
                IsCi = true,
            };
        }

        if (string.Equals(lookup("GITLAB_CI"), "true", StringComparison.OrdinalIgnoreCase))
        {
            return new Detection
            {
                Provider = GitLabCi,
                DisplayName = "GitLab CI",
                RunnerOs = lookup("CI_RUNNER_DESCRIPTION"),
                IsCi = true,
            };
        }

        if (!string.IsNullOrEmpty(lookup("JENKINS_HOME")))
        {
            return new Detection
            {
                Provider = Jenkins,
                DisplayName = "Jenkins",
                RunnerOs = lookup("NODE_NAME"),
                IsCi = true,
            };
        }

        if (string.Equals(lookup("TF_BUILD"), "True", StringComparison.OrdinalIgnoreCase))
        {
            return new Detection
            {
                Provider = AzureDevOps,
                DisplayName = "Azure Pipelines",
                RunnerOs = lookup("Agent_OS"),
                IsCi = true,
            };
        }

        if (string.Equals(lookup("TRAVIS"), "true", StringComparison.OrdinalIgnoreCase))
        {
            return new Detection
            {
                Provider = TravisCi,
                DisplayName = "Travis CI",
                RunnerOs = lookup("TRAVIS_OS_NAME"),
                IsCi = true,
            };
        }

        // Generic fallback: most CI systems set the bare `CI=true` flag even
        // when we don't recognise the specific provider.
        if (string.Equals(lookup("CI"), "true", StringComparison.OrdinalIgnoreCase))
        {
            return new Detection
            {
                Provider = Generic,
                DisplayName = "Generic CI (CI=true detected)",
                RunnerOs = null,
                IsCi = true,
            };
        }

        return new Detection
        {
            Provider = None,
            DisplayName = "No CI environment detected",
            RunnerOs = null,
            IsCi = false,
        };
    }

    /// <summary>
    /// Resolve the runner OS string to print. Falls back to the host
    /// <see cref="Environment.OSVersion"/> when the provider does not expose a
    /// dedicated runner-os variable.
    /// </summary>
    public static string ResolveRunnerOs(Detection detection)
    {
        if (detection != null && !string.IsNullOrWhiteSpace(detection.RunnerOs))
            return detection.RunnerOs!;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return "Windows";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) return "Linux";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) return "macOS";
        return Environment.OSVersion.Platform.ToString();
    }

    /// <summary>
    /// Pick the workflow snippet to display for a detected provider. Unknown
    /// providers fall back to the GitHub Actions reference template.
    /// </summary>
    public static string SuggestSnippet(string provider)
    {
        return provider switch
        {
            GitHubActions => GitHubActionsSnippet,
            GitLabCi => GitLabSnippet,
            Jenkins => JenkinsSnippet,
            AzureDevOps => AzurePipelinesSnippet,
            TravisCi => TravisSnippet,
            _ => GitHubActionsSnippet,
        };
    }

    /// <summary>
    /// Provider-specific notes (e.g. Windows-only constraints). Empty string
    /// when there is nothing actionable to call out.
    /// </summary>
    public static IReadOnlyList<string> Gotchas(string provider)
    {
        var common = new[]
        {
            "Revit can only be driven on a self-hosted Windows runner; SARIF lint runs everywhere.",
            "Pin the `revitcli` version explicitly (`--version`) in production pipelines for reproducible runs.",
        };

        return provider switch
        {
            GitHubActions => Concat(common, new[]
            {
                "Use `permissions: { security-events: write }` so `upload-sarif` can attach to Code Scanning.",
            }),
            GitLabCi => Concat(common, new[]
            {
                "GitLab parses SARIF via the `sast` report; emit `--output sarif` and surface it as a `report:sast:` artifact.",
            }),
            Jenkins => Concat(common, new[]
            {
                "Use the Warnings Next Generation plugin's SARIF parser to surface issues on the build summary.",
            }),
            AzureDevOps => Concat(common, new[]
            {
                "Pair with the SARIF SAST Scans Tab extension to render Code Scanning results in Azure DevOps.",
            }),
            TravisCi => Concat(common, new[]
            {
                "Travis lacks first-class SARIF support; upload the report as an artifact for downstream consumers.",
            }),
            _ => common,
        };
    }

    /// <summary>
    /// Version reported by the running CLI assembly — useful diagnostic when
    /// debugging which build a runner picked up.
    /// </summary>
    public static string GetCliVersion()
    {
        var asm = typeof(CiEnvironment).Assembly;
        var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(info))
        {
            // Strip the +commit suffix MSBuild appends so the doctor output is tidy.
            var plus = info.IndexOf('+');
            return plus >= 0 ? info.Substring(0, plus) : info;
        }
        return asm.GetName().Version?.ToString() ?? "unknown";
    }

    private static string[] Concat(IReadOnlyList<string> a, IReadOnlyList<string> b)
    {
        var merged = new string[a.Count + b.Count];
        for (int i = 0; i < a.Count; i++) merged[i] = a[i];
        for (int i = 0; i < b.Count; i++) merged[a.Count + i] = b[i];
        return merged;
    }

    // ------------------------------------------------------------------
    // Workflow snippets
    // ------------------------------------------------------------------
    //
    // These templates intentionally use the project's published GitHub Action
    // path so newcomers can copy them verbatim. Keep them lean — `ci doctor`
    // dumps the snippet to stdout and trimming saves screen space.

    public const string GitHubActionsSnippet = @"# .github/workflows/revitcli-check.yml
name: RevitCli Check
on:
  pull_request:
permissions:
  contents: read
  security-events: write
jobs:
  check:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '8.0.x'
      - uses: ./.github/actions/revitcli-check
        with:
          revitcli-version: latest
";

    public const string GitLabSnippet = @"# .gitlab-ci.yml
revitcli-check:
  image: mcr.microsoft.com/dotnet/sdk:8.0
  script:
    - dotnet tool install -g RevitCli
    - export PATH=""$PATH:$HOME/.dotnet/tools""
    - revitcli check --output sarif --report revitcli.sarif
  artifacts:
    when: always
    reports:
      sast: revitcli.sarif
";

    public const string JenkinsSnippet = @"// Jenkinsfile (Declarative)
pipeline {
  agent any
  stages {
    stage('RevitCli Check') {
      steps {
        sh 'dotnet tool install -g RevitCli'
        sh 'export PATH=""$PATH:$HOME/.dotnet/tools"" && revitcli check --output sarif --report revitcli.sarif'
      }
      post {
        always {
          archiveArtifacts artifacts: 'revitcli.sarif', fingerprint: true
        }
      }
    }
  }
}
";

    public const string AzurePipelinesSnippet = @"# azure-pipelines.yml
trigger: [main]
pool:
  vmImage: ubuntu-latest
steps:
  - task: UseDotNet@2
    inputs:
      version: '8.0.x'
  - script: dotnet tool install -g RevitCli
    displayName: Install RevitCli
  - script: revitcli check --output sarif --report $(Build.ArtifactStagingDirectory)/revitcli.sarif
    displayName: RevitCli Check
  - task: PublishBuildArtifacts@1
    inputs:
      pathToPublish: '$(Build.ArtifactStagingDirectory)/revitcli.sarif'
      artifactName: 'revitcli-sarif'
";

    public const string TravisSnippet = @"# .travis.yml
language: csharp
dotnet: 8.0
script:
  - dotnet tool install -g RevitCli
  - export PATH=""$PATH:$HOME/.dotnet/tools""
  - revitcli check --output sarif --report revitcli.sarif
";
}
