using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using RevitCli.Profile;

namespace RevitCli.Mcp.Resources;

/// <summary>
/// <c>revitcli://profile/effective</c> — resolves the inheritance chain from
/// the discovered <c>.revitcli.yml</c> and returns the merged profile as YAML
/// (matching the <c>profile show --resolve</c> command). When no profile is
/// found we surface a short YAML comment so the LLM can still receive a
/// well-formed payload instead of an error.
/// </summary>
internal sealed class ProfileResource : IMcpResource
{
    private readonly Func<string?> _discover;

    public ProfileResource() : this(() => ProfileLoader.Discover())
    {
    }

    /// <summary>
    /// Test seam — lets unit tests inject a deterministic profile path
    /// instead of relying on the cwd walk.
    /// </summary>
    internal ProfileResource(Func<string?> discover)
    {
        _discover = discover;
    }

    public string Uri => "revitcli://profile/effective";

    public string Name => "Effective project profile";

    public string Description =>
        "The merged effective .revitcli.yml after walking the `extends` chain. " +
        "Returned as YAML with a header listing the resolved chain.";

    public string MimeType => "text/yaml";

    public Task<string> ReadAsync(CancellationToken cancellationToken)
    {
        var path = _discover();
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            // Empty document with a comment — keeps the payload valid YAML.
            return Task.FromResult(
                "# No .revitcli.yml found in the current working directory tree.\n");
        }

        var rendered = ProfileResolver.Render(path, ProfileRenderFormat.Yaml);
        return Task.FromResult(rendered);
    }
}
