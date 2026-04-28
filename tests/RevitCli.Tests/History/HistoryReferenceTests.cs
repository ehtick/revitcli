using System;
using System.Collections.Generic;
using RevitCli.History;
using Xunit;

namespace RevitCli.Tests.History;

public class HistoryReferenceTests
{
    private static List<SnapshotMetadata> Sample()
    {
        return new List<SnapshotMetadata>
        {
            new() { Id = "snapshot-20260101T000000Z-aaaaaaaa", CapturedAt = "2026-01-01T00:00:00.0000000+00:00" },
            new() { Id = "snapshot-20260201T000000Z-bbbbbbbb", CapturedAt = "2026-02-01T00:00:00.0000000+00:00" },
            new() { Id = "snapshot-20260301T000000Z-cccccccc", CapturedAt = "2026-03-01T00:00:00.0000000+00:00" },
        };
    }

    [Fact]
    public void Parse_AtMinusOne_ReturnsMostRecent()
    {
        var selector = HistoryReference.Parse("@-1");
        var picked = selector(Sample());
        Assert.NotNull(picked);
        Assert.Equal("snapshot-20260301T000000Z-cccccccc", picked!.Id);
    }

    [Fact]
    public void Parse_AtMinusTwo_ReturnsSecondMostRecent()
    {
        var selector = HistoryReference.Parse("@-2");
        var picked = selector(Sample());
        Assert.NotNull(picked);
        Assert.Equal("snapshot-20260201T000000Z-bbbbbbbb", picked!.Id);
    }

    [Fact]
    public void Parse_AtMinusOutOfRange_ReturnsNull()
    {
        var selector = HistoryReference.Parse("@-99");
        var picked = selector(Sample());
        Assert.Null(picked);
    }

    [Fact]
    public void Parse_AtMinusZero_Throws()
    {
        Assert.Throws<FormatException>(() => HistoryReference.Parse("@-0"));
    }

    [Fact]
    public void Parse_AtMinusNegative_Throws()
    {
        Assert.Throws<FormatException>(() => HistoryReference.Parse("@--1"));
    }

    [Fact]
    public void Parse_NonNumericAtMinus_Throws()
    {
        Assert.Throws<FormatException>(() => HistoryReference.Parse("@-abc"));
    }

    [Fact]
    public void Parse_IsoTimestamp_PicksClosestNotAfter()
    {
        var selector = HistoryReference.Parse("2026-02-15T12:00:00Z");
        var picked = selector(Sample());
        Assert.NotNull(picked);
        Assert.Equal("snapshot-20260201T000000Z-bbbbbbbb", picked!.Id);
    }

    [Fact]
    public void Parse_IsoTimestampBeforeAllEntries_ReturnsNull()
    {
        var selector = HistoryReference.Parse("2025-01-01T00:00:00Z");
        var picked = selector(Sample());
        Assert.Null(picked);
    }

    [Fact]
    public void Parse_IsoTimestampExactMatch_ReturnsExact()
    {
        var selector = HistoryReference.Parse("2026-02-01T00:00:00Z");
        var picked = selector(Sample());
        Assert.NotNull(picked);
        Assert.Equal("snapshot-20260201T000000Z-bbbbbbbb", picked!.Id);
    }

    [Fact]
    public void Parse_FutureTimestamp_StillReturnsLatestAvailable()
    {
        var selector = HistoryReference.Parse("2030-01-01T00:00:00Z");
        var picked = selector(Sample());
        Assert.NotNull(picked);
        Assert.Equal("snapshot-20260301T000000Z-cccccccc", picked!.Id);
    }

    [Fact]
    public void Parse_DurationSevenDays_ResolvesAgainstNow()
    {
        var now = new DateTimeOffset(2026, 3, 8, 0, 0, 0, TimeSpan.Zero);
        var selector = HistoryReference.Parse("7d", now);
        var picked = selector(Sample());
        Assert.NotNull(picked);
        // now - 7d = 2026-03-01; should pick the 2026-03-01 entry exactly.
        Assert.Equal("snapshot-20260301T000000Z-cccccccc", picked!.Id);
    }

    [Fact]
    public void Parse_DurationHours_Works()
    {
        var now = new DateTimeOffset(2026, 2, 1, 12, 0, 0, TimeSpan.Zero);
        var selector = HistoryReference.Parse("12h", now);
        var picked = selector(Sample());
        Assert.NotNull(picked);
        Assert.Equal("snapshot-20260201T000000Z-bbbbbbbb", picked!.Id);
    }

    [Fact]
    public void Parse_NegativeDuration_Throws()
    {
        Assert.Throws<FormatException>(() => HistoryReference.Parse("-7d"));
    }

    [Fact]
    public void Parse_MalformedInputs_Throw()
    {
        Assert.Throws<FormatException>(() => HistoryReference.Parse(""));
        Assert.Throws<FormatException>(() => HistoryReference.Parse("   "));
        Assert.Throws<FormatException>(() => HistoryReference.Parse("garbage"));
        Assert.Throws<FormatException>(() => HistoryReference.Parse("2026-99-99T00:00:00Z"));
        Assert.Throws<FormatException>(() => HistoryReference.Parse("d"));
        Assert.Throws<FormatException>(() => HistoryReference.Parse("7"));
    }

    [Fact]
    public void Parse_NullInput_Throws()
    {
        Assert.Throws<FormatException>(() => HistoryReference.Parse(null!));
    }

    [Fact]
    public void Parse_EmptyList_ReturnsNullForAnyForm()
    {
        var empty = new List<SnapshotMetadata>();
        Assert.Null(HistoryReference.Parse("@-1")(empty));
        Assert.Null(HistoryReference.Parse("2026-04-27T00:00:00Z")(empty));
        Assert.Null(HistoryReference.Parse("7d", DateTimeOffset.UtcNow)(empty));
    }
}
