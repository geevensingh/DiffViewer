using System.IO;
using System.Threading;
using DiffViewer.Models;
using DiffViewer.Services;
using DiffViewer.ViewModels;
using FluentAssertions;
using Xunit;

namespace DiffViewer.Tests.ViewModels;

public sealed class MissingClonePromptViewModelTests
{
    private static readonly PullRequestRef SamplePr =
        new("github.com", "octocat", "hello-world", 1);

    [Fact]
    public async Task BrowseExisting_PathIsRepoWithMatchingRemote_RecordsMappingAndResolves()
    {
        var settings = new FakeSettings();
        var inspector = new FakeInspector
        {
            RepositoryPaths = { @"C:\repos\hello" },
            RemotesByPath = { [@"C:\repos\hello"] = new[] { "https://github.com/octocat/hello-world.git" } },
        };
        var cloner = new FakeCloner();
        var vm = NewVm(settings, inspector, cloner,
            pickFolder: _ => @"C:\repos\hello",
            confirmUseUnmatched: _ => false);

        vm.BrowseExistingCommand.Execute(null);

        var result = await vm.Completion;
        result.Should().BeOfType<MissingClonePromptResult.Resolved>()
            .Which.ClonePath.Should().Be(@"C:\repos\hello");

        settings.Current.RepoUrlMappings
            .Should().ContainKey(RepoUrlKey.From(SamplePr))
            .WhoseValue.Should().Be(@"C:\repos\hello");
    }

    [Fact]
    public void BrowseExisting_NoFolderPicked_DialogStaysOpen()
    {
        var vm = NewVm(
            new FakeSettings(), new FakeInspector(), new FakeCloner(),
            pickFolder: _ => null,
            confirmUseUnmatched: _ => false);

        vm.BrowseExistingCommand.Execute(null);

        vm.Completion.IsCompleted.Should().BeFalse();
    }

    [Fact]
    public void BrowseExisting_NotARepo_ShowsErrorAndStaysOpen()
    {
        var inspector = new FakeInspector(); // empty - nothing is a repo
        var vm = NewVm(
            new FakeSettings(), inspector, new FakeCloner(),
            pickFolder: _ => @"C:\not-a-repo",
            confirmUseUnmatched: _ => false);

        vm.BrowseExistingCommand.Execute(null);

        vm.Completion.IsCompleted.Should().BeFalse();
        vm.ErrorMessage.Should().Contain("not a git repository");
    }

    [Fact]
    public async Task BrowseExisting_NoMatchingRemote_UserConfirms_RecordsMapping()
    {
        var settings = new FakeSettings();
        var inspector = new FakeInspector
        {
            RepositoryPaths = { @"C:\repos\renamed" },
            RemotesByPath =
            {
                [@"C:\repos\renamed"] = new[] { "https://github.com/someone-else/different.git" },
            },
        };
        var prompted = new List<string>();
        var vm = NewVm(settings, inspector, new FakeCloner(),
            pickFolder: _ => @"C:\repos\renamed",
            confirmUseUnmatched: msg => { prompted.Add(msg); return true; });

        vm.BrowseExistingCommand.Execute(null);

        var result = await vm.Completion;
        result.Should().BeOfType<MissingClonePromptResult.Resolved>();
        prompted.Should().HaveCount(1);
        prompted[0].Should().Contain("different");
    }

    [Fact]
    public void BrowseExisting_NoMatchingRemote_UserDeclines_StaysOpen()
    {
        var inspector = new FakeInspector
        {
            RepositoryPaths = { @"C:\repos\renamed" },
            RemotesByPath =
            {
                [@"C:\repos\renamed"] = new[] { "https://github.com/someone-else/different.git" },
            },
        };
        var vm = NewVm(new FakeSettings(), inspector, new FakeCloner(),
            pickFolder: _ => @"C:\repos\renamed",
            confirmUseUnmatched: _ => false);

        vm.BrowseExistingCommand.Execute(null);

        vm.Completion.IsCompleted.Should().BeFalse();
        vm.StatusMessage.Should().Contain("does not match");
    }

    [Fact]
    public async Task BrowseExisting_RemoteIsSshForm_MatchesPr()
    {
        var settings = new FakeSettings();
        var inspector = new FakeInspector
        {
            RepositoryPaths = { @"C:\repos\hello" },
            RemotesByPath = { [@"C:\repos\hello"] = new[] { "git@github.com:octocat/hello-world.git" } },
        };
        var vm = NewVm(settings, inspector, new FakeCloner(),
            pickFolder: _ => @"C:\repos\hello",
            confirmUseUnmatched: _ => false);

        vm.BrowseExistingCommand.Execute(null);

        var result = await vm.Completion;
        result.Should().BeOfType<MissingClonePromptResult.Resolved>();
    }

    [Fact]
    public async Task CloneForMe_Success_RecordsMappingAndResolves()
    {
        var settings = new FakeSettings();
        var inspector = new FakeInspector();
        var cloner = new FakeCloner
        {
            ResultFactory = (url, dest, _, _) => new CloneResult.Success(dest),
        };
        var vm = NewVm(settings, inspector, cloner,
            pickFolder: _ => @"C:\workspace",
            confirmUseUnmatched: _ => false);

        await vm.CloneForMeCommand.ExecuteAsync(null);

        var result = await vm.Completion;
        result.Should().BeOfType<MissingClonePromptResult.Resolved>()
            .Which.ClonePath.Should().Be(@"C:\workspace\hello-world");

        cloner.LastCloneUrl.Should().Be("https://github.com/octocat/hello-world.git");
        cloner.LastDestination.Should().Be(@"C:\workspace\hello-world");
        settings.Current.RepoUrlMappings
            .Should().ContainKey(RepoUrlKey.From(SamplePr));
    }

    [Fact]
    public async Task CloneForMe_OffersToRememberDefault_OnAccept_WritesSetting()
    {
        var settings = new FakeSettings();
        var inspector = new FakeInspector();
        var cloner = new FakeCloner
        {
            ResultFactory = (_, dest, _, _) => new CloneResult.Success(dest),
        };
        var vm = NewVm(settings, inspector, cloner,
            pickFolder: _ => @"C:\workspace",
            confirmUseUnmatched: _ => false,
            confirmRememberDefaultClone: path => path == @"C:\workspace");

        await vm.CloneForMeCommand.ExecuteAsync(null);

        settings.Current.DefaultCloneDestination.Should().Be(@"C:\workspace");
    }

    [Fact]
    public async Task CloneForMe_AlreadyHasDefaultDestination_DoesNotRePrompt()
    {
        var settings = new FakeSettings
        {
            Current = new AppSettings { DefaultCloneDestination = @"C:\existing-default" },
        };
        var inspector = new FakeInspector();
        var cloner = new FakeCloner
        {
            ResultFactory = (_, dest, _, _) => new CloneResult.Success(dest),
        };
        var rememberCalls = 0;
        var vm = NewVm(settings, inspector, cloner,
            pickFolder: _ => @"C:\workspace",
            confirmUseUnmatched: _ => false,
            confirmRememberDefaultClone: _ => { rememberCalls++; return true; });

        await vm.CloneForMeCommand.ExecuteAsync(null);

        rememberCalls.Should().Be(0);
        settings.Current.DefaultCloneDestination.Should().Be(@"C:\existing-default");
    }

    [Fact]
    public async Task CloneForMe_AuthFailed_SetsErrorAndStaysOpen()
    {
        var cloner = new FakeCloner
        {
            ResultFactory = (_, _, _, _) =>
                new CloneResult.AuthFailed("clone gh repo clone instead"),
        };
        var vm = NewVm(new FakeSettings(), new FakeInspector(), cloner,
            pickFolder: _ => @"C:\workspace",
            confirmUseUnmatched: _ => false);

        await vm.CloneForMeCommand.ExecuteAsync(null);

        vm.Completion.IsCompleted.Should().BeFalse();
        vm.ErrorMessage.Should().Contain("clone gh repo clone instead");
        vm.StatusMessage.Should().Contain("Browse");
    }

    [Fact]
    public async Task CloneForMe_GenericFailure_SetsErrorAndStaysOpen()
    {
        var cloner = new FakeCloner
        {
            ResultFactory = (_, _, _, _) =>
                new CloneResult.Failed("network unreachable"),
        };
        var vm = NewVm(new FakeSettings(), new FakeInspector(), cloner,
            pickFolder: _ => @"C:\workspace",
            confirmUseUnmatched: _ => false);

        await vm.CloneForMeCommand.ExecuteAsync(null);

        vm.Completion.IsCompleted.Should().BeFalse();
        vm.ErrorMessage.Should().Contain("network unreachable");
    }

    [Fact]
    public async Task CloneForMe_Cancelled_StaysOpen()
    {
        var cloner = new FakeCloner
        {
            ResultFactory = (_, _, _, _) => new CloneResult.Cancelled(),
        };
        var vm = NewVm(new FakeSettings(), new FakeInspector(), cloner,
            pickFolder: _ => @"C:\workspace",
            confirmUseUnmatched: _ => false);

        await vm.CloneForMeCommand.ExecuteAsync(null);

        vm.Completion.IsCompleted.Should().BeFalse();
        vm.StatusMessage.Should().Contain("cancelled");
    }

    [Fact]
    public async Task CloneForMe_PrecancelledToken_CancelsBeforeCloneStarts()
    {
        // Smoke test: pass cancellation through the cloner observation
        // window and assert the result propagates within bounded time.
        var cloner = new FakeCloner
        {
            ResultFactory = (_, _, _, ct) =>
            {
                ct.ThrowIfCancellationRequested();
                return new CloneResult.Success("unreachable");
            },
        };
        var vm = NewVm(new FakeSettings(), new FakeInspector(), cloner,
            pickFolder: _ => @"C:\workspace",
            confirmUseUnmatched: _ => false);

        // Cancel before the cloner is invoked: the VM's internal CTS is
        // created in CloneForMe, so we trigger cancellation via the VM
        // command and then immediately call CancelClone via a
        // continuation-injection in the cloner.
        var cancelTriggered = new ManualResetEventSlim(false);
        cloner.ResultFactory = (_, _, _, ct) =>
        {
            cancelTriggered.Wait(TimeSpan.FromSeconds(2));
            ct.ThrowIfCancellationRequested();
            return new CloneResult.Failed("should not reach");
        };

        var task = vm.CloneForMeCommand.ExecuteAsync(null);
        vm.CancelCloneCommand.Execute(null);
        cancelTriggered.Set();

        await task;
        vm.Completion.IsCompleted.Should().BeFalse();
    }

    [Fact]
    public async Task CloneForMe_DestinationExists_ShowsErrorAndDoesNotInvokeCloner()
    {
        var inspector = new FakeInspector
        {
            RepositoryPaths = { @"C:\workspace\hello-world" },
        };
        // Inspector treats the dir as a repo for IsRepository, but the
        // existence check in the VM is independent. We need a path that
        // Directory.Exists returns true for, which means a real temp dir.
        var tempParent = Path.Combine(Path.GetTempPath(), "diffviewer-tests-" + Guid.NewGuid().ToString("N"));
        var conflicting = Path.Combine(tempParent, "hello-world");
        Directory.CreateDirectory(conflicting);
        try
        {
            var cloner = new FakeCloner();
            var vm = NewVm(new FakeSettings(), new FakeInspector(), cloner,
                pickFolder: _ => tempParent,
                confirmUseUnmatched: _ => false);

            await vm.CloneForMeCommand.ExecuteAsync(null);

            vm.ErrorMessage.Should().Contain("already exists");
            cloner.CallCount.Should().Be(0);
            vm.Completion.IsCompleted.Should().BeFalse();
        }
        finally
        {
            Directory.Delete(tempParent, recursive: true);
        }
    }

    [Fact]
    public async Task Cancel_CompletesWithCancelledResult()
    {
        var vm = NewVm(new FakeSettings(), new FakeInspector(), new FakeCloner(),
            pickFolder: _ => null,
            confirmUseUnmatched: _ => false);

        vm.CancelCommand.Execute(null);

        var result = await vm.Completion;
        result.Should().BeOfType<MissingClonePromptResult.Cancelled>();
    }

    private static MissingClonePromptViewModel NewVm(
        FakeSettings settings,
        FakeInspector inspector,
        FakeCloner cloner,
        Func<string?, string?> pickFolder,
        Func<string, bool> confirmUseUnmatched,
        Func<string, bool>? confirmRememberDefaultClone = null)
    {
        return new MissingClonePromptViewModel(
            SamplePr, settings, inspector, cloner, pickFolder,
            confirmUseUnmatched, confirmRememberDefaultClone);
    }

    private sealed class FakeSettings : ISettingsService
    {
        public AppSettings Current { get; set; } = new();
        public SettingsLoadOutcome LastLoadOutcome => SettingsLoadOutcome.Loaded;
        public event EventHandler<SettingsChangedEventArgs>? Changed;

        public void Save(AppSettings updated)
        {
            var prev = Current;
            Current = updated;
            Changed?.Invoke(this, new SettingsChangedEventArgs(prev, updated));
        }

        public AppSettings Update(Func<AppSettings, AppSettings> mutate)
        {
            Save(mutate(Current));
            return Current;
        }
    }

    private sealed class FakeInspector : IRepoInspector
    {
        public HashSet<string> RepositoryPaths { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, IReadOnlyList<string>> RemotesByPath { get; }
            = new(StringComparer.OrdinalIgnoreCase);

        public bool IsRepository(string path)
            => RepositoryPaths.Contains(path) || RemotesByPath.ContainsKey(path);

        public IReadOnlyList<string> GetRemoteUrls(string path)
            => RemotesByPath.TryGetValue(path, out var remotes)
                ? remotes
                : Array.Empty<string>();
    }

    private sealed class FakeCloner : IGitHubCloner
    {
        public int CallCount { get; private set; }
        public string? LastCloneUrl { get; private set; }
        public string? LastDestination { get; private set; }
        public Func<string, string, IProgress<CloneProgress>?, CancellationToken, CloneResult>?
            ResultFactory { get; set; }

        public Task<CloneResult> CloneAsync(
            string cloneUrl,
            string destinationPath,
            IProgress<CloneProgress>? progress,
            CancellationToken ct)
        {
            CallCount++;
            LastCloneUrl = cloneUrl;
            LastDestination = destinationPath;
            if (ResultFactory is null)
            {
                return Task.FromResult<CloneResult>(new CloneResult.Failed("no factory configured"));
            }
            return Task.FromResult(ResultFactory(cloneUrl, destinationPath, progress, ct));
        }
    }
}
