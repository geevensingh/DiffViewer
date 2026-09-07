using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using DiffViewer.Services;
using DiffViewer.ViewModels;
using FluentAssertions;
using Xunit;

namespace DiffViewer.Tests.ViewModels;

/// <summary>
/// Behavior of the worktree picker attached to the "New diff" dialog's
/// repo-path input.
/// </summary>
public class WorktreePickerViewModelTests
{
    [Fact]
    public void IsEnabled_WithNoRepoPath_IsFalse()
    {
        var picker = MakePicker(out _, out _);

        picker.IsEnabled.Should().BeFalse();
    }

    [Fact]
    public void IsEnabled_OnceARepoPathResolves_IsTrue()
    {
        var picker = MakePicker(out _, out _);

        picker.CanonicalRepoPath = @"C:\repos\diffviewer";

        picker.IsEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task EnsureLoadedAsync_WithNoRepoPath_DoesNotEnumerate()
    {
        var picker = MakePicker(out var enumerator, out _);

        await picker.EnsureLoadedAsync();

        enumerator.EnumeratedPaths.Should().BeEmpty();
        picker.IsLoaded.Should().BeFalse();
    }

    [Fact]
    public async Task EnsureLoadedAsync_EnumeratesAgainstTheCurrentRepoPath()
    {
        var picker = MakePicker(out var enumerator, out _, MainWorktree(), Linked("feature-a"));
        picker.CanonicalRepoPath = @"C:\repos\diffviewer";

        await picker.EnsureLoadedAsync();

        enumerator.EnumeratedPaths.Should().ContainSingle().Which.Should().Be(@"C:\repos\diffviewer");
        picker.Worktrees.Should().HaveCount(2);
        picker.IsLoaded.Should().BeTrue();
    }

    [Fact]
    public async Task EnsureLoadedAsync_CalledTwice_EnumeratesOnlyOnce()
    {
        var picker = MakePicker(out var enumerator, out _, MainWorktree());
        picker.CanonicalRepoPath = @"C:\repos\diffviewer";

        await picker.EnsureLoadedAsync();
        await picker.EnsureLoadedAsync();

        enumerator.EnumeratedPaths.Should().HaveCount(1);
    }

    [Fact]
    public async Task ChangingTheRepoPath_DiscardsThePreviousEnumeration()
    {
        var picker = MakePicker(out _, out _, MainWorktree(), Linked("feature-a"));
        picker.CanonicalRepoPath = @"C:\repos\diffviewer";
        await picker.EnsureLoadedAsync();

        picker.CanonicalRepoPath = @"C:\repos\other";

        picker.IsLoaded.Should().BeFalse();
        picker.Worktrees.Should().BeEmpty();
    }

    [Fact]
    public async Task HasAlternativeWorktrees_WithOnlyTheMainWorktree_IsFalse()
    {
        // A repo with no linked worktrees would otherwise render a
        // one-row list pointing at where the user already is.
        var picker = MakePicker(out _, out _, MainWorktree());
        picker.CanonicalRepoPath = @"C:\repos\diffviewer";

        await picker.EnsureLoadedAsync();

        picker.HasAlternativeWorktrees.Should().BeFalse();
    }

    [Fact]
    public async Task HasAlternativeWorktrees_WithALinkedWorktree_IsTrue()
    {
        var picker = MakePicker(out _, out _, MainWorktree(), Linked("feature-a"));
        picker.CanonicalRepoPath = @"C:\repos\diffviewer";

        await picker.EnsureLoadedAsync();

        picker.HasAlternativeWorktrees.Should().BeTrue();
    }

    [Fact]
    public void PickWorktree_WritesTheWorkingDirectoryBack()
    {
        var picker = MakePicker(out _, out var written);

        picker.PickWorktreeCommand.Execute(Linked("feature-a"));

        written.Should().ContainSingle().Which.Should().Be(@"C:\worktrees\feature-a");
    }

    [Fact]
    public void PickWorktree_ForAMissingWorktree_WritesNothing()
    {
        // The row is shown so the user understands what git still knows
        // about - not so they can diff a directory that isn't there.
        var picker = MakePicker(out _, out var written);

        picker.PickWorktreeCommand.Execute(Linked("feature-a") with { IsMissing = true });

        written.Should().BeEmpty();
    }

    [Fact]
    public void PickWorktree_WithNoSelection_WritesNothing()
    {
        var picker = MakePicker(out _, out var written);

        picker.PickWorktreeCommand.Execute(null);

        written.Should().BeEmpty();
    }

    private static WorktreePickerViewModel MakePicker(
        out StubWorktreeEnumerator enumerator,
        out List<string> written,
        params WorktreeEntry[] entries)
    {
        var stub = new StubWorktreeEnumerator(entries);
        var writes = new List<string>();
        enumerator = stub;
        written = writes;
        return new WorktreePickerViewModel(
            stub,
            writeBack: writes.Add,
            initialCanonicalRepoPath: null,
            enumerateRunner: work => Task.FromResult(work()));
    }

    private static WorktreeEntry MainWorktree() => new(
        Name: "diffviewer",
        WorkingDirectory: @"C:\repos\diffviewer",
        HeadFriendlyName: "master",
        IsMain: true,
        IsCurrent: true,
        IsLocked: false,
        IsMissing: false);

    private static WorktreeEntry Linked(string name) => new(
        Name: name,
        WorkingDirectory: $@"C:\worktrees\{name}",
        HeadFriendlyName: name,
        IsMain: false,
        IsCurrent: false,
        IsLocked: false,
        IsMissing: false);
}
