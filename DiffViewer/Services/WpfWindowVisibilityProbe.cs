using System.Windows;

namespace DiffViewer.Services;

/// <summary>
/// Production <see cref="IWindowVisibilityProbe"/> backed by the WPF
/// <see cref="Application.Current"/> main window. Tracks
/// <see cref="Window.IsVisibleChanged"/> and <see cref="Window.StateChanged"/>
/// — a window that's <c>Hidden</c>, <c>Minimized</c>, or has
/// <see cref="UIElement.IsVisible"/> false counts as not visible.
///
/// <para>The probe binds to the first <see cref="Application.MainWindow"/>
/// that appears after construction. If the app hasn't created its main
/// window yet (early-startup race), the probe reports <c>true</c>
/// (the optimistic default: better to poll once unnecessarily than
/// silently skip the first poll because the window isn't wired up
/// yet). Once the window appears, the probe latches onto it for the
/// rest of the app's lifetime — we never need to follow a different
/// window because DiffViewer is single-window by design.</para>
///
/// <para>App-singleton: registered once in <c>AppServices</c> and
/// shared across every diff context.</para>
/// </summary>
public sealed class WpfWindowVisibilityProbe : IWindowVisibilityProbe
{
    private readonly object _lock = new();
    private Window? _window;
    private bool _lastVisible = true;

    public event EventHandler? VisibilityChanged;

    public bool IsVisible
    {
        get
        {
            EnsureBound();
            lock (_lock) return _lastVisible;
        }
    }

    private void EnsureBound()
    {
        if (_window is not null) return;

        var app = Application.Current;
        if (app is null) return;

        // Marshal onto the UI dispatcher so the IsVisible read and the
        // event subscription happen on the thread that owns the window.
        if (app.Dispatcher.CheckAccess())
        {
            BindOnUiThread(app);
        }
        else
        {
            app.Dispatcher.Invoke(() => BindOnUiThread(app));
        }
    }

    private void BindOnUiThread(Application app)
    {
        var window = app.MainWindow;
        if (window is null) return;

        lock (_lock)
        {
            if (_window is not null) return;
            _window = window;
            _lastVisible = ComputeVisible(window);
        }

        window.IsVisibleChanged += OnIsVisibleChanged;
        window.StateChanged += OnStateChanged;
    }

    private static bool ComputeVisible(Window window)
        => window.IsVisible && window.WindowState != WindowState.Minimized;

    private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        => RecomputeAndRaise();

    private void OnStateChanged(object? sender, EventArgs e)
        => RecomputeAndRaise();

    private void RecomputeAndRaise()
    {
        bool now;
        Window? window;
        lock (_lock)
        {
            window = _window;
        }
        if (window is null) return;

        bool computed = ComputeVisible(window);
        bool fire;
        lock (_lock)
        {
            fire = _lastVisible != computed;
            _lastVisible = computed;
            now = _lastVisible;
        }

        if (fire) VisibilityChanged?.Invoke(this, EventArgs.Empty);
        _ = now; // local capture for readability; intentional discard
    }
}
