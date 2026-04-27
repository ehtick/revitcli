using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using LibGit2Sharp;
using RevitCli.Profile;
using Xunit;

namespace RevitCli.Tests.Profile;

/// <summary>
/// Drives <see cref="ProfileInstaller"/> against local file:// repositories so
/// the test suite never touches the network. Each test owns a temp dir that
/// holds both the source repo and the install target so cleanup is one
/// recursive delete in <see cref="Dispose"/>.
/// </summary>
public class ProfileInstallerTests : IDisposable
{
    private readonly string _root;
    private readonly string _sourceRepoPath;
    private readonly string _sourceRepoUrl;

    public ProfileInstallerTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "revitcli-installer-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);

        _sourceRepoPath = Path.Combine(_root, "source");
        Directory.CreateDirectory(_sourceRepoPath);

        // file:// URL form so LibGit2Sharp routes through its standard transport
        // exactly as it would for a real https:// remote — keeps the production
        // path warm in tests without requiring network.
        _sourceRepoUrl = new Uri(_sourceRepoPath).AbsoluteUri;

        SeedSourceRepo();
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) DeleteDirectoryRobust(_root); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    /// <summary>
    /// Build a minimal git repo with two commits on main, a v1.0 tag on the
    /// first commit, and a feature branch off of main. Each commit holds a
    /// profile YAML so installer tests can verify both file content and ref
    /// selection. The seed code force-renames HEAD to <c>main</c> so the test
    /// behaviour does not depend on the host's <c>init.defaultBranch</c>.
    /// </summary>
    private void SeedSourceRepo()
    {
        Repository.Init(_sourceRepoPath);

        using var repo = new Repository(_sourceRepoPath);
        var sig = new Signature("Test", "test@example.com", DateTimeOffset.UtcNow);

        File.WriteAllText(Path.Combine(_sourceRepoPath, "main.yml"),
            "version: 1\nchecks:\n  default:\n    failOn: error\n");
        Directory.CreateDirectory(Path.Combine(_sourceRepoPath, "team"));
        File.WriteAllText(Path.Combine(_sourceRepoPath, "team", "team.yml"),
            "version: 1\nchecks:\n  team:\n    failOn: warning\n");
        LibGit2Sharp.Commands.Stage(repo, "*");
        var firstCommit = repo.Commit("seed: initial profile bundle", sig, sig);

        // Force HEAD onto a deterministically-named 'main' branch regardless
        // of the libgit2 default. UpdateTarget edits .git/HEAD without
        // touching the working tree.
        repo.CreateBranch("main", firstCommit);
        repo.Refs.UpdateTarget("HEAD", "refs/heads/main");

        // Tag v1.0 against the first commit so we can exercise the tag branch.
        repo.ApplyTag("v1.0");

        // Second commit on main so HEAD-vs-tag selection is observable.
        File.WriteAllText(Path.Combine(_sourceRepoPath, "main.yml"),
            "version: 1\nchecks:\n  default:\n    failOn: warning\n");
        LibGit2Sharp.Commands.Stage(repo, "*");
        var secondCommit = repo.Commit("update: tighten failOn", sig, sig);

        // Feature branch off main's second commit, with a third commit on
        // top. We avoid Commands.Checkout entirely — it churns the working
        // tree and every code path can be exercised by ref pointer math.
        repo.CreateBranch("feature", secondCommit);
        File.WriteAllText(Path.Combine(_sourceRepoPath, "main.yml"),
            "version: 1\nchecks:\n  default:\n    failOn: error\n  experimental:\n    failOn: error\n");
        LibGit2Sharp.Commands.Stage(repo, "*");
        repo.Refs.UpdateTarget("HEAD", "refs/heads/feature");
        repo.Commit("feature: add experimental check", sig, sig);

        // Leave HEAD pointing at main and the working tree on main's second
        // commit content so default-ref clones get failOn: warning.
        repo.Refs.UpdateTarget("HEAD", "refs/heads/main");
        File.WriteAllText(Path.Combine(_sourceRepoPath, "main.yml"),
            "version: 1\nchecks:\n  default:\n    failOn: warning\n");
        LibGit2Sharp.Commands.Stage(repo, "*");
    }

    [Fact]
    public async Task Install_LocalFileUrl_CopiesFullTreeToTargetDir()
    {
        var target = Path.Combine(_root, "installs", "default");

        var result = await ProfileInstaller.InstallAsync(
            _sourceRepoUrl, refSpec: null, subPath: null, targetDir: target);

        Assert.Equal(Path.GetFullPath(target), result.TargetDir);
        Assert.True(File.Exists(Path.Combine(target, "main.yml")));
        Assert.True(File.Exists(Path.Combine(target, "team", "team.yml")));
        // Default ref-less clone leaves us on main's tip — second commit,
        // where failOn is "warning".
        var contents = File.ReadAllText(Path.Combine(target, "main.yml"));
        Assert.Contains("failOn: warning", contents);
    }

    [Fact]
    public async Task Install_RefIsBranch_ChecksOutBranchTip()
    {
        var target = Path.Combine(_root, "installs", "feature");

        var result = await ProfileInstaller.InstallAsync(
            _sourceRepoUrl, refSpec: "feature", subPath: null, targetDir: target);

        var contents = File.ReadAllText(Path.Combine(target, "main.yml"));
        Assert.Contains("experimental", contents);
        Assert.NotNull(result.CheckedOutRef);
    }

    [Fact]
    public async Task Install_RefIsTag_ChecksOutTaggedCommit()
    {
        var target = Path.Combine(_root, "installs", "v1.0");

        await ProfileInstaller.InstallAsync(
            _sourceRepoUrl, refSpec: "v1.0", subPath: null, targetDir: target);

        // v1.0 points at the first commit, where failOn was still "error".
        var contents = File.ReadAllText(Path.Combine(target, "main.yml"));
        Assert.Contains("failOn: error", contents);
        Assert.DoesNotContain("experimental", contents);
    }

    [Fact]
    public async Task Install_SubPath_CopiesOnlyRequestedSubdirectory()
    {
        var target = Path.Combine(_root, "installs", "team-only");

        await ProfileInstaller.InstallAsync(
            _sourceRepoUrl, refSpec: null, subPath: "team", targetDir: target);

        Assert.True(File.Exists(Path.Combine(target, "team.yml")));
        // main.yml lives outside the requested subpath — it must NOT be copied.
        Assert.False(File.Exists(Path.Combine(target, "main.yml")));
    }

    [Fact]
    public async Task Install_SubPathIsSingleFile_CopiesJustThatFile()
    {
        var target = Path.Combine(_root, "installs", "main-file-only");

        await ProfileInstaller.InstallAsync(
            _sourceRepoUrl, refSpec: null, subPath: "main.yml", targetDir: target);

        Assert.True(File.Exists(Path.Combine(target, "main.yml")));
        Assert.False(Directory.Exists(Path.Combine(target, "team")));
    }

    [Fact]
    public async Task Install_TargetExists_WithoutForce_Throws()
    {
        var target = Path.Combine(_root, "installs", "occupied");
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "stale.txt"), "left over");

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await ProfileInstaller.InstallAsync(
                _sourceRepoUrl, refSpec: null, subPath: null, targetDir: target));

        // The pre-existing file must still be there — install must not have
        // started clobbering before discovering the conflict.
        Assert.True(File.Exists(Path.Combine(target, "stale.txt")));
    }

    [Fact]
    public async Task Install_TargetExists_WithForce_OverwritesContents()
    {
        var target = Path.Combine(_root, "installs", "force-overwrite");
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "stale.txt"), "left over");

        await ProfileInstaller.InstallAsync(
            _sourceRepoUrl, refSpec: null, subPath: null, targetDir: target, force: true);

        // stale.txt is gone, replaced by the freshly cloned repo contents.
        Assert.False(File.Exists(Path.Combine(target, "stale.txt")));
        Assert.True(File.Exists(Path.Combine(target, "main.yml")));
    }

    [Fact]
    public async Task Install_RefDoesNotExist_Throws()
    {
        var target = Path.Combine(_root, "installs", "no-such-ref");

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await ProfileInstaller.InstallAsync(
                _sourceRepoUrl, refSpec: "does-not-exist", subPath: null, targetDir: target));
    }

    [Fact]
    public async Task Install_SubPathNotInRepo_Throws()
    {
        var target = Path.Combine(_root, "installs", "missing-subpath");

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await ProfileInstaller.InstallAsync(
                _sourceRepoUrl, refSpec: null, subPath: "no/such/dir", targetDir: target));
    }

    [Theory]
    [InlineData("https://github.com/org/standards.git", "standards")]
    [InlineData("https://github.com/org/standards", "standards")]
    [InlineData("git@github.com:org/standards.git", "standards")]
    [InlineData("file:///tmp/repos/team-rules", "team-rules")]
    [InlineData("https://example.com/path/?ref=main", "path")]
    public void DeriveProfileName_StripsGitSuffixAndQuery(string url, string expected)
    {
        Assert.Equal(expected, ProfileInstaller.DeriveProfileName(url));
    }

    private static void DeleteDirectoryRobust(string path)
    {
        if (!Directory.Exists(path)) return;
        foreach (var file in Directory.GetFiles(path, "*", SearchOption.AllDirectories))
        {
            try
            {
                var attrs = File.GetAttributes(file);
                if ((attrs & FileAttributes.ReadOnly) != 0)
                    File.SetAttributes(file, attrs & ~FileAttributes.ReadOnly);
            }
            catch { /* best effort */ }
        }
        Directory.Delete(path, true);
    }
}
