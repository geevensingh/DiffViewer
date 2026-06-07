using System;
using DiffViewer.Utility;
using FluentAssertions;
using Xunit;

namespace DiffViewer.Tests.Utility;

public class PathListEditorTests
{
    private const string Dir = @"C:\Users\me\AppData\Local\DiffViewer\current";

    // ---- Contains --------------------------------------------------------

    [Fact]
    public void Contains_NullOrEmptyRaw_IsFalse()
    {
        PathListEditor.Contains(null, Dir).Should().BeFalse();
        PathListEditor.Contains("", Dir).Should().BeFalse();
    }

    [Fact]
    public void Contains_ExactSegment_IsTrue()
        => PathListEditor.Contains($@"C:\Windows;{Dir};C:\Tools", Dir).Should().BeTrue();

    [Fact]
    public void Contains_IsCaseInsensitive()
        => PathListEditor.Contains(Dir.ToUpperInvariant(), Dir).Should().BeTrue();

    [Fact]
    public void Contains_TrailingSeparatorMismatch_StillMatches()
    {
        PathListEditor.Contains($@"{Dir}\", Dir).Should().BeTrue();
        PathListEditor.Contains(Dir, $@"{Dir}\").Should().BeTrue();
    }

    [Fact]
    public void Contains_SubstringOfAnotherSegment_DoesNotMatch()
    {
        // "...\current" must not match "...\current2".
        PathListEditor.Contains($@"{Dir}2", Dir).Should().BeFalse();
        PathListEditor.Contains($@"C:\a\currentX;C:\b", @"C:\a\current").Should().BeFalse();
    }

    [Fact]
    public void Contains_EnvironmentVariableForm_MatchesExpandedTarget()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var expanded = System.IO.Path.Combine(localAppData, "DiffViewer", "current");
        var rawWithVar = @"%LOCALAPPDATA%\DiffViewer\current";

        PathListEditor.Contains(rawWithVar, expanded).Should().BeTrue();
    }

    // ---- Add -------------------------------------------------------------

    [Fact]
    public void Add_NullRaw_ReturnsJustDirectory()
        => PathListEditor.Add(null, Dir).Should().Be(Dir);

    [Fact]
    public void Add_EmptyRaw_ReturnsJustDirectory()
        => PathListEditor.Add("", Dir).Should().Be(Dir);

    [Fact]
    public void Add_WhitespaceRaw_ReturnsJustDirectory()
        => PathListEditor.Add("   ", Dir).Should().Be(Dir);

    [Fact]
    public void Add_AppendsWithSingleSeparator()
        => PathListEditor.Add(@"C:\Windows;C:\Tools", Dir).Should().Be($@"C:\Windows;C:\Tools;{Dir}");

    [Fact]
    public void Add_RawEndingWithSeparator_DoesNotDoubleUp()
        => PathListEditor.Add(@"C:\Windows;", Dir).Should().Be($@"C:\Windows;{Dir}");

    [Fact]
    public void Add_AlreadyPresent_ReturnsNull()
        => PathListEditor.Add($@"C:\Windows;{Dir}", Dir).Should().BeNull();

    [Fact]
    public void Add_AlreadyPresentWithTrailingSlash_ReturnsNull()
        => PathListEditor.Add($@"C:\Windows;{Dir}\", Dir).Should().BeNull();

    [Fact]
    public void Add_BlankDirectory_ReturnsNull()
    {
        PathListEditor.Add(@"C:\Windows", "").Should().BeNull();
        PathListEditor.Add(@"C:\Windows", "   ").Should().BeNull();
    }

    [Fact]
    public void Add_TrimsTrailingSeparatorOnStoredEntry()
        => PathListEditor.Add(@"C:\Windows", $@"{Dir}\").Should().Be($@"C:\Windows;{Dir}");

    // ---- Remove ----------------------------------------------------------

    [Fact]
    public void Remove_NullOrEmptyRaw_ReturnsNull()
    {
        PathListEditor.Remove(null, Dir).Should().BeNull();
        PathListEditor.Remove("", Dir).Should().BeNull();
    }

    [Fact]
    public void Remove_NotPresent_ReturnsNull()
        => PathListEditor.Remove(@"C:\Windows;C:\Tools", Dir).Should().BeNull();

    [Fact]
    public void Remove_FromMiddle_PreservesOtherSegments()
        => PathListEditor.Remove($@"C:\Windows;{Dir};C:\Tools", Dir).Should().Be(@"C:\Windows;C:\Tools");

    [Fact]
    public void Remove_FromEnd()
        => PathListEditor.Remove($@"C:\Windows;{Dir}", Dir).Should().Be(@"C:\Windows");

    [Fact]
    public void Remove_FromStart()
        => PathListEditor.Remove($@"{Dir};C:\Windows", Dir).Should().Be(@"C:\Windows");

    [Fact]
    public void Remove_OnlyEntry_ReturnsEmptyStringNotNull()
        => PathListEditor.Remove(Dir, Dir).Should().Be(string.Empty);

    [Fact]
    public void Remove_DuplicateEntries_RemovesAll()
        => PathListEditor.Remove($@"{Dir};C:\Windows;{Dir}\", Dir).Should().Be(@"C:\Windows");

    [Fact]
    public void Remove_PreservesInteriorEmptySegmentsVerbatim()
        => PathListEditor.Remove($@"C:\Windows;;{Dir};C:\Tools", Dir).Should().Be(@"C:\Windows;;C:\Tools");

    [Fact]
    public void Remove_EnvironmentVariableForm_IsRemoved()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var expanded = System.IO.Path.Combine(localAppData, "DiffViewer", "current");

        PathListEditor.Remove($@"C:\Windows;%LOCALAPPDATA%\DiffViewer\current", expanded)
            .Should().Be(@"C:\Windows");
    }

    [Fact]
    public void Remove_IsCaseInsensitive()
        => PathListEditor.Remove($@"C:\Windows;{Dir.ToUpperInvariant()}", Dir).Should().Be(@"C:\Windows");
}
