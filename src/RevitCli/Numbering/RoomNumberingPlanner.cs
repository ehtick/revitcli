using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using RevitCli.Output;
using RevitCli.Plans;
using RevitCli.Shared;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace RevitCli.Numbering;

internal static class RoomNumberingPlanner
{
    public const string DefaultParameter = "Number";

    public static RoomNumberingPlan Create(
        IReadOnlyList<ElementInfo> rooms,
        LoadedRoomNumberingRule rule,
        string scope,
        string planPath)
    {
        ValidateRule(rule.Rule);
        var normalizedScope = string.IsNullOrWhiteSpace(scope) ? "all" : scope.Trim();
        var selected = rooms
            .Where(room => MatchesScope(room, normalizedScope))
            .OrderBy(room => GroupKey(room, rule.Rule), StringComparer.OrdinalIgnoreCase)
            .ThenBy(room => SortKey(room, rule.Rule), StringComparer.OrdinalIgnoreCase)
            .ThenBy(room => room.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(room => room.Id)
            .ToList();

        var parameter = string.IsNullOrWhiteSpace(rule.Rule.Parameter)
            ? DefaultParameter
            : rule.Rule.Parameter.Trim();
        var selectedIds = selected.Select(room => room.Id).ToHashSet();
        var allNumbers = rooms
            .Where(room => room.Parameters.TryGetValue(parameter, out var value) && !string.IsNullOrWhiteSpace(value))
            .GroupBy(room => room.Parameters[parameter], StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);
        var actions = new List<RoomNumberingPlanAction>();
        var skipped = new List<RoomNumberingPlanSkipped>();
        var conflicts = new List<string>();

        foreach (var group in selected.GroupBy(room => GroupKey(room, rule.Rule), StringComparer.OrdinalIgnoreCase))
        {
            var sequence = rule.Rule.Start;
            foreach (var room in group)
            {
                var current = GetParameter(room, parameter);
                var target = RenderScheme(rule.Rule, room, sequence);
                sequence++;

                if (string.IsNullOrWhiteSpace(target))
                {
                    skipped.Add(Skip(room, current, "empty-target", "Numbering rule produced an empty room number."));
                    continue;
                }

                if (string.Equals(current, target, StringComparison.OrdinalIgnoreCase))
                {
                    skipped.Add(Skip(room, current, "unchanged", "Room already has the generated number."));
                    continue;
                }

                if (allNumbers.TryGetValue(target, out var holders) &&
                    holders.Any(holder => holder.Id != room.Id))
                {
                    conflicts.Add(
                        $"{target} is already used by room(s): {string.Join(", ", holders.Where(holder => holder.Id != room.Id).Select(holder => $"{holder.Name} [{holder.Id}]"))}");
                    continue;
                }

                actions.Add(new RoomNumberingPlanAction(
                    room.Id,
                    room.Name,
                    room.Category,
                    parameter,
                    current,
                    target,
                    GroupKey(room, rule.Rule),
                    SortKey(room, rule.Rule)));
            }
        }

        var duplicateTargets = actions
            .GroupBy(action => action.NewNumber, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();
        foreach (var duplicate in duplicateTargets)
            conflicts.Add($"target room number {duplicate} appears more than once in the plan");

        if (conflicts.Count > 0)
        {
            var preview = string.Join("; ", conflicts.Take(5));
            if (conflicts.Count > 5)
                preview += $"; and {conflicts.Count - 5} more";
            throw new InvalidOperationException($"Room renumber would overwrite existing room numbers: {preview}");
        }

        var fullPlanPath = Path.GetFullPath(planPath);
        var fullRulePath = Path.GetFullPath(rule.Path);
        return new RoomNumberingPlan(
            "room-numbering-plan.v1",
            "room-numbering",
            "rooms renumber",
            DateTime.UtcNow.ToString("o"),
            Environment.UserName,
            true,
            normalizedScope,
            fullRulePath,
            parameter,
            new RoomNumberingPlanSummary(rooms.Count, selected.Count, actions.Count, skipped.Count),
            actions,
            skipped,
            new SetPlanCommands
            {
                Show = $"revitcli plan show {Quote(fullPlanPath)} --output markdown",
                DryRunApply = $"revitcli plan apply {Quote(fullPlanPath)} --dry-run",
                Apply = $"revitcli plan apply {Quote(fullPlanPath)} --yes"
            });
    }

    public static void ValidateRule(RoomNumberingRule rule)
    {
        if (string.IsNullOrWhiteSpace(rule.Scheme))
            throw new InvalidOperationException("Room renumber requires scheme.");
        if (rule.Start < 0)
            throw new InvalidOperationException("Room renumber start must be zero or greater.");
    }

    internal static List<ImportGroup> CreateGroups(RoomNumberingPlan plan)
    {
        return plan.Actions
            .Where(action => action.RoomId > 0)
            .GroupBy(
                action => new { action.Parameter, Value = action.NewNumber },
                (key, actions) => new ImportGroup
                {
                    Param = key.Parameter,
                    Value = key.Value,
                    ElementIds = actions.Select(action => action.RoomId).Distinct().OrderBy(id => id).ToList()
                })
            .OrderBy(group => group.Param, StringComparer.OrdinalIgnoreCase)
            .ThenBy(group => group.Value, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static RoomNumberingPlanSkipped Skip(ElementInfo room, string oldNumber, string reason, string message) =>
        new(room.Id, room.Name, room.Category, oldNumber, reason, message);

    private static string GetParameter(ElementInfo room, string parameter)
    {
        var key = room.Parameters.Keys.FirstOrDefault(candidate =>
            string.Equals(candidate, parameter, StringComparison.OrdinalIgnoreCase));
        return key == null ? "" : room.Parameters[key] ?? "";
    }

    private static bool MatchesScope(ElementInfo room, string scope)
    {
        if (scope.Equals("all", StringComparison.OrdinalIgnoreCase))
            return true;

        return MatchesPattern(room.Name, scope) ||
               MatchesPattern(room.TypeName, scope) ||
               room.Parameters.Values.Any(value => MatchesPattern(value, scope));
    }

    private static string GroupKey(ElementInfo room, RoomNumberingRule rule)
    {
        if (rule.GroupBy.Count == 0)
            return "";

        return string.Join("|", rule.GroupBy.Select(token => ResolveToken(room, rule, token)));
    }

    private static string SortKey(ElementInfo room, RoomNumberingRule rule)
    {
        var sort = rule.Sort.Count == 0 ? new List<string> { "level", "zone", "type", "name" } : rule.Sort;
        return string.Join("|", sort.Select(token => ResolveToken(room, rule, token)));
    }

    private static string RenderScheme(RoomNumberingRule rule, ElementInfo room, int sequence)
    {
        return Regex.Replace(rule.Scheme, @"\{([^{}]+)\}", match =>
        {
            var token = match.Groups[1].Value.Trim();
            if (token.StartsWith("seq", StringComparison.OrdinalIgnoreCase))
                return FormatSequence(sequence, token);

            return ResolveToken(room, rule, token);
        });
    }

    private static string FormatSequence(int sequence, string token)
    {
        var parts = token.Split(':', 2);
        if (parts.Length == 2 && int.TryParse(parts[1], out var width) && width > 0)
            return sequence.ToString("D" + width);
        return sequence.ToString();
    }

    private static string ResolveToken(ElementInfo room, RoomNumberingRule rule, string token)
    {
        if (token.Equals("name", StringComparison.OrdinalIgnoreCase))
            return room.Name;
        if (token.Equals("type", StringComparison.OrdinalIgnoreCase))
            return room.TypeName;
        if (token.Equals("id", StringComparison.OrdinalIgnoreCase))
            return room.Id.ToString();

        var parameter = rule.Tokens.TryGetValue(token, out var mapped)
            ? mapped
            : token switch
            {
                var value when value.Equals("level", StringComparison.OrdinalIgnoreCase) => "Level",
                var value when value.Equals("zone", StringComparison.OrdinalIgnoreCase) => "Zone",
                _ => token
            };
        return GetParameter(room, parameter);
    }

    private static bool MatchesPattern(string? value, string pattern)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;
        if (!pattern.Contains('*', StringComparison.Ordinal) && !pattern.Contains('?', StringComparison.Ordinal))
            return value.Contains(pattern, StringComparison.OrdinalIgnoreCase);

        var regex = "^" + Regex.Escape(pattern).Replace("\\*", ".*", StringComparison.Ordinal).Replace("\\?", ".", StringComparison.Ordinal) + "$";
        return Regex.IsMatch(value, regex, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static string Quote(string value) => $"\"{value.Replace("\"", "\\\"", StringComparison.Ordinal)}\"";
}

internal static class RoomNumberingRuleStore
{
    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    public static LoadedRoomNumberingRule Load(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException($"Room numbering rule not found: {fullPath}");

        var rule = Deserializer.Deserialize<RoomNumberingRule>(File.ReadAllText(fullPath))
            ?? throw new InvalidOperationException($"Failed to parse room numbering rule: {fullPath}");
        RoomNumberingPlanner.ValidateRule(rule);
        return new LoadedRoomNumberingRule(fullPath, rule);
    }
}

internal static class RoomNumberingPlanStore
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static void Save(string path, RoomNumberingPlan plan)
    {
        var fullPath = Path.GetFullPath(path);
        var dir = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(dir))
            Directory.CreateDirectory(dir);

        File.WriteAllText(fullPath, JsonSerializer.Serialize(plan, JsonOptions));
    }

    public static RoomNumberingPlan Load(string path)
    {
        var plan = JsonSerializer.Deserialize<RoomNumberingPlan>(File.ReadAllText(path), JsonOptions)
            ?? throw new InvalidOperationException("Room numbering plan is empty or invalid JSON.");
        if (!string.Equals(plan.SchemaVersion, "room-numbering-plan.v1", StringComparison.Ordinal))
            throw new InvalidOperationException($"Unsupported room numbering schemaVersion '{plan.SchemaVersion}'.");
        if (!string.Equals(plan.Type, "room-numbering", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Unsupported room numbering plan type '{plan.Type}'.");
        return plan;
    }
}

internal sealed record LoadedRoomNumberingRule(string Path, RoomNumberingRule Rule);

public sealed class RoomNumberingRule
{
    public int SchemaVersion { get; set; } = 1;
    public string Parameter { get; set; } = RoomNumberingPlanner.DefaultParameter;
    public string Scheme { get; set; } = "";
    public int Start { get; set; } = 1;
    public List<string> GroupBy { get; set; } = new();
    public List<string> Sort { get; set; } = new();
    public Dictionary<string, string> Tokens { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed record RoomNumberingPlan(
    [property: JsonPropertyName("schemaVersion")] string SchemaVersion,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("command")] string Command,
    [property: JsonPropertyName("createdAtUtc")] string CreatedAtUtc,
    [property: JsonPropertyName("createdBy")] string CreatedBy,
    [property: JsonPropertyName("dryRun")] bool DryRun,
    [property: JsonPropertyName("scope")] string Scope,
    [property: JsonPropertyName("rulePath")] string RulePath,
    [property: JsonPropertyName("parameter")] string Parameter,
    [property: JsonPropertyName("summary")] RoomNumberingPlanSummary Summary,
    [property: JsonPropertyName("actions")] IReadOnlyList<RoomNumberingPlanAction> Actions,
    [property: JsonPropertyName("skipped")] IReadOnlyList<RoomNumberingPlanSkipped> Skipped,
    [property: JsonPropertyName("commands")] SetPlanCommands Commands);

public sealed record RoomNumberingPlanSummary(
    [property: JsonPropertyName("roomCount")] int RoomCount,
    [property: JsonPropertyName("selectedCount")] int SelectedCount,
    [property: JsonPropertyName("actionCount")] int ActionCount,
    [property: JsonPropertyName("skippedCount")] int SkippedCount);

public sealed record RoomNumberingPlanAction(
    [property: JsonPropertyName("roomId")] long RoomId,
    [property: JsonPropertyName("roomName")] string RoomName,
    [property: JsonPropertyName("category")] string Category,
    [property: JsonPropertyName("parameter")] string Parameter,
    [property: JsonPropertyName("oldNumber")] string OldNumber,
    [property: JsonPropertyName("newNumber")] string NewNumber,
    [property: JsonPropertyName("groupKey")] string GroupKey,
    [property: JsonPropertyName("sortKey")] string SortKey);

public sealed record RoomNumberingPlanSkipped(
    [property: JsonPropertyName("roomId")] long RoomId,
    [property: JsonPropertyName("roomName")] string RoomName,
    [property: JsonPropertyName("category")] string Category,
    [property: JsonPropertyName("oldNumber")] string OldNumber,
    [property: JsonPropertyName("reason")] string Reason,
    [property: JsonPropertyName("message")] string Message);
