using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using DiffViewer.Rendering;
using DiffViewer.Services;
using DiffViewer.Utility;
using ICSharpCode.AvalonEdit.Highlighting;
using Velopack;

namespace DiffViewer;

public partial class App : Application
{
    // PHASE 1 SPIKE: hardcoded local-folder feed for the Velopack
    // proof-of-concept. Phase 2 will replace with a configurable
    // GitHub-releases URL + settings-driven check cadence.
    private const string SpikeFeedPath = @"C:\Repos\DiffViewer-VelopackFeed";

    private CancellationTokenSource? _shutdownCts;
    private MainWindowCoordinator? _coordinator;
    private HttpClient? _httpClient;

    // PHASE 1 SPIKE: WPF's auto-generated Main doesn't give Velopack
    // a hook to intercept install/uninstall/restart args before WPF
    // spins up. Per the Velopack WPF sample
    // (velopack/velopack/samples/CSharpWpf/App.xaml.cs) we replace
    // it with our own Main and set <StartupObject> in DiffViewer.csproj.
    [STAThread]
    private static void Main(string[] args)
    {
        try
        {
            // Must run before any WPF code so --veloapp-install /
            // --veloapp-update / etc. are handled and the process
            // exits without ever spinning up the UI.
            VelopackApp.Build()
                .OnFirstRun(_ => { /* spike: nothing */ })
                .Run();
        }
        catch (Exception ex)
        {
            LogSpike($"VelopackApp.Run() failed: {ex}");
        }

        var app = new App();
        app.InitializeComponent();
        app.Run();
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Attach to the parent process's console (cmd / PowerShell / git
        // stdio) before anything else. Returns false for double-click
        // launches (no parent console) — see ConsoleAttacher's class docs.
        // Done up-front so any later stderr writes (e.g. CLI parse errors
        // surfaced through MainWindowCoordinator.HandleColdLaunchFailure)
        // land in the launching terminal, matching the behavior expected by
        // `git difftool` and other CLI consumers (issue #5).
        ConsoleAttacher.AttachToParent();

        // Register DiffViewer's hand-authored XSHD highlighting definitions
        // (TypeScript, YAML, Go, Rust, Ruby, Bash, TOML) on top of the set
        // AvalonEdit ships with. Idempotent; lazy-loads the XSHD payloads on
        // first lookup so this doesn't impact startup time.
        CustomHighlightingRegistrar.RegisterAll(HighlightingManager.Instance);

        _shutdownCts = new CancellationTokenSource();

        // App-level singletons that survive in-place context switches.
        // Per-context resources (RepositoryService, watcher, MainViewModel,
        // etc.) are owned by the per-context ContextScope built inside
        // MainWindowCoordinator → CompositionRoot.
        var settingsService = new SettingsService();
        var diffService = new DiffService();
        var externalAppLauncher = new ExternalAppLauncher(settingsService);
        var recents = new RecentContextsService();
        // Hydrate from disk before the dropdown is wired up. Failure to
        // load is already handled inside RecentsStore.LoadAsync (returns
        // Empty on missing/malformed file) so this only awaits the IO.
        await recents.LoadAsync(_shutdownCts.Token);

        // PR-review feature dependency graph (Phases 2-8). Each layer is
        // a single instance for the app's lifetime; HttpClient is held by
        // App so it can be disposed cleanly on shutdown. None of these
        // services hold repo-specific state — they're keyed by
        // (host, owner, repo, number) parameters at call time so the
        // per-context ContextScope doesn't need to know about them.
        var processRunner = new DefaultProcessRunner();
        var authProvider = new GhCliAuthProvider(processRunner);
        _httpClient = new HttpClient();
        var githubClient = new GitHubClient(_httpClient, authProvider);
        var repoInspector = new LibGit2RepoInspector();
        var localRepoLocator = new LocalRepoLocator(settingsService, repoInspector, recents);
        var metadataResolver = new PullRequestMetadataResolver(githubClient);
        var fetcher = new PullRequestLocalFetcher(processRunner);
        var prResolver = new PullRequestResolver(localRepoLocator, metadataResolver, fetcher);
        var cloner = new LibGit2GitHubCloner();
        // ownerLookup runs at dialog-show time so it picks up whichever
        // window is currently active (handles the rare case where the
        // main window has been re-created mid-session).
        var missingClonePromptHost = new MissingClonePromptHost(
            settingsService, repoInspector, cloner,
            ownerLookup: () => Application.Current?.MainWindow);

        // "New diff" dialog (Phase 2/3 of the in-app mode-switching
        // feature). Validator wraps a fresh ProcessCommandLineEnvironment
        // — same seam the CLI parser uses, so the dialog accepts the
        // same paths and commit-ish refs the command line does. The
        // ownerLookup closure runs at dialog-show time so it picks up
        // the live MainWindow, matching the MissingClonePromptHost
        // pattern.
        var diffLaunchValidator = new DiffLaunchValidator(new ProcessCommandLineEnvironment());
        var diffModeRegistry = DiffModeRegistry.BuildDefault();
        var refEnumerator = new LibGit2GitRefEnumerator();
        var clipboardService = new WpfClipboardService();
        var newDiffDialogHost = new NewDiffDialogHost(
            diffModeRegistry,
            diffLaunchValidator,
            refEnumerator,
            recents,
            clipboardService,
            ownerLookup: () => Application.Current?.MainWindow);

        var services = new AppServices(
            settingsService, diffService, externalAppLauncher, recents,
            prResolver, missingClonePromptHost, newDiffDialogHost);

        _coordinator = new MainWindowCoordinator(
            services,
            new MessageBoxDialogService(),
            _shutdownCts.Token,
            stderrWriter: ConsoleAttacher.IsAttached
                ? message =>
                {
                    try { Console.Error.WriteLine(message); }
                    catch { /* best-effort; parent console may have closed */ }
                }
                : null);
        // Late-bind the switcher into the services bundle so per-context
        // view-models (built later inside CompositionRoot.BuildContextAsync)
        // can wire the dropdown to the coordinator.
        services.ContextSwitcher = _coordinator;

        var window = new MainWindow(settingsService);
        Application.Current.MainWindow = window;
        // Expose the coordinator on Window.Tag so MainWindow.xaml's
        // loading-overlay bindings can read IsSwitching / SwitchingStatus
        // via RelativeSource={RelativeSource AncestorType=Window} without
        // perturbing the existing DataContext = Coordinator.Current
        // contract that the inner content templates depend on.
        window.Tag = _coordinator;
        _coordinator.CurrentChanged += (_, _) => window.DataContext = _coordinator.Current;
        window.Closed += async (_, _) =>
        {
            if (_coordinator is not null) await _coordinator.DisposeCurrentAsync();
        };

        var ok = await _coordinator.InitialLaunchAsync(e.Args, _shutdownCts.Token);
        if (!ok)
        {
            // Coordinator already showed the error dialog and called
            // Shutdown(1); just bail out before Show().
            return;
        }

        window.Show();

        // PHASE 1 SPIKE: fire-and-forget headless update check against
        // the local-folder feed. No UI; verifies the
        // CheckForUpdates → Download → ApplyAndRestart loop end-to-end.
        _ = Task.Run(CheckForUpdatesSpikeAsync);
    }

    private static async Task CheckForUpdatesSpikeAsync()
    {
        try
        {
            var mgr = new UpdateManager(SpikeFeedPath);
            if (!mgr.IsInstalled)
            {
                LogSpike("UpdateManager.IsInstalled=false (running from dev / portable). Skipping.");
                return;
            }
            LogSpike($"Current version: {mgr.CurrentVersion}; checking feed {SpikeFeedPath}");
            var info = await mgr.CheckForUpdatesAsync();
            if (info is null)
            {
                LogSpike("No update available.");
                return;
            }
            LogSpike($"Update found: {info.TargetFullRelease.Version}. Downloading...");
            await mgr.DownloadUpdatesAsync(info);
            LogSpike("Download complete. Applying and restarting.");
            mgr.ApplyUpdatesAndRestart(info);
        }
        catch (Exception ex)
        {
            LogSpike($"Update check failed: {ex}");
        }
    }

    private static void LogSpike(string message)
    {
        try
        {
            var logPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "DiffViewer",
                "velopack-spike.log");
            Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
            File.AppendAllText(logPath, $"[{DateTime.Now:O}] {message}{Environment.NewLine}");
        }
        catch { /* best-effort spike logging */ }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try { _shutdownCts?.Cancel(); } catch { }
        try { _shutdownCts?.Dispose(); } catch { }
        try { _httpClient?.Dispose(); } catch { }
        base.OnExit(e);
    }
}
