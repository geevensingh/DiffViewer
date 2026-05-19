using DiffViewer.Models;
using DiffViewer.Services;
using FluentAssertions;
using Xunit;

namespace DiffViewer.Tests.Services;

/// <summary>
/// Tests for the named-flag CLI grammar
/// (<c>--repo &lt;p&gt; --left &lt;ref|WORKING&gt; --right &lt;ref|WORKING&gt; [--file &lt;p&gt;]</c>),
/// added for <c>git difftool</c> integration (issue #5). The legacy
/// positional grammar is covered by <see cref="CommandLineParserTests"/>;
/// this class only exercises the flag-form branch.
/// </summary>
public class CommandLineParserFlagFormTests
{
    private const string Cwd = @"C:\Repos\foo";
    private const string Repo = @"C:\Repos\bar";

    private sealed class StubEnv : ICommandLineEnvironment
    {
        public string CurrentDirectory { get; init; } = Cwd;
        public HashSet<string> ExistingPaths { get; init; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> GitRepos { get; init; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<(string repo, string commit), bool> Commits { get; init; }
            = new(new TupleIgnoreCase());
        public Dictionary<string, string> DiscoveredRoots { get; init; }
            = new(StringComparer.OrdinalIgnoreCase);

        public bool PathExists(string path) => ExistingPaths.Contains(path);
        public bool IsGitRepository(string path) => GitRepos.Contains(path);
        public bool TryResolveCommitIsh(string repoPath, string commitIsh)
            => Commits.TryGetValue((repoPath, commitIsh), out var ok) && ok;
        public string? TryDiscoverRepoRoot(string path)
            => DiscoveredRoots.TryGetValue(path, out var root) ? root : null;

        private sealed class TupleIgnoreCase : IEqualityComparer<(string repo, string commit)>
        {
            public bool Equals((string repo, string commit) x, (string repo, string commit) y)
                => string.Equals(x.repo, y.repo, StringComparison.OrdinalIgnoreCase)
                   && string.Equals(x.commit, y.commit, StringComparison.Ordinal);

            public int GetHashCode((string repo, string commit) obj)
                => HashCode.Combine(obj.repo.ToLowerInvariant(), obj.commit);
        }
    }

    private static StubEnv RepoEnv() => new()
    {
        ExistingPaths = { Repo },
        GitRepos = { Repo },
    };

    [Fact]
    public void Flags_CommitVsCommit_Resolves()
    {
        var parser = new CommandLineParser();
        var env = RepoEnv();
        env.Commits[(Repo, "main")] = true;
        env.Commits[(Repo, "HEAD~3")] = true;

        var result = parser.Parse(
            new[] { "--repo", Repo, "--left", "HEAD~3", "--right", "main" }, env);

        result.IsSuccess.Should().BeTrue();
        result.Parsed!.RepoPath.Should().Be(Repo);
        result.Parsed.Left.Should().BeOfType<DiffSide.CommitIsh>()
            .Which.Reference.Should().Be("HEAD~3");
        result.Parsed.Right.Should().BeOfType<DiffSide.CommitIsh>()
            .Which.Reference.Should().Be("main");
        result.Parsed.InitialFile.Should().BeNull();
    }

    [Fact]
    public void Flags_WorkingSentinel_RightSide_MapsToWorkingTree()
    {
        var parser = new CommandLineParser();
        var env = RepoEnv();
        env.Commits[(Repo, "HEAD")] = true;

        var result = parser.Parse(
            new[] { "--repo", Repo, "--left", "HEAD", "--right", "WORKING" }, env);

        result.IsSuccess.Should().BeTrue();
        result.Parsed!.Left.Should().BeOfType<DiffSide.CommitIsh>()
            .Which.Reference.Should().Be("HEAD");
        result.Parsed.Right.Should().BeOfType<DiffSide.WorkingTree>();
    }

    [Fact]
    public void Flags_WorkingSentinel_LeftSide_MapsToWorkingTree()
    {
        var parser = new CommandLineParser();
        var env = RepoEnv();
        env.Commits[(Repo, "main")] = true;

        var result = parser.Parse(
            new[] { "--repo", Repo, "--left", "WORKING", "--right", "main" }, env);

        result.IsSuccess.Should().BeTrue();
        result.Parsed!.Left.Should().BeOfType<DiffSide.WorkingTree>();
        result.Parsed.Right.Should().BeOfType<DiffSide.CommitIsh>()
            .Which.Reference.Should().Be("main");
    }

    [Theory]
    [InlineData("WORKING")]
    [InlineData("working")]
    [InlineData("Working")]
    [InlineData("WoRkInG")]
    public void Flags_WorkingSentinel_IsCaseInsensitive(string sentinel)
    {
        var parser = new CommandLineParser();
        var env = RepoEnv();
        env.Commits[(Repo, "HEAD")] = true;

        var result = parser.Parse(
            new[] { "--repo", Repo, "--left", "HEAD", "--right", sentinel }, env);

        result.IsSuccess.Should().BeTrue();
        result.Parsed!.Right.Should().BeOfType<DiffSide.WorkingTree>();
    }

    [Fact]
    public void Flags_FileFlag_PopulatesInitialFile_WithNormalizedSeparators()
    {
        var parser = new CommandLineParser();
        var env = RepoEnv();
        env.Commits[(Repo, "HEAD")] = true;

        var result = parser.Parse(
            new[] { "--repo", Repo, "--left", "HEAD", "--right", "WORKING",
                    "--file", "src/foo/bar.cs" }, env);

        result.IsSuccess.Should().BeTrue();
        result.Parsed!.InitialFile.Should().Be(@"src\foo\bar.cs");
    }

    [Fact]
    public void Flags_FileFlag_AcceptsBackslashSeparators()
    {
        var parser = new CommandLineParser();
        var env = RepoEnv();
        env.Commits[(Repo, "HEAD")] = true;

        var result = parser.Parse(
            new[] { "--repo", Repo, "--left", "HEAD", "--right", "WORKING",
                    "--file", @"src\foo\bar.cs" }, env);

        result.IsSuccess.Should().BeTrue();
        result.Parsed!.InitialFile.Should().Be(@"src\foo\bar.cs");
    }

    [Fact]
    public void Flags_FileFlag_LeadingSeparatorTrimmed()
    {
        var parser = new CommandLineParser();
        var env = RepoEnv();
        env.Commits[(Repo, "HEAD")] = true;

        var result = parser.Parse(
            new[] { "--repo", Repo, "--left", "HEAD", "--right", "WORKING",
                    "--file", "/src/foo.cs" }, env);

        result.IsSuccess.Should().BeTrue();
        result.Parsed!.InitialFile.Should().Be(@"src\foo.cs");
    }

    [Fact]
    public void Flags_FlagsInAnyOrder()
    {
        var parser = new CommandLineParser();
        var env = RepoEnv();
        env.Commits[(Repo, "abc")] = true;
        env.Commits[(Repo, "def")] = true;

        var result = parser.Parse(
            new[] { "--right", "def", "--file", "x.txt", "--left", "abc", "--repo", Repo }, env);

        result.IsSuccess.Should().BeTrue();
        result.Parsed!.RepoPath.Should().Be(Repo);
        result.Parsed.Left.Should().BeOfType<DiffSide.CommitIsh>()
            .Which.Reference.Should().Be("abc");
        result.Parsed.Right.Should().BeOfType<DiffSide.CommitIsh>()
            .Which.Reference.Should().Be("def");
        result.Parsed.InitialFile.Should().Be("x.txt");
    }

    [Fact]
    public void Flags_MissingRepo_Fails()
    {
        var parser = new CommandLineParser();
        var env = RepoEnv();

        var result = parser.Parse(
            new[] { "--left", "HEAD", "--right", "WORKING" }, env);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Kind.Should().Be(CommandLineErrorKind.MissingRequiredFlag);
        result.Error.Message.Should().Contain("--repo");
    }

    [Fact]
    public void Flags_MissingLeft_Fails()
    {
        var parser = new CommandLineParser();
        var env = RepoEnv();

        var result = parser.Parse(
            new[] { "--repo", Repo, "--right", "WORKING" }, env);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Kind.Should().Be(CommandLineErrorKind.MissingRequiredFlag);
        result.Error.Message.Should().Contain("--left");
    }

    [Fact]
    public void Flags_MissingRight_Fails()
    {
        var parser = new CommandLineParser();
        var env = RepoEnv();

        var result = parser.Parse(
            new[] { "--repo", Repo, "--left", "HEAD" }, env);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Kind.Should().Be(CommandLineErrorKind.MissingRequiredFlag);
        result.Error.Message.Should().Contain("--right");
    }

    [Fact]
    public void Flags_FlagAtEndOfArgv_MissingValue_Fails()
    {
        var parser = new CommandLineParser();
        var env = RepoEnv();

        var result = parser.Parse(
            new[] { "--repo", Repo, "--left", "HEAD", "--right" }, env);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Kind.Should().Be(CommandLineErrorKind.MissingFlagValue);
        result.Error.Message.Should().Contain("--right");
    }

    [Fact]
    public void Flags_FlagFollowedByAnotherFlag_MissingValue_Fails()
    {
        var parser = new CommandLineParser();
        var env = RepoEnv();

        // --left's value is "--right", which we treat as the next flag rather
        // than a value (a value-less --left is more likely a typo than a ref
        // someone literally named "--right").
        var result = parser.Parse(
            new[] { "--repo", Repo, "--left", "--right", "WORKING" }, env);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Kind.Should().Be(CommandLineErrorKind.MissingFlagValue);
        result.Error.Message.Should().Contain("--left");
    }

    [Fact]
    public void Flags_UnknownFlag_Fails()
    {
        var parser = new CommandLineParser();
        var env = RepoEnv();

        var result = parser.Parse(
            new[] { "--repo", Repo, "--left", "HEAD", "--right", "WORKING", "--bogus", "x" }, env);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Kind.Should().Be(CommandLineErrorKind.UnknownFlag);
        result.Error.Message.Should().Contain("--bogus");
    }

    [Fact]
    public void Flags_MixedPositionalAndFlag_Fails()
    {
        var parser = new CommandLineParser();
        var env = RepoEnv();

        // Once the parser enters flag-form mode (first arg starts with --), a
        // bare positional later in argv is rejected loudly. Mixing the two
        // grammars is a category error.
        var result = parser.Parse(
            new[] { "--repo", Repo, "--left", "HEAD", "--right", "WORKING", "stray" }, env);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Kind.Should().Be(CommandLineErrorKind.UnexpectedPositionalArgument);
        result.Error.Message.Should().Contain("stray");
    }

    [Fact]
    public void Flags_RepoDoesNotExist_Fails()
    {
        var parser = new CommandLineParser();
        var env = new StubEnv(); // no paths registered

        var result = parser.Parse(
            new[] { "--repo", Repo, "--left", "HEAD", "--right", "WORKING" }, env);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Kind.Should().Be(CommandLineErrorKind.PathDoesNotExist);
    }

    [Fact]
    public void Flags_RepoExistsButNotARepo_Fails()
    {
        var parser = new CommandLineParser();
        var env = new StubEnv
        {
            ExistingPaths = { Repo },
            // Not in GitRepos and not in DiscoveredRoots → fails.
        };

        var result = parser.Parse(
            new[] { "--repo", Repo, "--left", "HEAD", "--right", "WORKING" }, env);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Kind.Should().Be(CommandLineErrorKind.NotAGitRepository);
    }

    [Fact]
    public void Flags_RepoIsSubdirOfRepo_DiscoversAndUsesRoot()
    {
        const string subdir = @"C:\Repos\bar\src";
        var parser = new CommandLineParser();
        var env = new StubEnv
        {
            ExistingPaths = { subdir },
            GitRepos = { Repo },
            DiscoveredRoots = { [subdir] = Repo },
            Commits = { [(Repo, "HEAD")] = true },
        };

        var result = parser.Parse(
            new[] { "--repo", subdir, "--left", "HEAD", "--right", "WORKING" }, env);

        result.IsSuccess.Should().BeTrue();
        result.Parsed!.RepoPath.Should().Be(Repo);
    }

    [Fact]
    public void Flags_UnresolvableLeftCommit_Fails()
    {
        var parser = new CommandLineParser();
        var env = RepoEnv();
        // "ghost" deliberately unregistered.

        var result = parser.Parse(
            new[] { "--repo", Repo, "--left", "ghost", "--right", "WORKING" }, env);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Kind.Should().Be(CommandLineErrorKind.UnknownCommitIsh);
        result.Error.Message.Should().Contain("ghost");
    }

    [Fact]
    public void Flags_UnresolvableRightCommit_Fails()
    {
        var parser = new CommandLineParser();
        var env = RepoEnv();
        env.Commits[(Repo, "main")] = true;

        var result = parser.Parse(
            new[] { "--repo", Repo, "--left", "main", "--right", "ghost" }, env);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Kind.Should().Be(CommandLineErrorKind.UnknownCommitIsh);
        result.Error.Message.Should().Contain("ghost");
    }

    [Fact]
    public void Flags_BothWorking_Allowed()
    {
        // Edge case: both sides are WORKING. The parser doesn't reject this —
        // it's a no-op diff at the model layer, not a parse error. Surfacing
        // it as a parse failure would force the parser to know what the
        // engine considers degenerate, which it shouldn't.
        var parser = new CommandLineParser();
        var env = RepoEnv();

        var result = parser.Parse(
            new[] { "--repo", Repo, "--left", "WORKING", "--right", "WORKING" }, env);

        result.IsSuccess.Should().BeTrue();
        result.Parsed!.Left.Should().BeOfType<DiffSide.WorkingTree>();
        result.Parsed.Right.Should().BeOfType<DiffSide.WorkingTree>();
    }

    [Fact]
    public void ParseLaunch_FlagForm_ReturnsLocalVariant()
    {
        // The launch-aware wrapper should route flag-form args through the
        // local parse path (not the PR-URL branch), and surface the result
        // as a Local variant.
        var parser = new CommandLineParser();
        var env = RepoEnv();
        env.Commits[(Repo, "HEAD")] = true;

        var plan = parser.ParseLaunch(
            new[] { "--repo", Repo, "--left", "HEAD", "--right", "WORKING" }, env);

        plan.IsLocal.Should().BeTrue();
        plan.IsPullRequest.Should().BeFalse();
        plan.IsError.Should().BeFalse();
        plan.Local!.RepoPath.Should().Be(Repo);
    }

    [Fact]
    public void ParseLaunch_FlagFormError_ReturnsErrorVariant()
    {
        var parser = new CommandLineParser();
        var env = RepoEnv();

        var plan = parser.ParseLaunch(new[] { "--repo", Repo }, env);

        plan.IsError.Should().BeTrue();
        plan.Error!.Kind.Should().Be(CommandLineErrorKind.MissingRequiredFlag);
    }
}
