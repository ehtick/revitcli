using System.CommandLine;
using System.Linq;
using RevitCli.Config;
using Spectre.Console;

namespace RevitCli.Commands;

public static class ConfigCommand
{
    internal static readonly string[] ValidKeys =
    {
        "serverUrl",
        "defaultOutput",
        "exportDir",
        "Revit2024InstallDir",
        "Revit2025InstallDir",
        "Revit2026InstallDir"
    };

    public static Command Create()
    {
        var command = new Command("config", "View or modify CLI configuration");

        var showCommand = new Command("show", "Show current configuration");
        showCommand.SetHandler(() =>
        {
            var config = CliConfig.Load();
            var table = new Table().Border(TableBorder.Rounded);
            table.AddColumn("[bold]Setting[/]");
            table.AddColumn("[bold]Value[/]");
            table.AddRow("serverUrl", $"[cyan]{Markup.Escape(config.ServerUrl)}[/]");
            table.AddRow("defaultOutput", $"[green]{Markup.Escape(config.DefaultOutput)}[/]");
            table.AddRow("exportDir", Markup.Escape(config.ExportDir));
            table.AddRow("Revit2024InstallDir", Markup.Escape(config.Revit2024InstallDir ?? ""));
            table.AddRow("Revit2025InstallDir", Markup.Escape(config.Revit2025InstallDir ?? ""));
            table.AddRow("Revit2026InstallDir", Markup.Escape(config.Revit2026InstallDir ?? ""));
            AnsiConsole.Write(table);
        });

        var setCommand = new Command("set", "Set a configuration value");
        var keyArg = new Argument<string>("key", $"Setting name ({string.Join(", ", ValidKeys)})");
        var valueArg = new Argument<string>("value", "New value");
        setCommand.AddArgument(keyArg);
        setCommand.AddArgument(valueArg);
        setCommand.SetHandler((key, value) =>
        {
            var config = CliConfig.Load();
            switch (key.ToLower())
            {
                case "serverurl":
                    if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
                        (uri.Scheme != "http" && uri.Scheme != "https"))
                    {
                        AnsiConsole.MarkupLine($"[red]Invalid URL:[/] {Markup.Escape(value)}");
                        return;
                    }
                    config.ServerUrl = value;
                    break;
                case "defaultoutput":
                    if (!QueryCommand.ValidOutputFormats.Contains(value.ToLowerInvariant()))
                    {
                        AnsiConsole.MarkupLine($"[red]Invalid format:[/] must be one of: {string.Join(", ", QueryCommand.ValidOutputFormats)}");
                        return;
                    }
                    config.DefaultOutput = value.ToLowerInvariant();
                    break;
                case "exportdir":
                    if (string.IsNullOrWhiteSpace(value))
                    {
                        AnsiConsole.MarkupLine("[red]Invalid directory:[/] cannot be empty");
                        return;
                    }
                    config.ExportDir = value;
                    break;
                case "revit2024installdir":
                    if (!SetRevitInstallDir(config, 2024, value))
                        return;
                    break;
                case "revit2025installdir":
                    if (!SetRevitInstallDir(config, 2025, value))
                        return;
                    break;
                case "revit2026installdir":
                    if (!SetRevitInstallDir(config, 2026, value))
                        return;
                    break;
                default:
                    AnsiConsole.MarkupLine($"[red]Unknown setting:[/] {Markup.Escape(key)}");
                    return;
            }
            config.Save();
            AnsiConsole.MarkupLine($"[green]Set[/] {Markup.Escape(key)} = {Markup.Escape(value)}");
        }, keyArg, valueArg);

        command.AddCommand(showCommand);
        command.AddCommand(setCommand);
        return command;
    }

    private static bool SetRevitInstallDir(CliConfig config, int year, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            AnsiConsole.MarkupLine("[red]Invalid directory:[/] cannot be empty");
            return false;
        }

        switch (year)
        {
            case 2024:
                config.Revit2024InstallDir = value;
                break;
            case 2025:
                config.Revit2025InstallDir = value;
                break;
            case 2026:
                config.Revit2026InstallDir = value;
                break;
        }

        return true;
    }
}
