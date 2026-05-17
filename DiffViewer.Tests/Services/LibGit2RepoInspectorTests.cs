using DiffViewer.Services;
using FluentAssertions;
using LibGit2Sharp;
using System.IO;
using Xunit;

namespace DiffViewer.Tests.Services;

/// <summary>
/// Integration coverage for the LibGit2Sharp-backed inspector. Uses
/// <see cref="TempRepo"/> so the libgit2 path actually runs against a
/// real on-disk repo (the unit tests for <see cref="LocalRepoLocator"/>
/// use a fake inspector).
/// </summary>
public sealed class LibGit2RepoInspectorTests
{
    [Fact]
    public void IsRepository_ReturnsTrue_ForValidRepo()
    {
        using var repo = new TempRepo();
        var inspector = new LibGit2RepoInspector();

        inspector.IsRepository(repo.Path).Should().BeTrue();
    }

    [Fact]
    public void IsRepository_ReturnsFalse_ForNonRepoDirectory()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "DiffViewer.NotARepo-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var inspector = new LibGit2RepoInspector();
            inspector.IsRepository(tempDir).Should().BeFalse();
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    [Fact]
    public void IsRepository_ReturnsFalse_ForNonExistentPath()
    {
        var inspector = new LibGit2RepoInspector();
        inspector.IsRepository(@"C:\does\not\exist\" + Guid.NewGuid().ToString("N"))
            .Should().BeFalse();
    }

    [Fact]
    public void GetRemoteUrls_EmptyByDefault()
    {
        using var temp = new TempRepo();
        var inspector = new LibGit2RepoInspector();

        inspector.GetRemoteUrls(temp.Path).Should().BeEmpty();
    }

    [Fact]
    public void GetRemoteUrls_ReturnsAllConfiguredRemotes()
    {
        using var temp = new TempRepo();
        using (var repo = new Repository(temp.Path))
        {
            repo.Network.Remotes.Add("origin", "https://github.com/me/fork.git");
            repo.Network.Remotes.Add("upstream", "https://github.com/canonical/repo.git");
        }

        var inspector = new LibGit2RepoInspector();
        var urls = inspector.GetRemoteUrls(temp.Path);

        urls.Should().BeEquivalentTo(new[]
        {
            "https://github.com/me/fork.git",
            "https://github.com/canonical/repo.git",
        });
    }

    [Fact]
    public void GetRemoteUrls_OnNonRepo_ReturnsEmpty()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "DiffViewer.NotARepo-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var inspector = new LibGit2RepoInspector();
            inspector.GetRemoteUrls(tempDir).Should().BeEmpty();
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }
}
