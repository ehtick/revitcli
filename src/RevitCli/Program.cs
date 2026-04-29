using System.CommandLine;
using System.Reflection;
using RevitCli.Client;
using RevitCli.Commands;
using RevitCli.Config;

System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);

// --version: handled early before any other setup
if (args.Length == 1 && args[0] is "--version" or "-v")
{
    var version = typeof(Program).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
        ?.InformationalVersion ?? "0.1.0";
    Console.WriteLine($"revitcli {version}");
    return 0;
}

// -i: shortcut for interactive mode
if (args.Length == 1 && args[0] == "-i")
{
    args = new[] { "interactive" };
}

// --verbose: enable HTTP request logging
var verbose = args.Contains("--verbose");
if (verbose)
{
    args = args.Where(a => a != "--verbose").ToArray();
}

var config = CliConfig.Load();
var (serverUrl, token) = RevitClient.DiscoverServerUrl(config.ServerUrl);
var client = new RevitClient(serverUrl, token) { Verbose = verbose };
var rootCommand = CliCommandCatalog.CreateRootCommand(
    client,
    config,
    includeInteractiveCommand: true,
    includeBatchCommand: true);
var invokeExitCode = await rootCommand.InvokeAsync(args);
return Environment.ExitCode != 0 ? Environment.ExitCode : invokeExitCode;
