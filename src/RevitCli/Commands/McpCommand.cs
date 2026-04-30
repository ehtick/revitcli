using System;
using System.CommandLine;
using System.Threading;
using RevitCli.Client;
using RevitCli.Mcp;

namespace RevitCli.Commands;

/// <summary>
/// Hidden legacy MCP compatibility entry point.
///
/// MCP is retired from the product roadmap. Keep this command callable for
/// existing local scripts, but do not expose it in help, completions, or new
/// docs.
/// </summary>
public static class McpCommand
{
    public static Command Create(RevitClient client)
    {
        var mcp = new Command("mcp", "Deprecated legacy MCP adapter (hidden).")
        {
            IsHidden = true
        };

        var serve = new Command("serve", "Run the deprecated MCP server on stdio (newline-delimited JSON-RPC 2.0).")
        {
            IsHidden = true
        };
        var allowWrites = new Option<bool>(
            "--allow-writes",
            description: "Legacy compatibility only. Enable mutating tools (e.g. `set`). Each call still needs `confirm: true`.",
            getDefaultValue: () => false);
        serve.AddOption(allowWrites);

        serve.SetHandler(async (bool writes) =>
        {
            // stderr is the only safe channel: stdout is reserved for protocol bytes.
            Console.Error.WriteLine("[deprecated] `revitcli mcp serve` is hidden and retired from the roadmap. Prefer Codex CLI calling visible `revitcli` commands.");
            var server = McpServer.CreateDefault(client, Console.In, Console.Out, Console.Error, writes);
            using var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true;
                cts.Cancel();
            };
            Environment.ExitCode = await server.RunAsync(cts.Token).ConfigureAwait(false);
        }, allowWrites);

        mcp.AddCommand(serve);
        return mcp;
    }
}
