using DiffViewer.Models;

namespace DiffViewer.Services;

/// <summary>
/// Default <see cref="ICommandLineParser"/>. Implements the disambiguation
/// table from the plan:
/// <list type="bullet">
///   <item><c>(no args)</c>            → working tree vs HEAD in cwd</item>
///   <item><c>repoPath</c>             → working tree vs HEAD in repoPath</item>
///   <item><c>commit</c>               → working tree vs commit in cwd</item>
///   <item><c>repoPath commit</c>      → working tree vs commit in repoPath</item>
///   <item><c>commitA commitB</c>      → commitA vs commitB in cwd</item>
///   <item><c>repoPath commitA commitB</c> → commitA vs commitB in repoPath</item>
/// </list>
/// Disambiguation: an argument is treated as a repo path iff
/// <see cref="ICommandLineEnvironment.PathExists"/> AND
/// <see cref="ICommandLineEnvironment.IsGitRepository"/>; otherwise it is
/// resolved as a commit-ish.
///
/// <para>When the resolved repo path turns out to be a subdirectory of a
/// repo (or the current working directory is one), the parser falls back to
/// <see cref="ICommandLineEnvironment.TryDiscoverRepoRoot"/> so the app can
/// be launched from anywhere inside a worktree and still load the whole
/// repo.</para>
/// </summary>
public sealed class CommandLineParser : ICommandLineParser
{
    /// <inheritdoc />
    public CommandLineLaunchPlan ParseLaunch(IReadOnlyList<string> args, ICommandLineEnvironment env)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(env);

        // PR-URL launches: a single argument that parses as a GitHub PR URL
        // is routed to the PR resolver, not through ParsedCommandLine.
        if (args.Count == 1 && PullRequestRef.TryParse(args[0], out var pr, out _))
        {
            return CommandLineLaunchPlan.FromPullRequest(pr);
        }

        var local = Parse(args, env);
        return local.IsSuccess
            ? CommandLineLaunchPlan.FromLocal(local.Parsed!)
            : CommandLineLaunchPlan.Failure(local.Error!.Kind, local.Error.Message);
    }

    public CommandLineParseResult Parse(IReadOnlyList<string> args, ICommandLineEnvironment env)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(env);

        // Flag-form grammar: --repo <p> --left <ref|WORKING> --right <ref|WORKING> [--file <p>]
        // Branches off iff the first arg starts with "--". Positional grammar
        // (the historical form) handles everything else, including args that
        // start with a single "-" (rejected as UnknownFlag below).
        if (args.Count > 0 && args[0].StartsWith("--", StringComparison.Ordinal))
        {
            return ParseFlagForm(args, env);
        }

        // Reject unknown switches early — every arg starting with "-" is a flag we don't (yet) support.
        for (int i = 0; i < args.Count; i++)
        {
            if (args[i].Length > 0 && args[i][0] == '-')
            {
                return CommandLineParseResult.Failure(
                    CommandLineErrorKind.UnknownFlag,
                    $"Unknown flag: {args[i]}");
            }
        }

        if (args.Count > 3)
        {
            return CommandLineParseResult.Failure(
                CommandLineErrorKind.TooManyArguments,
                $"Too many arguments: expected 0-3, got {args.Count}");
        }

        // Decide whether the first arg is a repo path. We probe filesystem first
        // (the plan: "checking the path on disk first, then falling back to commit-ish").
        string repoPath = env.CurrentDirectory;
        int sideArgsStart = 0;

        if (args.Count > 0 && LooksLikeRepoPath(args[0], env))
        {
            repoPath = args[0];
            sideArgsStart = 1;
        }
        else if (args.Count > 0 && args[0].Length > 0 && IsLikelyPath(args[0]))
        {
            // Argument looks like a path (contains a separator, drive letter, leading dot)
            // but doesn't exist or isn't a repo on its own. We still allow it iff it's a
            // subdirectory of a repo — discovery resolves that. If the path doesn't even
            // exist, fail loudly; "..\foo" really meant a path, not a ref.
            if (!env.PathExists(args[0]))
            {
                return CommandLineParseResult.Failure(
                    CommandLineErrorKind.PathDoesNotExist,
                    $"Path does not exist: {args[0]}");
            }

            repoPath = args[0];
            sideArgsStart = 1;
        }

        // Make sure the resolved repo path is, or sits inside, a git repo. The
        // discovery fallback handles "launched from a subdirectory of a repo".
        if (!env.IsGitRepository(repoPath))
        {
            var discovered = env.TryDiscoverRepoRoot(repoPath);
            if (discovered is null)
            {
                return CommandLineParseResult.Failure(
                    CommandLineErrorKind.NotAGitRepository,
                    $"Not a git repository: {repoPath}");
            }

            repoPath = discovered;
        }

        int sideCount = args.Count - sideArgsStart;
        DiffSide left;
        DiffSide right;

        switch (sideCount)
        {
            case 0:
                // Working tree vs HEAD.
                left = new DiffSide.CommitIsh("HEAD");
                right = new DiffSide.WorkingTree();
                break;

            case 1:
            {
                string commit = args[sideArgsStart];
                if (!env.TryResolveCommitIsh(repoPath, commit))
                {
                    return CommandLineParseResult.Failure(
                        CommandLineErrorKind.UnknownCommitIsh,
                        $"Cannot resolve `{commit}` in repo {repoPath}");
                }

                left = new DiffSide.CommitIsh(commit);
                right = new DiffSide.WorkingTree();
                break;
            }

            case 2:
            {
                string commitA = args[sideArgsStart];
                string commitB = args[sideArgsStart + 1];

                if (!env.TryResolveCommitIsh(repoPath, commitA))
                {
                    return CommandLineParseResult.Failure(
                        CommandLineErrorKind.UnknownCommitIsh,
                        $"Cannot resolve `{commitA}` in repo {repoPath}");
                }

                if (!env.TryResolveCommitIsh(repoPath, commitB))
                {
                    return CommandLineParseResult.Failure(
                        CommandLineErrorKind.UnknownCommitIsh,
                        $"Cannot resolve `{commitB}` in repo {repoPath}");
                }

                left = new DiffSide.CommitIsh(commitA);
                right = new DiffSide.CommitIsh(commitB);
                break;
            }

            default:
                // Unreachable — guarded above by the 0–3 arg cap.
                return CommandLineParseResult.Failure(
                    CommandLineErrorKind.TooManyArguments,
                    $"Too many side arguments: {sideCount}");
        }

        return CommandLineParseResult.Success(new ParsedCommandLine(repoPath, left, right));
    }

    private static bool LooksLikeRepoPath(string arg, ICommandLineEnvironment env) =>
        env.PathExists(arg) && env.IsGitRepository(arg);

    /// <summary>
    /// Sentinel that <c>--left</c> / <c>--right</c> accept to mean
    /// "working tree" (i.e. <see cref="DiffSide.WorkingTree"/>).
    /// Matched case-insensitively.
    /// </summary>
    private const string WorkingTreeSentinel = "WORKING";

    /// <summary>
    /// Flag-form parser: <c>--repo &lt;p&gt; --left &lt;ref|WORKING&gt;
    /// --right &lt;ref|WORKING&gt; [--file &lt;repo-relative-path&gt;]</c>.
    /// </summary>
    /// <remarks>
    /// Designed for non-interactive launches (e.g. <c>git difftool</c>): every
    /// side is named, nothing is inferred from cwd, and mixing positional
    /// arguments with flag arguments is rejected up front to avoid the
    /// "is this a value or a stray ref" ambiguity that haunts the positional
    /// grammar. The flag form still goes through the same repo-discovery and
    /// commit-ish resolution as the positional form, so failure modes match.
    /// </remarks>
    private static CommandLineParseResult ParseFlagForm(IReadOnlyList<string> args, ICommandLineEnvironment env)
    {
        string? repoArg = null;
        string? leftArg = null;
        string? rightArg = null;
        string? fileArg = null;

        for (int i = 0; i < args.Count; i++)
        {
            var a = args[i];
            if (!a.StartsWith("--", StringComparison.Ordinal))
            {
                return CommandLineParseResult.Failure(
                    CommandLineErrorKind.UnexpectedPositionalArgument,
                    $"Unexpected positional argument in flag-form parse: `{a}`. " +
                    "Mix --repo / --left / --right / --file only; do not combine flag and positional forms.");
            }

            // All four known flags take a value. Peek ahead once; reject if the
            // next token is missing or is itself another flag.
            if (a is "--repo" or "--left" or "--right" or "--file")
            {
                if (i + 1 >= args.Count || args[i + 1].StartsWith("--", StringComparison.Ordinal))
                {
                    return CommandLineParseResult.Failure(
                        CommandLineErrorKind.MissingFlagValue,
                        $"Flag `{a}` is missing its value.");
                }
                var value = args[i + 1];
                i++;

                switch (a)
                {
                    case "--repo": repoArg = value; break;
                    case "--left": leftArg = value; break;
                    case "--right": rightArg = value; break;
                    case "--file": fileArg = value; break;
                }
            }
            else
            {
                return CommandLineParseResult.Failure(
                    CommandLineErrorKind.UnknownFlag,
                    $"Unknown flag: {a}");
            }
        }

        if (string.IsNullOrEmpty(repoArg))
        {
            return CommandLineParseResult.Failure(
                CommandLineErrorKind.MissingRequiredFlag,
                "Missing required flag: --repo");
        }
        if (string.IsNullOrEmpty(leftArg))
        {
            return CommandLineParseResult.Failure(
                CommandLineErrorKind.MissingRequiredFlag,
                "Missing required flag: --left");
        }
        if (string.IsNullOrEmpty(rightArg))
        {
            return CommandLineParseResult.Failure(
                CommandLineErrorKind.MissingRequiredFlag,
                "Missing required flag: --right");
        }

        // Resolve repo path. Same shape as positional: path must exist;
        // if it isn't itself a repo, discovery walks upward.
        if (!env.PathExists(repoArg))
        {
            return CommandLineParseResult.Failure(
                CommandLineErrorKind.PathDoesNotExist,
                $"Path does not exist: {repoArg}");
        }

        string repoPath = repoArg;
        if (!env.IsGitRepository(repoPath))
        {
            var discovered = env.TryDiscoverRepoRoot(repoPath);
            if (discovered is null)
            {
                return CommandLineParseResult.Failure(
                    CommandLineErrorKind.NotAGitRepository,
                    $"Not a git repository: {repoPath}");
            }
            repoPath = discovered;
        }

        // Resolve each side. WORKING (case-insensitive) → WorkingTree; anything
        // else must resolve as a commit-ish inside the repo.
        var leftSide = ResolveSide(leftArg!, repoPath, env, out var leftErr);
        if (leftErr is not null) return leftErr;

        var rightSide = ResolveSide(rightArg!, repoPath, env, out var rightErr);
        if (rightErr is not null) return rightErr;

        // Normalize the optional file path separator so downstream code can do
        // straight string comparisons against FileEntryViewModel.RepoRelativePath
        // (which already uses Path.DirectorySeparatorChar). Trim a leading
        // separator for robustness against `--file /src/foo.cs`.
        string? initialFile = fileArg;
        if (!string.IsNullOrEmpty(initialFile))
        {
            initialFile = initialFile
                .Replace('/', System.IO.Path.DirectorySeparatorChar)
                .Replace('\\', System.IO.Path.DirectorySeparatorChar)
                .TrimStart(System.IO.Path.DirectorySeparatorChar);
        }

        return CommandLineParseResult.Success(
            new ParsedCommandLine(repoPath, leftSide!, rightSide!, initialFile));
    }

    /// <summary>
    /// Maps a side argument (<c>WORKING</c> or a commit-ish) to a
    /// <see cref="DiffSide"/>, validating commit-ish resolution against the
    /// supplied repo. Sets <paramref name="error"/> on failure; returns
    /// <c>null</c> in that case.
    /// </summary>
    private static DiffSide? ResolveSide(
        string arg, string repoPath, ICommandLineEnvironment env, out CommandLineParseResult? error)
    {
        if (string.Equals(arg, WorkingTreeSentinel, StringComparison.OrdinalIgnoreCase))
        {
            error = null;
            return new DiffSide.WorkingTree();
        }

        if (!env.TryResolveCommitIsh(repoPath, arg))
        {
            error = CommandLineParseResult.Failure(
                CommandLineErrorKind.UnknownCommitIsh,
                $"Cannot resolve `{arg}` in repo {repoPath}");
            return null;
        }

        error = null;
        return new DiffSide.CommitIsh(arg);
    }

    /// <summary>
    /// True if the argument <em>looks</em> like a filesystem path (rather than a commit-ish).
    /// We err on the side of treating it as a commit-ish (more permissive) — only
    /// flag obvious path-like inputs to give a clearer error message.
    /// </summary>
    private static bool IsLikelyPath(string arg)
    {
        // Drive-letter paths: "C:\..." or "C:/..."
        if (arg.Length >= 3 && char.IsLetter(arg[0]) && arg[1] == ':' &&
            (arg[2] == '\\' || arg[2] == '/'))
        {
            return true;
        }

        // UNC paths.
        if (arg.StartsWith(@"\\", StringComparison.Ordinal))
        {
            return true;
        }

        // Relative paths starting with "." or ".."
        if (arg.StartsWith("./", StringComparison.Ordinal) ||
            arg.StartsWith(".\\", StringComparison.Ordinal) ||
            arg.StartsWith("../", StringComparison.Ordinal) ||
            arg.StartsWith("..\\", StringComparison.Ordinal))
        {
            return true;
        }

        return false;
    }
}
