using System.Collections.Generic;
using System.Linq;
using RevitCli.History;
using Xunit;

namespace RevitCli.Tests.History;

public class TrendRendererTests
{
    [Fact]
    public void Render_Empty_ReportsNoSnapshots()
    {
        var result = TrendRenderer.Render(new List<TrendRenderer.Point>());
        Assert.Equal(string.Empty, result.Sparkline);
        Assert.Single(result.Rows);
        Assert.Contains("No snapshots in window", result.Rows[0]);
    }

    [Fact]
    public void Render_SinglePoint_FillsWidthWithOneGlyph()
    {
        var points = new[] { new TrendRenderer.Point("2026-04-27", 5.0) };
        var result = TrendRenderer.Render(points, width: 10);
        Assert.Equal(10, result.Sparkline.Length);
        // Single point with no range => flat mid-height bar.
        var midGlyph = TrendRenderer.BlockChars[TrendRenderer.BlockChars.Length / 2];
        Assert.Equal(new string(midGlyph, 10), result.Sparkline);
        Assert.Single(result.Rows);
        Assert.Contains("2026-04-27", result.Rows[0]);
        Assert.Contains("5", result.Rows[0]);
    }

    [Fact]
    public void Render_AscendingValues_StartsLowEndsHigh()
    {
        var points = Enumerable.Range(0, 5)
            .Select(i => new TrendRenderer.Point($"d{i}", (double)i))
            .ToList();
        var result = TrendRenderer.Render(points, width: 5);

        Assert.Equal(5, result.Sparkline.Length);
        Assert.Equal(TrendRenderer.BlockChars[0], result.Sparkline[0]);
        Assert.Equal(TrendRenderer.BlockChars[TrendRenderer.BlockChars.Length - 1],
            result.Sparkline[result.Sparkline.Length - 1]);
    }

    [Fact]
    public void Render_DescendingValues_StartsHighEndsLow()
    {
        var points = Enumerable.Range(0, 5)
            .Select(i => new TrendRenderer.Point($"d{i}", (double)(4 - i)))
            .ToList();
        var result = TrendRenderer.Render(points, width: 5);

        Assert.Equal(5, result.Sparkline.Length);
        Assert.Equal(TrendRenderer.BlockChars[TrendRenderer.BlockChars.Length - 1], result.Sparkline[0]);
        Assert.Equal(TrendRenderer.BlockChars[0], result.Sparkline[result.Sparkline.Length - 1]);
    }

    [Fact]
    public void Render_FlatValues_RendersMidHeightBar()
    {
        var points = Enumerable.Range(0, 4)
            .Select(i => new TrendRenderer.Point($"d{i}", 42.0))
            .ToList();
        var result = TrendRenderer.Render(points, width: 8);
        var midGlyph = TrendRenderer.BlockChars[TrendRenderer.BlockChars.Length / 2];
        Assert.Equal(new string(midGlyph, 8), result.Sparkline);
    }

    [Fact]
    public void Render_MissingValue_RendersBlankCell()
    {
        var points = new[]
        {
            new TrendRenderer.Point("d0", 1.0),
            new TrendRenderer.Point("d1", null),
            new TrendRenderer.Point("d2", 5.0),
        };
        var result = TrendRenderer.Render(points, width: 6);
        Assert.Equal(6, result.Sparkline.Length);
        Assert.Contains(TrendRenderer.MissingChar, result.Sparkline);
        // Per-point rows show a dash for the missing value.
        Assert.Equal(3, result.Rows.Count);
        Assert.Contains("-", result.Rows[1]);
    }

    [Fact]
    public void Render_PointsFewerThanWidth_StretchesBars()
    {
        var points = new[]
        {
            new TrendRenderer.Point("d0", 0.0),
            new TrendRenderer.Point("d1", 10.0),
        };
        var result = TrendRenderer.Render(points, width: 10);
        Assert.Equal(10, result.Sparkline.Length);
        // Each input point should drive at least width/count cells with the same glyph.
        var firstGlyph = result.Sparkline[0];
        var lastGlyph = result.Sparkline[result.Sparkline.Length - 1];
        Assert.Equal(TrendRenderer.BlockChars[0], firstGlyph);
        Assert.Equal(TrendRenderer.BlockChars[TrendRenderer.BlockChars.Length - 1], lastGlyph);
    }

    [Fact]
    public void Render_AllMissing_ProducesPlaceholderLine()
    {
        var points = new[]
        {
            new TrendRenderer.Point("d0", null),
            new TrendRenderer.Point("d1", null),
        };
        var result = TrendRenderer.Render(points, width: 4);
        Assert.Equal(4, result.Sparkline.Length);
        foreach (var ch in result.Sparkline)
        {
            Assert.Equal(TrendRenderer.MissingChar, ch);
        }
    }

    [Fact]
    public void Render_BlockCharsRoundTripUtf8()
    {
        var points = Enumerable.Range(0, 8)
            .Select(i => new TrendRenderer.Point($"d{i}", (double)i))
            .ToList();
        var result = TrendRenderer.Render(points, width: 8);
        // Every glyph must be one of the block chars (UTF-8 round-trippable).
        foreach (var ch in result.Sparkline)
        {
            Assert.Contains(ch, TrendRenderer.BlockChars);
        }
    }
}
