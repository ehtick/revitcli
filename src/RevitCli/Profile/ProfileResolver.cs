using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace RevitCli.Profile;

/// <summary>
/// Pure resolver that walks the <c>extends</c> chain (currently single-parent
/// only — array extends is deferred per the v1.9 plan) and reports both the
/// effective merged profile and the chain order, so callers can render
/// <c>profile show --resolve</c>.
/// </summary>
public static class ProfileResolver
{
    /// <summary>
    /// Walk the <c>extends</c> chain starting from <paramref name="profilePath"/>
    /// and return the chain in order from farthest ancestor to the input file.
    /// The returned paths are absolute and use <see cref="Path.GetFullPath(string)"/>
    /// canonical form.
    /// </summary>
    public static IReadOnlyList<string> GetInheritanceChain(string profilePath)
    {
        if (string.IsNullOrWhiteSpace(profilePath))
            throw new ArgumentException("profilePath must be non-empty.", nameof(profilePath));

        var canonical = Path.GetFullPath(profilePath);
        if (!File.Exists(canonical))
            throw new FileNotFoundException($"Profile not found: {canonical}", canonical);

        // Walk parents recursively, collecting in child-first order, then reverse
        // at the end so consumers see [oldest ancestor, ..., effective].
        var chain = new List<string>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var current = canonical;

        while (current != null)
        {
            if (!visited.Add(current))
                throw new InvalidOperationException(
                    $"Circular profile inheritance detected at {current}.");

            chain.Add(current);

            var extendsRaw = ReadExtends(current);
            if (string.IsNullOrWhiteSpace(extendsRaw))
                break;

            var baseDir = Path.GetDirectoryName(current)!;
            var parentPath = Path.GetFullPath(Path.Combine(baseDir, extendsRaw!));
            if (!File.Exists(parentPath))
                throw new FileNotFoundException(
                    $"Profile '{current}' extends '{extendsRaw}' which does not exist (resolved {parentPath}).",
                    parentPath);

            current = parentPath;
        }

        chain.Reverse();
        return chain;
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

    private static string? ReadExtends(string path)
    {
        // Build a deserializer focused only on the 'extends' key so we don't
        // re-run the full ProjectProfile schema validation here — the loader
        // will do it during the Load call. Keeps this function infallible
        // against unrelated schema noise.
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();

        var yaml = File.ReadAllText(path);
        try
        {
            var stub = deserializer.Deserialize<ExtendsStub>(yaml);
            return string.IsNullOrWhiteSpace(stub?.Extends) ? null : stub!.Extends;
        }
        catch (YamlDotNet.Core.YamlException)
        {
            // The full loader will surface the parse error with a richer message
            // when the caller invokes Render; here we just bail out of the walk.
            return null;
        }
    }

    private sealed class ExtendsStub
    {
        [YamlMember(Alias = "extends")]
        public string? Extends { get; set; }
    }
}

/// <summary>Output format for <see cref="ProfileResolver.Render"/>.</summary>
public enum ProfileRenderFormat
{
    Yaml = 0,
    Json = 1,
}
