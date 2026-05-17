using System;
using System.IO;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace RevitCli.Sheets;

internal static class SheetIndexStore
{
    public const string DefaultRelativePath = ".revitcli/sheets/index.yml";

    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    private static readonly ISerializer Serializer = new SerializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull)
        .Build();

    public static string ResolvePath(string? path)
    {
        return Path.GetFullPath(string.IsNullOrWhiteSpace(path) ? DefaultRelativePath : path);
    }

    public static LoadedSheetIndex? TryLoadDefault()
    {
        var path = ResolvePath(null);
        return File.Exists(path) ? Load(path) : null;
    }

    public static LoadedSheetIndex Load(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var yaml = File.ReadAllText(fullPath);
        var index = Deserializer.Deserialize<SheetIndex>(yaml)
            ?? throw new InvalidOperationException($"Sheet index is empty: {fullPath}");

        if (index.SchemaVersion != 1)
        {
            throw new InvalidOperationException(
                $"Sheet index schemaVersion must be 1, got {index.SchemaVersion}.");
        }

        return new LoadedSheetIndex(fullPath, index);
    }

    public static string ToYaml(SheetIndex index) => Serializer.Serialize(index);
}

internal sealed record LoadedSheetIndex(string Path, SheetIndex Index);
