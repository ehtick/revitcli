using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace RevitCli.Profile;

/// <summary>
/// Pure resolver that walks the <c>extends</c> chain — including v1.9
/// multi-parent <c>extends: [a.yml, b.yml]</c> shape and diamond inheritance
/// (a→b,c; b→d; c→d) — and reports both the effective merged profile and
/// the chain order, so callers can render <c>profile show --resolve</c>.
/// </summary>
public static class ProfileResolver
{
    /// <summary>
    /// Walk the <c>extends</c> graph starting from <paramref name="profilePath"/>
    /// and return ancestors in post-order (oldest ancestor first, leaf last).
    /// The returned paths are absolute and use <see cref="Path.GetFullPath(string)"/>
    /// canonical form. Each path appears at most once even when multiple
    /// branches reach it (diamond inheritance), and a true cycle on the
    /// recursion stack throws <see cref="InvalidOperationException"/>.
    /// </summary>
    public static IReadOnlyList<string> GetInheritanceChain(string profilePath)
    {
        if (string.IsNullOrWhiteSpace(profilePath))
            throw new ArgumentException("profilePath must be non-empty.", nameof(profilePath));

        var canonical = Path.GetFullPath(profilePath);
        if (!File.Exists(canonical))
            throw new FileNotFoundException($"Profile not found: {canonical}", canonical);

        var chain = new List<string>();
        var chainSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var inProgress = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        Walk(canonical, chain, chainSet, inProgress);
        return chain;
    }

    private static void Walk(
        string current,
        List<string> chain,
        HashSet<string> chainSet,
        HashSet<string> inProgress)
    {
        // Cycle detection: only flag a true cycle on the active recursion
        // stack. Diamonds (the same ancestor reached via two different
        // branches) are NOT cycles — Pull request #10's deep-merge mode
        // explicitly allows them, so we silently skip the second visit.
        if (inProgress.Contains(current))
            throw new InvalidOperationException(
                $"Circular profile inheritance detected at {current}.");
        if (chainSet.Contains(current))
            return;

        inProgress.Add(current);
        try
        {
            var baseDir = Path.GetDirectoryName(current)!;
            foreach (var raw in ReadExtendsList(current))
            {
                var parentPath = Path.GetFullPath(Path.Combine(baseDir, raw));
                if (!File.Exists(parentPath))
                    throw new FileNotFoundException(
                        $"Profile '{current}' extends '{raw}' which does not exist (resolved {parentPath}).",
                        parentPath);
                Walk(parentPath, chain, chainSet, inProgress);
            }
            // Post-order: every parent has been added before us, so the
            // resulting list reads "oldest ancestor first" without a
            // separate Reverse() pass.
            chain.Add(current);
            chainSet.Add(current);
        }
        finally
        {
            inProgress.Remove(current);
        }
    }

    /// <summary>
    /// Resolve <paramref name="profilePath"/> via <see cref="ProfileLoader.Load"/>
    /// and emit the merged effective profile in the requested format. Adds a
    /// header comment line listing the inheritance chain so reviewers can see
    /// where each value came from.
    /// </summary>
    public static string Render(string profilePath, ProfileRenderFormat format)
    {
        if (string.IsNullOrWhiteSpace(profilePath))
            throw new ArgumentException("profilePath must be non-empty.", nameof(profilePath));

        var chain = GetInheritanceChain(profilePath);
        var merged = ProfileLoader.Load(profilePath);

        return format switch
        {
            ProfileRenderFormat.Json => RenderJson(merged, chain),
            ProfileRenderFormat.Yaml => RenderYaml(merged, chain),
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unsupported render format."),
        };
    }

    private static string RenderYaml(ProjectProfile profile, IReadOnlyList<string> chain)
    {
        var serializer = new SerializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .DisableAliases()
            .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull)
            .Build();

        var body = serializer.Serialize(profile);
        var sb = new StringBuilder();
        sb.Append(BuildHeader(chain, "# "));
        sb.Append(body);
        return sb.ToString();
    }

    private static string RenderJson(ProjectProfile profile, IReadOnlyList<string> chain)
    {
        // Use camelCase to match the YAML wire form so callers can map identifiers
        // back to docs without translating naming conventions.
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        };

        var sb = new StringBuilder();
        sb.Append(BuildHeader(chain, "// "));
        sb.Append(JsonSerializer.Serialize(profile, options));
        sb.Append('\n');
        return sb.ToString();
    }

    private static string BuildHeader(IReadOnlyList<string> chain, string commentPrefix)
    {
        var sb = new StringBuilder();
        sb.Append(commentPrefix);
        sb.Append("Resolved profile (chain: ");
        if (chain.Count == 0)
        {
            sb.Append("<empty>");
        }
        else
        {
            for (var i = 0; i < chain.Count; i++)
            {
                if (i > 0) sb.Append(" <- ");
                // Render only the file name so the header stays compact and
                // does not leak absolute filesystem paths into PR diffs.
                sb.Append(Path.GetFileName(chain[i]));
            }
            sb.Append(" <- effective");
        }
        sb.Append(')');
        sb.Append('\n');
        return sb.ToString();
    }

    private static IReadOnlyList<string> ReadExtendsList(string path)
    {
        // Build a deserializer focused only on the 'extends' key so we don't
        // re-run the full ProjectProfile schema validation here — the loader
        // will do it during the Load call. Keeps this function infallible
        // against unrelated schema noise. We type Extends as object? so a
        // single string AND an array literal both deserialise without an
        // exception; NormalizeExtends reduces both shapes to List<string>.
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();

        var yaml = File.ReadAllText(path);
        try
        {
            var stub = deserializer.Deserialize<ExtendsStub>(yaml);
            return NormalizeExtends(stub?.Extends);
        }
        catch (YamlDotNet.Core.YamlException)
        {
            // The full loader will surface the parse error with a richer message
            // when the caller invokes Render; here we just bail out of the walk.
            return Array.Empty<string>();
        }
    }

    private static IReadOnlyList<string> NormalizeExtends(object? raw)
    {
        if (raw == null)
            return Array.Empty<string>();

        if (raw is string s)
            return string.IsNullOrWhiteSpace(s)
                ? Array.Empty<string>()
                : new[] { s.Trim() };

        if (raw is System.Collections.IEnumerable list)
        {
            var result = new List<string>();
            foreach (var item in list)
            {
                if (item is string str && !string.IsNullOrWhiteSpace(str))
                    result.Add(str.Trim());
            }
            return result;
        }

        return Array.Empty<string>();
    }

    private sealed class ExtendsStub
    {
        [YamlMember(Alias = "extends")]
        public object? Extends { get; set; }
    }
}

/// <summary>Output format for <see cref="ProfileResolver.Render"/>.</summary>
public enum ProfileRenderFormat
{
    Yaml = 0,
    Json = 1,
}
