using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using ScrcpySend.Core;
using Windows.Graphics;

namespace ScrcpySend.App;

internal sealed partial class WindowSizing : IDisposable
{
    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo { public uint Size; public Rect Bounds, Work; public uint Flags; }
    private sealed record Environment(nint Monitor, uint Dpi, Rect Work, int FrameWidth, int FrameHeight);

    [LibraryImport("user32.dll")]
    private static partial uint GetDpiForWindow(nint window);
    [LibraryImport("user32.dll")]
    private static partial nint MonitorFromWindow(nint window, uint flags);
    [LibraryImport("user32.dll", EntryPoint = "GetMonitorInfoW", SetLastError = true)]
    private static partial int GetMonitorInfo(nint monitor, ref MonitorInfo info);

    private readonly AppWindow app;
    private readonly FrameworkElement root;
    private readonly nint handle;
    private readonly Action<Exception> report;
    private XamlRoot? observedRoot;
    private Environment? environment;
    private bool applying, disposed;

    public WindowSizing(Window window, FrameworkElement root, Action<Exception> report)
    {
        app = window.AppWindow;
        this.root = root;
        this.report = report;
        handle = WinRT.Interop.WindowNative.GetWindowHandle(window);
        app.Changed += OnWindowChanged;
        root.Loaded += OnLoaded;
        Apply(true);
    }

    private void OnLoaded(object sender, RoutedEventArgs args)
    {
        if (observedRoot != null) observedRoot.Changed -= OnRootChanged;
        observedRoot = root.XamlRoot;
        if (observedRoot != null) observedRoot.Changed += OnRootChanged;
        Apply(false);
    }
    private void OnRootChanged(XamlRoot sender, XamlRootChangedEventArgs args) => Apply(false);
    private void OnWindowChanged(AppWindow sender, AppWindowChangedEventArgs args) => Apply(false);

    private void Apply(bool initial)
    {
        if (disposed || applying) return;
        applying = true;
        try
        {
            var monitor = MonitorFromWindow(handle, 2);
            var info = new MonitorInfo { Size = (uint)Marshal.SizeOf<MonitorInfo>() };
            if (monitor == 0 || GetMonitorInfo(monitor, ref info) == 0)
                throw new Win32Exception(Marshal.GetLastPInvokeError(), "Cannot query the window's monitor work area.");
            var dpi = GetDpiForWindow(handle);
            if (dpi == 0) throw new InvalidOperationException("Cannot query the window DPI.");
            var frameWidth = Math.Max(0, app.Size.Width - app.ClientSize.Width);
            var frameHeight = Math.Max(0, app.Size.Height - app.ClientSize.Height);
            var current = new Environment(monitor, dpi, info.Work, frameWidth, frameHeight);
            if (!initial && current == environment) return;
            var work = info.Work;
            var placement = WindowSizingPolicy.Create(dpi, work.Left, work.Top, work.Right - work.Left,
                work.Bottom - work.Top, frameWidth, frameHeight);
            if (app.Presenter is not OverlappedPresenter presenter)
                throw new InvalidOperationException("Window size constraints require an overlapped presenter.");
            presenter.PreferredMinimumWidth = placement.MinimumWidth;
            presenter.PreferredMinimumHeight = placement.MinimumHeight;
            environment = current;
            if (initial)
            {
                app.MoveAndResize(new RectInt32(placement.X, placement.Y, placement.Width, placement.Height));
            }
            else if (presenter.State == OverlappedPresenterState.Restored)
            {
                var width = Math.Clamp(app.Size.Width, placement.MinimumWidth, work.Right - work.Left);
                var height = Math.Clamp(app.Size.Height, placement.MinimumHeight, work.Bottom - work.Top);
                var x = Math.Clamp(app.Position.X, work.Left, work.Right - width);
                var y = Math.Clamp(app.Position.Y, work.Top, work.Bottom - height);
                if (width != app.Size.Width || height != app.Size.Height || x != app.Position.X || y != app.Position.Y)
                    app.MoveAndResize(new RectInt32(x, y, width, height));
            }
        }
        catch (Exception error) { report(error); }
        finally { applying = false; }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        app.Changed -= OnWindowChanged;
        root.Loaded -= OnLoaded;
        if (observedRoot != null) observedRoot.Changed -= OnRootChanged;
    }
}
