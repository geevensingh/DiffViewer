using System.IO;
using DiffViewer;
using DiffViewer.Services;
using FluentAssertions;
using LibGit2Sharp;
using Xunit;

namespace DiffViewer.Tests.Services;

/// <summary>
/// End-to-end probe of <see cref="ProcessCommandLineEnvironment"/> against
/// a real LibGit2Sharp repo. Most parser tests use StubEnv; this is the
/// only place we verify what the production environment actually accepts.
/// </summary>
public class ProcessCommandLineEnvironmentTests
{
    [Fact]
    public void TryResolveCommitIsh_ExactBranchName_Resolves()
    {
        using var repo = new TempRepo();
        repo.WriteFile("a.txt", "1\n");
        repo.InitialCommit("c1");

        // TempRepo.Init creates the default branch; modern libgit2 calls
        // it "master". Verify exact-ref resolution works.
        var env = new ProcessCommandLineEnvironment();
        env.TryResolveCommitIsh(repo.Path, "HEAD").Should().BeTrue();
    }

    [Fact]
    public void TryResolveCommitIsh_RevparseTildeSyntax_Resolves()
    {
        // Repro for the user-reported bug: "+ New diff" → Commit vs
        // commit → main~1 vs main shows "Cannot resolve `main~1`".
        // LibGit2Sharp's Repository.Lookup<Commit>(objectish) DOES
        // call git_revparse_single under the hood; verify we don't
        // regress the revparse syntax that the CLI parser already
        // accepts.
        using var repo = new TempRepo();
        repo.WriteFile("a.txt", "1\n");
        repo.InitialCommit("c1");
        repo.WriteFile("a.txt", "2\n");
        repo.Commit("c2");

        var env = new ProcessCommandLineEnvironment();
        env.TryResolveCommitIsh(repo.Path, "HEAD~1").Should().BeTrue();
        env.TryResolveCommitIsh(repo.Path, "HEAD^").Should().BeTrue();
    }

    [Fact]
    public void TryResolveCommitIsh_BranchNameTildeSyntax_Resolves()
    {
        // The exact shape of the user's report: branch name with ~N.
        using var repo = new TempRepo();
        repo.WriteFile("a.txt", "1\n");
        repo.InitialCommit("c1");
        repo.WriteFile("a.txt", "2\n");
        repo.Commit("c2");

        // Resolve the actual current branch name (could be "master" or
        // "main" depending on init.defaultBranch).
        string branchName;
        using (var r = new Repository(repo.Path))
        {
            branchName = r.Head.FriendlyName;
        }

        var env = new ProcessCommandLineEnvironment();
        env.TryResolveCommitIsh(repo.Path, branchName).Should().BeTrue();
        env.TryResolveCommitIsh(repo.Path, branchName + "~1").Should().BeTrue();
    }

    [Fact]
    public void TryResolveCommitIsh_Nonsense_ReturnsFalse()
    {
        using var repo = new TempRepo();
        repo.WriteFile("a.txt", "1\n");
        repo.InitialCommit("c1");

        var env = new ProcessCommandLineEnvironment();
        env.TryResolveCommitIsh(repo.Path, "this-ref-does-not-exist").Should().BeFalse();
    }

    [Fact]
    public void TryResolveCommitIsh_LightweightTag_Resolves()
    {
        // Lightweight tags (`git tag <name>` with no -a/-m) point
        // directly at the underlying commit, so even the unpeeled
        // Lookup<Commit> would catch them. Pinned as a baseline so
        // any future refactor that breaks tag resolution surfaces
        // here rather than only against the annotated case below.
        using var t = new TempRepo();
        t.WriteFile("a.txt", "1\n");
        var c1 = t.InitialCommit("c1");
        t.CreateLightweightTag("v1.0.0", c1);

        var env = new ProcessCommandLineEnvironment();
        env.TryResolveCommitIsh(t.Path, "v1.0.0").Should().BeTrue();
    }

    [Fact]
    public void TryResolveCommitIsh_AnnotatedTag_Resolves()
    {
        // Regression for the user-reported bug: picking `v1.2.0` from
        // the ref-picker (which surfaces annotated tags by peeling
        // through their TagAnnotation wrapper) put `v1.2.0` into the
        // commit-ish field, but the validator's Lookup<Commit> call
        // returned null because annotated tags resolve to a
        // TagAnnotation, not a Commit. The dialog then reported
        // "Cannot resolve `v1.2.0`" for a tag the picker had just
        // offered. The validator now peels through the wrapper so
        // picker output and validator input agree.
        using var t = new TempRepo();
        t.WriteFile("a.txt", "1\n");
        var c1 = t.InitialCommit("c1");
        t.CreateAnnotatedTag("v1.2.0", c1, "Release v1.2.0");

        var env = new ProcessCommandLineEnvironment();
        env.TryResolveCommitIsh(t.Path, "v1.2.0").Should().BeTrue();
    }
}
