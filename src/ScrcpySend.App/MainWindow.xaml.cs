using System.Text.Json;
using System.Globalization;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ScrcpySend.Core;
using ScrcpySend.Media;
using Windows.Graphics;
using Windows.Graphics.Capture;

namespace ScrcpySend.App;

public sealed partial class MainWindow : Window
{
    private readonly CancellationTokenSource lifetime = new();
    private readonly Task discovery;
    private CancellationTokenSource? castStop;
    private Task? cast;
    private GraphicsCaptureItem? source;
    private bool closing;
    private bool picking;
    private bool discoveryWarning;
    private bool initialized;
    private bool refreshingReceivers;
    private bool busy;
    private bool manualAddressTouched;
    private bool manualPortTouched;
    private readonly ReceiverSelection receiverSelection = new();
    private readonly WindowSizing windowSizing;

    public MainWindow()
    {
        InitializeComponent();
        PlatformLabel.Text = $"{System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture} · Hardware video · Trusted private LAN only";
        SrtMedia.NativeDiagnostic += Log;
        windowSizing = new WindowSizing(this, LayoutRoot, Report);
        AppWindow.Closing += Window_Closing;
        SelectFrameRate(new CaptureSettings().MaxFps);
        LoadSettings();
        initialized = true;
        if (!Environment.GetCommandLineArgs().Any(argument => argument is "--ui-self-test" or "--capture-self-test" or "--smoke-test"))
        {
            try { SetSource(WindowsVideoSource.SelectDefaultDisplay()); }
            catch (Exception error) { Report(new IOException($"Could not select the default display. Use Choose a display or window. {error.Message}", error)); }
        }
        RefreshReceivers([]);
        discovery = Environment.GetCommandLineArgs().Contains("--ui-self-test") ? Task.CompletedTask : DiscoverAsync();
        Closed += (_, _) => { SrtMedia.NativeDiagnostic -= Log; windowSizing.Dispose(); lifetime.Cancel(); };
    }

    private void Report(Exception error)
    {
        Log(error.ToString());
        Problem.Title = "Action failed";
        Problem.Message = error.Message;
        Problem.IsOpen = true;
    }

    internal void Log(string detail)
    {
        try { AppLog.Write(detail); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                Problem.Title = "Diagnostics log unavailable";
                Problem.Message = error.Message;
                Problem.IsOpen = true;
            });
        }
    }

    private async Task DiscoverAsync()
    {
        try
        {
            await new ReceiverDiscovery().RunAsync(items => DispatcherQueue.TryEnqueue(() =>
            {
                if (cast != null || closing) return;
                RefreshReceivers(items);
            }), detail =>
            {
                Log(detail);
                DispatcherQueue.TryEnqueue(() =>
                {
                    if (discoveryWarning) return;
                    discoveryWarning = true;
                    Problem.Title = "Invalid LAN advertisement ignored";
                    Problem.Message = detail;
                    Problem.IsOpen = true;
                });
            }, lifetime.Token);
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception error)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                Log(error.ToString());
                Problem.Title = "Discovery unavailable";
                Problem.Message = $"{error.Message} You can still enter the receiver IP manually.";
                Problem.IsOpen = true;
            });
        }
    }

    private void RefreshReceivers(IReadOnlyList<ReceiverEndpoint> items)
    {
        receiverSelection.Refresh(items);
        if (!Receivers.Items.Cast<ReceiverChoice>().SequenceEqual(receiverSelection.Choices))
        {
            refreshingReceivers = true;
            try
            {
                Receivers.ItemsSource = receiverSelection.Choices;
                Receivers.SelectedItem = receiverSelection.Selected;
            }
            finally { refreshingReceivers = false; }
        }
        DiscoveryStatus.Text = items.Count == 0 ? "No receivers discovered." :
            items.All(item => item.Busy) ? $"{items.Count} receiver(s) discovered; all are busy." :
            $"{items.Count} receiver(s) discovered.";
        UpdateManualEntry();
        UpdateValidation();
    }

    private void Receivers_SelectionChanged(object sender, SelectionChangedEventArgs args)
    {
        if (!initialized || refreshingReceivers) return;
        if (Receivers.SelectedItem is ReceiverChoice choice) receiverSelection.Select(choice);
        UpdateManualEntry();
        UpdateValidation();
    }

    private void UpdateManualEntry() =>
        ManualEntry.Visibility = receiverSelection.Selected?.IsManual == true ? Visibility.Visible : Visibility.Collapsed;

    private void Settings_TextChanged(object sender, TextChangedEventArgs args)
    {
        if (initialized) UpdateValidation();
    }

    private void Settings_SelectionChanged(object sender, SelectionChangedEventArgs args)
    {
        if (initialized) UpdateValidation();
    }

    private void EnableAudio_Changed(object sender, RoutedEventArgs args)
    {
        if (initialized) UpdateValidation();
    }

    private void ManualAddress_LostFocus(object sender, RoutedEventArgs args)
    {
        if (!initialized || receiverSelection.Selected?.IsManual != true) return;
        manualAddressTouched = true;
        UpdateValidation();
    }

    private void ManualPort_LostFocus(object sender, RoutedEventArgs args)
    {
        if (!initialized || receiverSelection.Selected?.IsManual != true) return;
        manualPortTouched = true;
        UpdateValidation();
    }

    private void SelectFrameRate(int fps)
    {
        MaxFps.ItemsSource = CaptureSettings.FpsPresets.Append(fps).Distinct().Order().ToArray();
        MaxFps.SelectedItem = fps;
    }

    private CaptureSettings.CaptureInput ReadInput() => CaptureSettings.ParseInput(MaxSize.Text, Bitrate.Text,
        MaxFps.SelectedItem is int fps ? fps : null,
        (string)((ComboBoxItem)ReceiverOrientation.SelectedItem).Tag, CultureInfo.CurrentCulture,
        (string)((ComboBoxItem)VideoCodec.SelectedItem).Tag, EnableAudio.IsChecked == true,
        (string)((ComboBoxItem)MediaTransport.SelectedItem).Tag);

    private void UpdateValidation()
    {
        if (!initialized) return;
        var input = ReadInput();
        SetError(DimensionError, input.DimensionError);
        SetError(BitrateError, input.BitrateError);
        SetError(FpsError, input.FpsError);
        ReceiverEndpoint? endpoint = null;
        string? endpointError = null;
        var manualSelected = receiverSelection.Selected?.IsManual == true;
        var manual = ManualReceiverInput.Parse(ManualAddress.Text, ManualPort.Text);
        var feedback = manual.Feedback(manualSelected && manualAddressTouched, manualSelected && manualPortTouched);
        SetError(ManualAddressError, feedback.Address);
        SetError(ManualPortError, feedback.Port);
        if (manualSelected) endpoint = manual.Endpoint;
        else
        {
            try { endpoint = receiverSelection.Resolve(ManualAddress.Text, ManualPort.Text); }
            catch (ArgumentException error) { endpointError = receiverSelection.Selected == null ? null : error.Message; }
        }
        SetError(ReceiverError, endpointError);
        Play.IsEnabled = !busy && source != null && input.Settings != null && endpoint != null;
    }

    private static void SetError(TextBlock label, string? error)
    {
        label.Text = error ?? "";
        label.Visibility = error == null ? Visibility.Collapsed : Visibility.Visible;
    }

    private async void ChooseSource_Click(object sender, RoutedEventArgs args)
    {
        picking = true;
        SetBusy(true);
        try
        {
            var picker = new GraphicsCapturePicker();
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
            var selected = await picker.PickSingleItemAsync();
            if (selected == null || closing) return;
            SetSource(selected);
        }
        catch (Exception error) { Report(error); }
        finally
        {
            picking = false;
            SetBusy(false);
            if (closing) Close();
        }
    }

    private void SetSource(GraphicsCaptureItem selected)
    {
        source = selected;
        SourceLabel.Text = $"{selected.DisplayName} ({selected.Size.Width} x {selected.Size.Height})";
        UpdateValidation();
    }

    private CaptureSettings ReadSettings()
    {
        var input = ReadInput();
        return input.Settings ?? throw new ArgumentException(string.Join(" ",
            new[] { input.DimensionError, input.BitrateError, input.FpsError }.OfType<string>()));
    }

    private async void Play_Click(object sender, RoutedEventArgs args)
    {
        if (cast != null || closing || picking) return;
        if (receiverSelection.Selected?.IsManual == true) manualAddressTouched = manualPortTouched = true;
        Problem.IsOpen = false;
        castStop = new CancellationTokenSource();
        SetBusy(true);
        try
        {
            var selectedSource = source ?? throw new ArgumentException("Choose a display or window first.");
            var endpoint = receiverSelection.Resolve(ManualAddress.Text, ManualPort.Text);
            var settings = ReadSettings();
            SaveSettings(settings);
            Status.Text = "Checking hardware encoder";
            var token = castStop.Token;
            var lease = castStop;
            cast = Task.Run(async () =>
            {
                await using var video = new WindowsVideoSource(selectedSource, settings);
                await using var audio = settings.EnableAudio ? new WindowsAudioSource(settings.MediaTransport == "srt") : null;
                await using var srt = settings.MediaTransport == "srt" ? new SrtMedia(Log) : null;
                DispatcherQueue.TryEnqueue(() =>
                {
                    if (ReferenceEquals(castStop, lease))
                        EncoderLabel.Text = $"{video.EncoderName} - {video.Width} x {video.Height}";
                });
                token.ThrowIfCancellationRequested();
                await new SenderSession().RunAsync(endpoint, settings, video, text =>
                {
                    Log($"Session: {text}");
                    DispatcherQueue.TryEnqueue(() =>
                    {
                        if (ReferenceEquals(castStop, lease)) Status.Text = text;
                    });
                }, token, audio, srt);
            });
            await cast;
            Status.Text = "Idle";
        }
        catch (OperationCanceledException) when (castStop.IsCancellationRequested) { Status.Text = "Stopped"; }
        catch (Exception error) { Status.Text = "Failed"; Report(error); }
        finally
        {
            cast = null;
            castStop.Dispose();
            castStop = null;
            SetBusy(false);
            if (closing) Close();
        }
    }

    private void Stop_Click(object sender, RoutedEventArgs args)
    {
        Stop.IsEnabled = false;
        Status.Text = "Stopping";
        castStop?.Cancel();
    }

    private void SetBusy(bool busy)
    {
        this.busy = busy;
        ChooseSource.IsEnabled = Receivers.IsEnabled = ManualAddress.IsEnabled = ManualPort.IsEnabled =
            MaxSize.IsEnabled = MaxFps.IsEnabled = Bitrate.IsEnabled = ReceiverOrientation.IsEnabled =
            VideoCodec.IsEnabled = EnableAudio.IsEnabled = MediaTransport.IsEnabled = !busy;
        Stop.IsEnabled = busy && !picking;
        UpdateValidation();
    }

    private void Window_Closing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        closing = true;
        if (cast != null || picking)
        {
            args.Cancel = true;
            castStop?.Cancel();
            Status.Text = "Stopping before closing";
        }
        else lifetime.Cancel();
    }

    private void LoadSettings()
    {
        var path = Path.Combine(AppLog.DirectoryPath, "settings.json");
        if (!File.Exists(path)) return;
        try
        {
            var settings = JsonSerializer.Deserialize<CaptureSettings>(File.ReadAllText(path))
                ?? throw new InvalidDataException("Saved settings are empty.");
            settings.Validate();
            MaxSize.Text = settings.MaxSize.ToString(CultureInfo.CurrentCulture);
            SelectFrameRate(settings.MaxFps);
            Bitrate.Text = (settings.VideoBitrate / 1_000_000m).ToString("0.######", CultureInfo.CurrentCulture);
            ReceiverOrientation.SelectedIndex = settings.ReceiverOrientation switch { "landscape" => 1, "portrait" => 2, _ => 0 };
            VideoCodec.SelectedIndex = settings.VideoCodec switch { "h265" => 1, "av1" => 2, _ => 0 };
            EnableAudio.IsChecked = settings.EnableAudio;
            MediaTransport.SelectedIndex = settings.MediaTransport == "srt" ? 1 : 0;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or ArgumentException or NotSupportedException)
        {
            Report(new IOException($"Could not load saved settings; visible defaults are in use. {error.Message}", error));
        }
    }

    private static void SaveSettings(CaptureSettings settings)
    {
        Directory.CreateDirectory(AppLog.DirectoryPath);
        var path = Path.Combine(AppLog.DirectoryPath, "settings.json");
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(settings));
        File.Move(temporary, path, true);
    }

    internal void RunUiSelfTest()
    {
        if (!LayoutRoot.IsLoaded)
        {
            void Loaded(object sender, RoutedEventArgs args)
            {
                LayoutRoot.Loaded -= Loaded;
                RunUiSelfTest();
            }
            LayoutRoot.Loaded += Loaded;
            return;
        }
        DispatcherQueue.TryEnqueue(async () =>
        {
            static void Require(bool condition, string message)
            {
                if (!condition) throw new InvalidOperationException(message);
            }
            try
            {
                VideoCodec.SelectedIndex = 0;
                EnableAudio.IsChecked = false;
                MediaTransport.SelectedIndex = 0;
                ReceiverOrientation.SelectedIndex = 0;
                Require(SettingsScroll.Parent == LayoutRoot && FooterHost.Parent == LayoutRoot &&
                        Grid.GetRow(SettingsScroll) == 0 && Grid.GetRow(FooterHost) == 1,
                    "Cast controls must be outside the scrolling settings.");
                Require(AppWindow.Presenter is OverlappedPresenter presenter &&
                        presenter.PreferredMinimumWidth is > 0 && presenter.PreferredMinimumHeight is > 0,
                    "DPI-aware minimum window size was not applied.");
                Require(DiscoveryStatus.Text == "No receivers discovered.", "Empty discovery status is misleading.");
                var available = new ReceiverEndpoint(System.Net.IPAddress.Parse("192.0.2.20"), 27184, "Test receiver", false);
                var occupied = new ReceiverEndpoint(System.Net.IPAddress.Parse("192.0.2.10"), 27184, "Busy receiver", true);
                RefreshReceivers([occupied, available]);
                Require(receiverSelection.Selected?.Endpoint?.Key == available.Key, "First available receiver was not selected.");
                Require(receiverSelection.Choices.Last().IsManual, "Manual entry is not last.");
                Require(ManualEntry.Visibility == Visibility.Collapsed, "Manual fields should be hidden.");
                source = MediaDiagnostics.OwnWindow(WinRT.Interop.WindowNative.GetWindowHandle(this));
                MaxSize.Text = "720";
                Bitrate.Text = 8.5m.ToString(CultureInfo.CurrentCulture);
                Require(((IEnumerable<int>)MaxFps.ItemsSource).Contains(90) &&
                        ((IEnumerable<int>)MaxFps.ItemsSource).Contains(120), "High-FPS presets are missing.");
                MaxFps.SelectedItem = 120;
                await Task.Delay(50);
                Require(Play.IsEnabled && ReadSettings() == new CaptureSettings(720, 120, 8_500_000), "Valid inputs were not applied exactly.");
                MaxSize.Text = "720.5";
                await Task.Delay(50);
                Require(!Play.IsEnabled && DimensionError.Visibility == Visibility.Visible && MaxSize.Text == "720.5",
                    "Invalid dimensions were not reported without clamping.");
                MaxSize.Text = "720";
                Bitrate.Text = "NaN";
                await Task.Delay(50);
                Require(!Play.IsEnabled && BitrateError.Visibility == Visibility.Visible, "Invalid bitrate was accepted.");
                Bitrate.Text = 8.5m.ToString(CultureInfo.CurrentCulture);
                Receivers.SelectedItem = ReceiverChoice.Manual;
                await Task.Delay(50);
                Require(ManualEntry.Visibility == Visibility.Visible, "Manual fields were not shown.");
                Require(!Play.IsEnabled && ManualAddressError.Visibility == Visibility.Collapsed &&
                        ManualPortError.Visibility == Visibility.Collapsed && ReceiverError.Visibility == Visibility.Collapsed,
                    "Untouched manual fields should show only a neutral hint.");
                ManualAddress_LostFocus(ManualAddress, new RoutedEventArgs());
                Require(ManualAddressError.Text == "Enter the receiver's IP address." &&
                        ManualAddressError.Visibility == Visibility.Visible && ManualPortError.Visibility == Visibility.Collapsed,
                    "An empty touched address must show a specific IP error, not a port error.");
                ManualAddress.Text = "127.0.0.1";
                ManualPort.Text = "65536";
                await Task.Delay(50);
                Require(!Play.IsEnabled && ManualPortError.Visibility == Visibility.Collapsed && ManualPort.Text == "65536",
                    "An untouched port should not show an error yet.");
                ManualPort_LostFocus(ManualPort, new RoutedEventArgs());
                Require(!Play.IsEnabled && ManualAddressError.Visibility == Visibility.Collapsed &&
                        ManualPortError.Visibility == Visibility.Visible && ManualPort.Text == "65536",
                    "Invalid port was not reported without clamping.");
                ManualPort.Text = "27184";
                await Task.Delay(50);
                Require(Play.IsEnabled && ManualPortError.Visibility == Visibility.Collapsed, "Valid manual entry cannot be used.");
                ManualAddress.Text = "invalid";
                Receivers.SelectedItem = receiverSelection.Choices.First(choice => choice.Endpoint?.Key == available.Key);
                await Task.Delay(50);
                Require(Play.IsEnabled && ManualEntry.Visibility == Visibility.Collapsed &&
                        ManualAddressError.Visibility == Visibility.Collapsed && ManualPortError.Visibility == Visibility.Collapsed,
                    "Hidden manual input still overrides discovery or shows stale errors.");
                Require(VideoCodec.Parent != ManualEntry, "Codec selection must not be hidden with manual entry.");
                VideoCodec.SelectedIndex = 1;
                Require(ReadSettings().VideoCodec == "h265", "HEVC choice was substituted.");
                VideoCodec.SelectedIndex = 2;
                EnableAudio.IsChecked = true;
                await Task.Delay(50);
                Require(ReadSettings() is { VideoCodec: "av1", EnableAudio: true }, "AV1/audio choices were not applied.");
                VideoCodec.SelectedIndex = 0;
                EnableAudio.IsChecked = false;
                RefreshReceivers([available with { Name = "Renamed" }, occupied]);
                Require(receiverSelection.Selected?.Endpoint?.Key == available.Key, "Discovery refresh changed the selected endpoint.");
                RefreshReceivers([]);
                Require(!Play.IsEnabled && ReceiverError.Visibility == Visibility.Visible, "Expired discovery was silently redirected.");
                Receivers.SelectedItem = ReceiverChoice.Manual;
                await Task.Delay(50);
                RefreshReceivers([available]);
                Require(receiverSelection.Selected?.IsManual == true && ManualEntry.Visibility == Visibility.Visible,
                    "Discovery replaced the explicit manual choice.");
                Log("UI_SELF_TEST_PASS: initial receiver selection, stable identity, conditional manual entry, inline numeric validation, and 90/120 FPS presets. No capture, network connection or settings save.");
            }
            catch (Exception error)
            {
                Log($"UI_SELF_TEST_FAIL: {error}");
                Environment.ExitCode = 1;
            }
            finally { Close(); }
        });
    }

    internal void RunCaptureSelfTest()
    {
        var timer = DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(50);
        timer.IsRepeating = true;
        var tick = 0;
        timer.Tick += (_, _) => Heading.Text = $"Capture test {++tick}";
        timer.Start();
        DispatcherQueue.TryEnqueue(async () =>
        {
            try
            {
                var item = MediaDiagnostics.OwnWindow(WinRT.Interop.WindowNative.GetWindowHandle(this));
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                var settings = new CaptureSettings(MaxSize: 640);
                var frames = await Task.Run(async () =>
                {
                    var total = 0;
                    for (var iteration = 0; iteration < 3; iteration++)
                    {
                        var resizeDelta = iteration % 2 == 0 ? 80 : -80;
                        await using var video = new WindowsVideoSource(item, settings);
                        try
                        {
                            total += await MediaDiagnostics.LoopbackAsync(video, settings, text =>
                            {
                                Log($"Self-test session: {text}");
                                if (text == "Streaming") DispatcherQueue.TryEnqueue(() =>
                                    AppWindow.Resize(new SizeInt32(AppWindow.Size.Width + resizeDelta, AppWindow.Size.Height)));
                            }, timeout.Token);
                        }
                        finally { Log(video.Statistics); }
                    }
                    return total;
                });
                if (frames < 30) throw new IOException("Three capture sessions produced fewer than 30 encoded frames.");
                Log($"CAPTURE_SELF_TEST_PASS: {frames} own-window frames across 3 GPU/hardware H.264 localhost sessions with source resizing; readiness and Stop acknowledged; no desktop capture or LAN transmission.");
            }
            catch (Exception error)
            {
                Log($"CAPTURE_SELF_TEST_FAIL: {error}");
                Environment.ExitCode = 1;
            }
            finally { Log($"Capture self-test UI ticks: {tick}."); timer.Stop(); Close(); }
        });
    }
}
