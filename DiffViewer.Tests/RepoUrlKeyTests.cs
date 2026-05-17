using DiffViewer.Models;
using FluentAssertions;
using Xunit;

namespace DiffViewer.Tests;

public sealed class RepoUrlKeyTests
{
    [Fact]
    public void Constructor_PreservesInputCasing_UnlikeFactory()
    {
        // The positional constructor is permissive (no normalization) so
        // callers that already have canonical values don't pay for an
        // extra ToLower. From(...) is the documented entry point for
        // unnormalized input.
        var key = new RepoUrlKey("GitHub.com", "Owner", "Repo");
        key.Host.Should().Be("GitHub.com");
        key.Owner.Should().Be("Owner");
        key.Repo.Should().Be("Repo");
    }

    [Theory]
    [InlineData("github.com", "geevensingh", "jotjson")]
    [InlineData("GITHUB.COM", "GEEVENSINGH", "JOTJSON")]
    [InlineData("GitHub.COM", "GeevenSingh", "JotJson")]
    public void From_NormalizesAllSegmentsToLowercase(string host, string owner, string repo)
    {
        var key = RepoUrlKey.From(host, owner, repo);

        key.Host.Should().Be("github.com");
        key.Owner.Should().Be("geevensingh");
        key.Repo.Should().Be("jotjson");
    }

    [Fact]
    public void From_NormalizedKeysAreEqual_IrrespectiveOfInputCasing()
    {
        var a = RepoUrlKey.From("github.com", "geevensingh", "jotjson");
        var b = RepoUrlKey.From("GITHUB.COM", "GeevenSingh", "JotJSON");

        a.Should().Be(b);
        a.GetHashCode().Should().Be(b.GetHashCode());
    }

    [Theory]
    [InlineData(null, "owner", "repo")]
    [InlineData("", "owner", "repo")]
    [InlineData("  ", "owner", "repo")]
    [InlineData("github.com", null, "repo")]
    [InlineData("github.com", "owner", null)]
    [InlineData("github.com", "", "repo")]
    [InlineData("github.com", "owner", "")]
    public void From_RejectsNullOrEmptySegments(string? host, string? owner, string? repo)
    {
        var act = () => RepoUrlKey.From(host!, owner!, repo!);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void From_PullRequestRef_ReusesNormalizedFields()
    {
        // PullRequestRef.TryParse already lowercases, so the From(PR)
        // factory just lifts those fields into a key — no further
        // normalization needed.
        PullRequestRef.TryParse(
            "https://github.com/GeevenSingh/JotJson/pull/268", out var pr, out _)
            .Should().BeTrue();

        var key = RepoUrlKey.From(pr!);
        key.Host.Should().Be("github.com");
        key.Owner.Should().Be("geevensingh");
        key.Repo.Should().Be("jotjson");
    }

    [Fact]
    public void From_PullRequestRef_Null_Throws()
    {
        var act = () => RepoUrlKey.From((PullRequestRef)null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ToWireString_UsesPipeSeparator()
    {
        var key = RepoUrlKey.From("github.com", "geevensingh", "jotjson");
        key.ToWireString().Should().Be("github.com|geevensingh|jotjson");
    }

    [Fact]
    public void TryParseWire_RoundTripsCanonicalForm()
    {
        var original = RepoUrlKey.From("github.com", "geevensingh", "jotjson");
        var parsed = RepoUrlKey.TryParseWire(original.ToWireString());

        parsed.Should().NotBeNull();
        parsed.Should().Be(original);
    }

    [Fact]
    public void TryParseWire_NormalizesCasingOnReadBack()
    {
        // Even if a hand-edited file used uppercase, reading should
        // produce a key equal to the lowercase canonical form.
        var parsed = RepoUrlKey.TryParseWire("GitHub.com|GeevenSingh|JotJson");

        parsed.Should().Be(RepoUrlKey.From("github.com", "geevensingh", "jotjson"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("only-one")]
    [InlineData("two|parts")]
    [InlineData("too|many|parts|here")]
    [InlineData("|owner|repo")]
    [InlineData("host||repo")]
    [InlineData("host|owner|")]
    public void TryParseWire_RejectsMalformed(string? wire)
    {
        RepoUrlKey.TryParseWire(wire).Should().BeNull();
    }
}
