using DiffViewer.Utility;
using FluentAssertions;
using Xunit;

namespace DiffViewer.Tests.Utility;

public class FileListFilterTests
{
    [Theory]
    [InlineData("src\\foo\\bar.cs", "foo", true)]
    [InlineData("src\\foo\\bar.cs", "FOO", true)]
    [InlineData("src\\Foo\\bar.cs", "foo", true)]
    [InlineData("src\\foo\\bar.cs", "qux", false)]
    [InlineData("src\\foo\\bar.cs", "", true)]
    [InlineData("src\\foo\\bar.cs", null, true)]
    [InlineData("", "foo", false)]
    [InlineData(null, "foo", false)]
    public void Matches_HandlesCaseInsensitive_AndEmptyOrNullArgs(
        string? path, string? query, bool expected)
    {
        FileListFilter.Matches(path, query).Should().Be(expected);
    }

    [Theory]
    [InlineData("src\\foo\\bar.cs", "foo/bar")]
    [InlineData("src\\foo\\bar.cs", "foo\\bar")]
    [InlineData("src\\foo\\bar.cs", "src/foo/bar.cs")]
    [InlineData("src\\foo\\bar.cs", "SRC\\FOO/bar.CS")]
    [InlineData("src/foo/bar.cs", "foo\\bar")]
    public void Matches_NormalizesSlashes_InBothQueryAndPath(string path, string query)
    {
        FileListFilter.Matches(path, query).Should().BeTrue(
            because: "the filter must be slash-insensitive so users can type either separator");
    }

    [Fact]
    public void Normalize_TurnsBackslashesIntoForwardSlashes()
    {
        FileListFilter.Normalize("src\\foo\\bar.cs").Should().Be("src/foo/bar.cs");
        FileListFilter.Normalize("src/foo/bar.cs").Should().Be("src/foo/bar.cs");
        FileListFilter.Normalize("").Should().BeEmpty();
        FileListFilter.Normalize(null).Should().BeEmpty();
    }

    [Theory]
    [InlineData("src/foo/bar.cs", "foo/bar", true)]
    [InlineData("src/foo/bar.cs", "foo\\bar", true)]
    [InlineData("src/foo/bar.cs", "qux", false)]
    [InlineData("src/foo/bar.cs", "", true)]
    [InlineData("src/foo/bar.cs", null, true)]
    [InlineData("", "foo", false)]
    public void MatchesNormalized_SkipsPathNormalization(
        string normalizedPath, string? query, bool expected)
    {
        FileListFilter.MatchesNormalized(normalizedPath, query).Should().Be(expected);
    }
}
