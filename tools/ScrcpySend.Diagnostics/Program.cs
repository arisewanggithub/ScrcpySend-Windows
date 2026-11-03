using ScrcpySend.Media;

try
{
    SrtMedia.NativeDiagnostic += Console.Error.WriteLine;
    var fps = 60;
    var codec = "h264";
    string? srtOutput = null;
    var width = 640; var height = 360;
    var bitrate = 8_000_000; var videoFirst = false;
    string? muxOutput = null;
    for (var index = 0; index < args.Length; index++)
    {
        if (args[index] == "--self-test") continue;
        if (args[index] == "--audio-probe") continue;
        if (args[index] == "--display-probe") continue;
        if (args[index] == "--native-probe") continue;
        if (args[index] == "--av-probe") continue;
        if (args[index] == "--srt-probe" && ++index < args.Length) { srtOutput = Path.GetFullPath(args[index]); continue; }
        if (args[index] == "--width" && ++index < args.Length && int.TryParse(args[index], out width) && width is > 0 and <= 3840) continue;
        if (args[index] == "--height" && ++index < args.Length && int.TryParse(args[index], out height) && height is > 0 and <= 3840) continue;
        if (args[index] == "--bitrate" && ++index < args.Length && int.TryParse(args[index], out bitrate) && bitrate is >= 100_000 and <= 100_000_000) continue;
        if (args[index] == "--video-first") { videoFirst = true; continue; }
        if (args[index] == "--mux-regression" && ++index < args.Length) { muxOutput = Path.GetFullPath(args[index]); continue; }
        if (args[index] == "--fps" && ++index < args.Length && int.TryParse(args[index], out fps) && fps is >= 1 and <= 120) continue;
        if (args[index] == "--codec" && ++index < args.Length && args[index] is "h264" or "h265" or "av1") { codec = args[index]; continue; }
        throw new ArgumentException("Usage: ScrcpySend.Diagnostics [--self-test|--audio-probe|--av-probe] [--fps 1-120] [--codec h264|h265|av1]");
    }
    if (args.Count(argument => argument is "--self-test" or "--audio-probe" or "--av-probe") > 1)
        throw new ArgumentException("Choose one diagnostic mode.");
    Console.WriteLine($"Process: {System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture}");
    if (args.Contains("--native-probe"))
    {
        using var nativeTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        Console.WriteLine(await MediaDiagnostics.NativeRuntimeProbeAsync(nativeTimeout.Token));
        return 0;
    }
    if (args.Contains("--display-probe"))
    {
        var item = WindowsVideoSource.SelectDefaultDisplay();
        if (item.Size.Width <= 0 || item.Size.Height <= 0) throw new IOException("Default display dimensions are invalid.");
        Console.WriteLine($"Default source: {item.DisplayName} ({item.Size.Width} x {item.Size.Height}). Capture item selected only: no frame pool, capture session, encoding, audio or transmission started.");
        return 0;
    }
    if (muxOutput != null)
    {
        using var muxTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        Console.WriteLine(await MediaDiagnostics.MuxTimestampRegressionAsync(muxOutput, muxTimeout.Token));
        return 0;
    }
    if (srtOutput != null)
    {
        using var srtTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        Console.WriteLine(await MediaDiagnostics.SrtProbeAsync(codec, srtOutput, srtTimeout.Token, width, height, bitrate, videoFirst));
        return 0;
    }
    if (args.Contains("--av-probe"))
    {
        using var avTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        Console.WriteLine(await MediaDiagnostics.AudioVideoProbeAsync(codec, avTimeout.Token, fps));
        return 0;
    }
    if (args.Contains("--audio-probe"))
    {
        using var audioTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        Console.WriteLine(await WindowsAudioSource.ProbeAsync(audioTimeout.Token));
        return 0;
    }
    var names = HardwareEncoder.EnumerateNames(codec);
    Console.WriteLine($"Native hardware {codec} encoders: {names.Count}");
    foreach (var name in names) Console.WriteLine($"  {name}");
    if (args.Contains("--self-test"))
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        Console.WriteLine(await MediaDiagnostics.SelfTestAsync(timeout.Token, fps, codec));
    }
    if (names.Count == 0)
    {
        Console.Error.WriteLine($"No native hardware {codec} encoder is available. Software video encoding is not allowed.");
        return 2;
    }
    return 0;
}
catch (Exception error)
{
    Console.Error.WriteLine(error);
    return 1;
}
