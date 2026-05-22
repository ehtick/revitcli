using System;
using System.Text.RegularExpressions;

namespace RevitCli.Sheets;

internal sealed partial class SheetNumberScheme
{
    private readonly string _prefix;
    private readonly string _middle;
    private readonly string _suffix;
    private readonly int _floorWidth;
    private readonly int _seqWidth;
    private readonly Regex _regex;

    private SheetNumberScheme(string raw, string prefix, string middle, string suffix, int floorWidth, int seqWidth)
    {
        Raw = raw;
        _prefix = prefix;
        _middle = middle;
        _suffix = suffix;
        _floorWidth = floorWidth;
        _seqWidth = seqWidth;
        _regex = new Regex(
            "^" + Regex.Escape(prefix) + @"(?<floor>\d{" + floorWidth + @"})"
            + Regex.Escape(middle) + @"(?<seq>\d{" + seqWidth + @"})"
            + Regex.Escape(suffix) + "$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    public string Raw { get; }

    public static SheetNumberScheme Parse(string scheme)
    {
        if (string.IsNullOrWhiteSpace(scheme))
            throw new InvalidOperationException("Sheet numbering scheme is required.");

        var match = SchemeRegex().Match(scheme);
        if (!match.Success)
        {
            throw new InvalidOperationException(
                $"Unsupported sheet numbering scheme '{scheme}'. Use token form like A-{{floor:01}}{{seq:02}}.");
        }

        return new SheetNumberScheme(
            scheme,
            match.Groups["prefix"].Value,
            match.Groups["middle"].Value,
            match.Groups["suffix"].Value,
            match.Groups["floorFormat"].Value.Length,
            match.Groups["seqFormat"].Value.Length);
    }

    public bool TryParse(string number, out int floor, out int seq)
    {
        floor = 0;
        seq = 0;
        var match = _regex.Match(number);
        return match.Success
               && int.TryParse(match.Groups["floor"].Value, out floor)
               && int.TryParse(match.Groups["seq"].Value, out seq);
    }

    public string Generate(int floor, int seq)
    {
        return _prefix
               + floor.ToString("D" + _floorWidth)
               + _middle
               + seq.ToString("D" + _seqWidth)
               + _suffix;
    }

    [GeneratedRegex(@"^(?<prefix>.*)\{floor:(?<floorFormat>\d+)\}(?<middle>.*)\{seq:(?<seqFormat>\d+)\}(?<suffix>.*)$", RegexOptions.CultureInvariant)]
    private static partial Regex SchemeRegex();
}
