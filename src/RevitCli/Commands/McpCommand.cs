using System;
using System.CommandLine;
using System.Threading;
using RevitCli.Client;
using RevitCli.Mcp;

namespace RevitCli.Commands;

/// <summary>
/// `revitcli mcp serve` — start a Model Context Protocol server on stdio.
///
/// Side track per docs/roadmap-2026q2-q3.md §8. Phase 1 shipped read-only
/// `status` / `query` / `audit` tools. Phase 2 adds:
///
/// <list type="bullet">
///   <item>Resources: <c>revitcli://snapshot/latest</c>,
///         <c>revitcli://history/</c>, <c>revitcli://profile/effective</c>.</item>
///   <item>Read-only <c>snapshot</c> tool.</item>
///   <item><c>set</c> tool gated behind <c>--allow-writes</c> + per-call
///         <c>confirm: true</c>.</item>
/// </list>
/// </summary>
public static class McpCommand
{
    public static Command Create(RevitClient client)
    {
        var mcp = new Command("mcp", "Model Context Protocol adapter (stdio).");

        var serve = new Command("serve", "Run the MCP server on stdio (newline-delimited JSON-RPC 2.0).");
        var allowWrites = new Option<bool>(
            "--allow-writes",
            description: "Enable mutating tools (e.g. `set`). Each call still needs `confirm: true`.",
            getDefaultValue: () => false);
        serve.AddOption(allowWrites);

        serve.SetHandler(async (bool writes) =>
        {
            // stderr is the only safe channel: stdout is reserved for protocol bytes.
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
