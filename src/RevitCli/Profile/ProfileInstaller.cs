using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using LibGit2Sharp;

namespace RevitCli.Profile;

/// <summary>
/// Implements <c>revitcli profile install &lt;git-url&gt;</c> — shallow-clones a
/// remote profile bundle into a local directory so users can <c>extends:</c> it.
///
/// Tests drive this against local file://-style repos created by
/// <see cref="LibGit2Sharp"/>, so no network round-trips are exercised in CI.
/// In production the same code path also accepts ssh:// and https:// URLs.
/// </summary>
public static class ProfileInstaller
{
    /// <summary>
    /// Result of a successful install. Carries the on-disk path so the CLI can
    /// echo back the exact <c>extends:</c> string the user should paste into
    /// their <c>.revitcli.yml</c>.
    /// </summary>
    public sealed record InstallResult(string TargetDir, string? CheckedOutRef, string SourceUrl);

    /// <summary>
    /// Shallow-clone <paramref name="gitUrl"/> at <paramref name="refSpec"/>
    /// (branch / tag / SHA — if null, the remote default branch) into
    /// <paramref name="targetDir"/>. When <paramref name="subPath"/> is set, the
    /// install copies only that file or directory into <paramref name="targetDir"/>
    /// (the cloned working tree is staged in a sibling temp dir and removed).
    /// </summary>
    /// <param name="gitUrl">Any URL LibGit2Sharp accepts (file://, https://, ssh://, ...).</param>
    /// <param name="refSpec">Branch name, tag name, or commit SHA. Null means HEAD.</param>
    /// <param name="subPath">Path inside the repo to copy out, or null to keep the whole tree.</param>
    /// <param name="targetDir">Destination on the local filesystem. Must not already exist unless <paramref name="force"/>.</param>
    /// <param name="force">When true, deletes <paramref name="targetDir"/> before installing.</param>
    public static Task<InstallResult> InstallAsync(
        string gitUrl,
        string? refSpec,
        string? subPath,
        string targetDir,
        bool force = false,
        bool allowLocalTransport = false)
    {
        if (string.IsNullOrWhiteSpace(gitUrl))
            throw new ArgumentException("gitUrl must be provided.", nameof(gitUrl));
        if (string.IsNullOrWhiteSpace(targetDir))
            throw new ArgumentException("targetDir must be provided.", nameof(targetDir));

        // SECURITY: refuse to clone via LibGit2Sharp's local transport
        // (file:// or bare paths) by default. The local transport reads any
        // directory the current user can read, which turns
        // `profile install <some/local/path>` into a generic "expose this
        // directory's git contents" primitive when the gitUrl is supplied
        // by anything less trusted than the user themselves. Tests opt in
        // via allowLocalTransport: true.
        if (!allowLocalTransport && IsLocalTransport(gitUrl))
            throw new ArgumentException(
                $"Refusing to clone local-transport URL '{gitUrl}'. Use https:// or ssh://. " +
                "Local clones are blocked by default to limit supply-chain risk.",
                nameof(gitUrl));

        // Run synchronously inside Task.Run so the public surface stays awaitable
        // (matches every other XxxAsync method in the CLI) without dragging
        // LibGit2Sharp into the awaitable model — its API is fully synchronous.
        return Task.Run(() => InstallCore(gitUrl, refSpec, subPath, targetDir, force));
    }

    private static InstallResult InstallCore(
        string gitUrl,
        string? refSpec,
        string? subPath,
        string targetDir,
        bool force)
    {
        var fullTarget = Path.GetFullPath(targetDir);

        if (Directory.Exists(fullTarget))
        {
            if (!force)
                throw new InvalidOperationException(
                    $"Target directory already exists: {fullTarget}. Pass --force to overwrite.");

            // The user explicitly opted in to clobber the directory; remove it
            // so the clone (or copy) starts from a clean slate. We do this even
            // for the no-subpath case so a half-finished previous install does
            // not leak state into the new tree.
            DeleteDirectoryRobust(fullTarget);
        }

        Directory.CreateDirectory(Directory.GetParent(fullTarget)!.FullName);

        // For subpath installs we clone into a scratch directory first, then
        // copy the wanted slice into the real target. This keeps the API simple
        // (caller always gets a clean targetDir of just the bytes they asked
        // for) at the cost of one extra copy.
        var workTreeRoot = subPath == null ? fullTarget : CreateScratchDir();

        try
        {
            var cloneOptions = new CloneOptions();

            // Shallow clone keeps remote profile bundles cheap to install — we
            // never need history for these, only the tip. LibGit2Sharp's local
            // transport (file:// URLs) does not implement shallow fetch though,
            // so we limit Depth=1 to network transports. Tests cover the
            // local-transport path; production users hit the shallow path.
            if (!IsLocalTransport(gitUrl))
                cloneOptions.FetchOptions.Depth = 1;

            // We deliberately do NOT pass cloneOptions.BranchName even for
            // refs that look like branches: BranchName fails Clone outright
            // when the ref turns out to be a tag or SHA, and the post-clone
            // CheckoutRef call below already handles every shape uniformly
            // (local branch, remote branch, tag, commit SHA). One code path
            // is easier to keep correct than two specialized ones.

            var repoPath = Repository.Clone(gitUrl, workTreeRoot, cloneOptions);

            string? checkedOut = null;
            if (!string.IsNullOrWhiteSpace(refSpec))
            {
                using var repo = new Repository(repoPath);
                checkedOut = CheckoutRef(repo, refSpec!);
            }
            else
            {
                using var repo = new Repository(repoPath);
                checkedOut = repo.Head?.FriendlyName;
            }

            if (subPath != null)
            {
                // SECURITY: NormalizeSubPath only normalises separators and
                // strips a leading slash; it does NOT collapse `..` segments.
                // Without the boundary check below, `--subpath ../../outside`
                // would resolve to a real path outside the cloned worktree
                // and File.Copy / CopyDirectory would happily reach it.
                var combined = Path.Combine(workTreeRoot, NormalizeSubPath(subPath));
                var canonicalSource = Path.GetFullPath(combined);
                var workTreeCanon = Path.GetFullPath(workTreeRoot);
                var workTreeWithSep = workTreeCanon.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
                    ? workTreeCanon
                    : workTreeCanon + Path.DirectorySeparatorChar;
                if (!canonicalSource.StartsWith(workTreeWithSep, StringComparison.Ordinal)
                    && !string.Equals(canonicalSource, workTreeCanon, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"--subpath '{subPath}' resolves outside the cloned repository (refused for safety).");
                }

                if (!File.Exists(canonicalSource) && !Directory.Exists(canonicalSource))
                    throw new InvalidOperationException(
                        $"--subpath '{subPath}' does not exist inside repository {gitUrl}.");

                Directory.CreateDirectory(fullTarget);
                if (File.Exists(canonicalSource))
                {
                    var destFile = Path.Combine(fullTarget, Path.GetFileName(canonicalSource));
                    File.Copy(canonicalSource, destFile, overwrite: true);
                }
                else
                {
                    CopyDirectory(canonicalSource, fullTarget);
                }
            }

            return new InstallResult(fullTarget, checkedOut, gitUrl);
        }
        finally
        {
            if (subPath != null && Directory.Exists(workTreeRoot))
            {
                // Best-effort cleanup of the scratch clone. Failures here are
                // not fatal — the install already succeeded and the user can
                // delete the scratch dir manually if Windows holds a handle.
                try { DeleteDirectoryRobust(workTreeRoot); }
                catch { /* swallow — see comment above */ }
            }
        }
    }

    /// <summary>
    /// Resolve a tag / branch / SHA on the cloned repo and check it out into
    /// the working tree. Returns the friendly name of whatever ended up at HEAD.
    /// </summary>
    private static string CheckoutRef(Repository repo, string refSpec)
    {
        // Try in priority order so the most-specific match wins:
        //   1. Exact local or remote branch (covers `main`, `origin/main`).
        //   2. Tag (covers `v1.0`).
        //   3. Direct SHA lookup.
        // Any unresolved ref falls through to a clear error message.
        var localBranch = repo.Branches[refSpec];
        if (localBranch != null)
        {
            LibGit2Sharp.Commands.Checkout(repo, localBranch);
            return localBranch.FriendlyName;
        }

        var remoteBranch = repo.Branches[$"origin/{refSpec}"];
        if (remoteBranch != null)
        {
            // Materialize a local tracking branch so subsequent fetches stay sane;
            // checkout the new branch tip.
            var local = repo.CreateBranch(refSpec, remoteBranch.Tip);
            repo.Branches.Update(local, b => b.TrackedBranch = remoteBranch.CanonicalName);
            LibGit2Sharp.Commands.Checkout(repo, local);
            return local.FriendlyName;
        }

        var tag = repo.Tags[refSpec];
        if (tag != null)
        {
            LibGit2Sharp.Commands.Checkout(repo, tag.Target.Sha);
            return $"tags/{tag.FriendlyName}";
        }

        var commit = repo.Lookup<Commit>(refSpec);
        if (commit != null)
        {
            LibGit2Sharp.Commands.Checkout(repo, commit.Sha);
            return commit.Sha;
        }

        throw new InvalidOperationException(
            $"--ref '{refSpec}' did not resolve to any branch, tag, or commit in the cloned repository.");
    }

    /// <summary>
    /// Detect URLs that LibGit2Sharp routes through its in-process local
    /// transport. The local transport does not implement shallow fetch, so we
    /// bypass <c>Depth=1</c> for these — clones of test fixtures and bare
    /// directory paths still work, just without the depth optimization.
    /// </summary>
    private static bool IsLocalTransport(string url)
    {
        if (string.IsNullOrEmpty(url)) return false;
        if (url.StartsWith("file:", StringComparison.OrdinalIgnoreCase)) return true;
        // Bare paths (e.g. C:\repo or /tmp/repo) without a scheme are also
        // routed through the local transport. The conservative check is
        // "no '://' delimiter" — anything explicitly schemed (https/ssh/git)
        // goes through smart transports.
        return !url.Contains("://", StringComparison.Ordinal);
    }

    private static string NormalizeSubPath(string subPath) =>
        subPath.Replace('/', Path.DirectorySeparatorChar)
               .Replace('\\', Path.DirectorySeparatorChar)
               .TrimStart(Path.DirectorySeparatorChar);

    private static string CreateScratchDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "revitcli-profile-install-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);

        // Skip the .git directory in the slice copy — users want the profile
        // bytes, not the full clone metadata. This keeps installed bundles
        // small and prevents accidental nested-repo confusion.
        foreach (var dir in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, dir);
            if (relative.Split(Path.DirectorySeparatorChar)[0] == ".git")
                continue;
            Directory.CreateDirectory(Path.Combine(destination, relative));
        }

        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);
            if (relative.Split(Path.DirectorySeparatorChar)[0] == ".git")
                continue;
            var dest = Path.Combine(destination, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.Copy(file, dest, overwrite: true);
        }
    }

    /// <summary>
    /// Recursive delete that clears Git's read-only bits first. LibGit2Sharp
    /// marks pack files as read-only on Windows, which makes the naive
    /// <see cref="Directory.Delete(string, bool)"/> throw on the second install
    /// pass.
    /// </summary>
    private static void DeleteDirectoryRobust(string path)
    {
        if (!Directory.Exists(path))
            return;

        foreach (var file in Directory.GetFiles(path, "*", SearchOption.AllDirectories))
        {
            try
            {
                var attrs = File.GetAttributes(file);
                if ((attrs & FileAttributes.ReadOnly) != 0)
                    File.SetAttributes(file, attrs & ~FileAttributes.ReadOnly);
            }
            catch (UnauthorizedAccessException) { /* fall through to Directory.Delete */ }
            catch (FileNotFoundException) { /* concurrent cleanup */ }
        }

        Directory.Delete(path, recursive: true);
    }

    /// <summary>
    /// Derive a default install dir name from a git URL, e.g.
    /// <c>https://github.com/org/standards.git</c> → <c>standards</c>. Used by
    /// the CLI when the caller does not pass <c>--target</c>.
    /// </summary>
    public static string DeriveProfileName(string gitUrl)
    {
        if (string.IsNullOrWhiteSpace(gitUrl))
            throw new ArgumentException("gitUrl must be provided.", nameof(gitUrl));

        // Strip query string / fragment then take the last path segment, then
        // strip a trailing .git so the canonical name matches what users
        // already type for repo aliases (org/standards → standards).
        var withoutQuery = gitUrl.Split('?', '#')[0].TrimEnd('/');
        var lastSlash = Math.Max(withoutQuery.LastIndexOf('/'), withoutQuery.LastIndexOf('\\'));
        var tail = lastSlash >= 0 ? withoutQuery.Substring(lastSlash + 1) : withoutQuery;

        if (tail.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
            tail = tail.Substring(0, tail.Length - 4);

        return string.IsNullOrWhiteSpace(tail) ? "profile" : tail;
    }
}
