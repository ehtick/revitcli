using System;
using System.CommandLine;
using System.Threading;
using RevitCli.Client;
using RevitCli.Mcp;

namespace RevitCli.Commands;

/// <summary>
/// `revitcli mcp serve` — start a Model Context Protocol server on stdio.
///
/// Side track per docs/roadmap-2026q2-q3.md §8. Read-only skeleton: exposes
/// `status`, `query`, `audit` tools. Resources / write tools / transport
/// variants are deferred.
/// </summary>
public static class McpCommand
{
    public static Command Create(RevitClient client)
    {
        var mcp = new Command("mcp", "Model Context Protocol adapter (stdio).");

        var serve = new Command("serve", "Run the MCP server on stdio (newline-delimited JSON-RPC 2.0).");
        serve.SetHandler(async () =>
        {
            // stderr is the only safe channel: stdout is reserved for protocol bytes.
            var server = McpServer.CreateDefault(client, Console.In, Console.Out, Console.Error);
            using var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true;
                cts.Cancel();
            };
            Environment.ExitCode = await server.RunAsync(cts.Token).ConfigureAwait(false);
        });

        mcp.AddCommand(serve);
        return mcp;
    }
}
