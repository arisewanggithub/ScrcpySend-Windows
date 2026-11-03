# ScrcpySend for Windows

Native ARM64 and x64 WinUI 3 sender for the existing Android
[ScrcpyRecv](https://github.com/arisewanggithub/ScrcpyRecv), on a trusted private
LAN. This is a separate application from the
[Android sender](https://github.com/arisewanggithub/ScrcpySend), not an official
scrcpy application.

## Current milestone

- Windows 11 22H2 or later, **native ARM64 and x64 builds**.
- WinUI 3, Windows App SDK **2.6.4**, .NET 10.
- The primary display is selected by default; the system picker can choose
  another window or display. No capture starts before Play and
  the receiver's explicit session acceptance.
- Direct3D 11 capture surfaces and GPU BGRA-to-NV12 scaling/conversion feed a
  **hardware-only H.264, H.265 or AV1** Media Foundation encoder. No WARP device, CPU bitmap
  readback, software video encoder, or codec substitution.
- H.264 Baseline and HEVC Main configuration/dimensions and I/P slices are
  validated. AV1 Main uses real sequence-header OBUs and displayed keyframes,
  respecting sequence size limits versus actual fixed frame size.
  B/SP/SI, hidden/reordered frames, unsupported configuration changes and
  incompatible elementary-stream formats fail explicitly.
- Protocol-v3 UDP discovery, manual numeric IP, custom TCP media, track
  readiness, liveness, receiver BUSY, Stop and owned cleanup.
- Applied size/FPS/bitrate and receiver-orientation settings persist. Endpoint
  selection, capture consent and active-session state do not persist.
- **Optional audio:** Windows process-loopback capture provides 48 kHz stereo
  signed-16 little-endian PCM. This is the receiver's explicit `raw` custom-TCP
  format, not a silent substitution for AAC/Opus. It adds 1.536 Mbps before
  framing overhead. Audio stays disabled in existing video-only settings.
- **Selectable transport:** custom TCP remains the default. SRT/fMP4 uses
  libsrt 1.5.7 and the pinned FFmpeg 7.1.1 muxer, with explicitly selected
  AAC-LC stereo 48 kHz at 192 kbps when audio is enabled. No PCM-to-AAC
  substitution occurs in custom TCP sessions.

This is not a finished production release. The first implementation does not
include crop, HDR, Opus audio encoding, a tray mode, automatic reconnect,
signed MSIX packaging or an updater. No end-to-end latency claim is made.

The audio contract is the same as Android: **Enable audio on** sends
audio only to the receiver and suppresses local playback; **off** leaves audio
local and sends video only. The app mutes all current active Windows playback
outputs only after both custom TCP tracks are ready, or SRT reports actual
rendered video and advancing enabled audio, and restores only mute changes
it still owns on normal Stop, disconnect and handled failures. Volume-only
notifications do not stop casting or discard mute ownership. New Windows volume
levels are never overwritten. If volume keys or mute controls unmute an output,
the owned capture worker re-suppresses local playback while receiver-only audio
is enabled; the user's latest mute choice is applied when sharing stops.
Volume callbacks only record state and wake the worker, never call back into
the volume API synchronously. Audio-device/routing changes still stop the cast.
Callback registration precedes the initial ownership snapshot; a notification
received during the read supersedes that snapshot. Requested app mutes are
tracked separately from observed output state so an intervening user mute is
not hidden by an optimistic update or a late app acknowledgement. An unchanged
external muted notification is conservatively preserved as a mute request.
Successful suppression also claims restoration ownership if it overwrote a
user unmute that arrived after the app requested mute, including an output
that was already muted before casting.
Use the receiver's controls to adjust cast loudness: process loopback is captured
before Windows endpoint/master volume.

Process-loopback capture is independent of the physical output endpoint, so
muting a hardware endpoint does not mute the captured stream. It captures all
other processes, excluding this sender and its children; the Windows audio
engine supplies real captured silence when nothing is playing. It does not
capture microphones, bypass protected content or record audio to disk.
See Microsoft's [process-loopback sample documentation](https://github.com/microsoft/Windows-classic-samples/tree/main/Samples/ApplicationLoopback).

Use Stop or close the app normally. Force termination, a process crash or power
loss cannot run cleanup and may leave an output muted; check Windows mute
settings if necessary. Physical local-silencing/restoration and real Android
A/V playback remain direct-device acceptance tests, not inferred from a
read-only diagnostic.

## Build and run

Install the .NET 10 SDK. NuGet restores WinUI and Windows SDK build dependencies;
Visual Studio/C++ tools are not required for this C# implementation.
SRT preparation downloads checksum-pinned Zig/CMake/Ninja tools for the build
host to build the bundled libsrt for the requested target architecture, plus
the matching pinned ARM64 or x64 LGPL FFmpeg release.
It does not install drivers, change system PATH or require administrator rights.
Archive downloads use uniquely owned staging files and are promoted into the
cache only after SHA256 verification. Failed transfers clean their staging
files; previously incomplete/invalid cached archives are reported and
redownloaded on retry.

```powershell
dotnet test .\tests\ScrcpySend.Tests\ScrcpySend.Tests.csproj
.\tools\PrepareSrt.ps1
dotnet build .\src\ScrcpySend.App\ScrcpySend.App.csproj -c Release -p:Platform=ARM64
dotnet run --project .\src\ScrcpySend.App\ScrcpySend.App.csproj -c Release -p:Platform=ARM64
```

ARM64 remains the default. To build x64 from either an ARM64 or x64 Windows host:

```powershell
.\tools\PrepareSrt.ps1 -Architecture x64
dotnet build .\src\ScrcpySend.App\ScrcpySend.App.csproj -c Release -r win-x64 -p:Platform=x64
.\tools\Publish.ps1 -Architecture x64
.\artifacts\win-x64\ScrcpySend.App.exe
```

Each package includes only its target architecture's native FFmpeg/libsrt DLLs.
Native loading follows the actual **process** architecture, not the OS
architecture: an x64 build running under emulation on ARM64 still loads x64
libraries. For best performance on ARM64, use the native ARM64 build.
The video policy is identical for both architectures: hardware-only encoding,
no software/codec/FPS fallback. Encoder availability is device/driver-dependent;
AV1 and HEVC are not guaranteed on every x64 PC.

CI builds/tests/packages ARM64 and x64 separately. These remain unsigned portable
packages, not MSIX/Store packages. Store association and certification are a
separate step.
PRs targeting `main` run the read-only **Windows PR CI** workflow, with pinned
actions, tests and self-contained untrimmed packages for both architectures.
The stable **PR build gate** fails if either architecture fails, is cancelled
or is skipped. Superseded PR runs are cancelled; main pushes and manual runs
are also validated. Branch protection requires the gate and an up-to-date
branch before merging. CI retains test diagnostics and unsigned development
packages but never creates a release or uses signing/publishing secrets.
Publishing checks the PE machine type of the executable and native media DLLs
to reject accidentally mixed ARM64/x64 packages.
Publish destinations must be empty: choose a new output directory for each
build, rather than reusing folders containing previous artifacts.

For a portable development build, including the .NET and Windows App SDK
runtimes:

```powershell
.\tools\Publish.ps1
.\artifacts\win-arm64\ScrcpySend.App.exe
```

To leave a running development build untouched, publish to a separate folder:

```powershell
.\tools\Publish.ps1 -OutputDirectory artifacts\win-arm64-ui-update -ArchiveName ScrcpySend-Windows-arm64-ui-update.zip
```

Keep the whole published folder together; the executable is not standalone.
The script creates `artifacts\ScrcpySend-Windows-arm64-dev.zip` or
`artifacts\ScrcpySend-Windows-x64-dev.zip` and includes
available dependency license/notice files. Development builds are unsigned.
Managed-device application controls may block unsigned portable executables.
Signing/deployment approval is outside this milestone; the app and build scripts
do not change Windows protection or application-control policy.

## Cast

The window launches with a target **900 x 800 DIP client area**, converting
logical size to physical pixels using the current window DPI. Its preferred
minimum is **640 x 480 DIPs** plus the native frame. Both are limited to 90% of
the current monitor's work area so high scaling or small displays never force
an oversized window. Minimums update when DPI/monitor metrics change; restored
windows are kept within the new work area. Play/Stop, status and encoder details
are pinned in a footer outside the scrolling settings.

1. Keep ScrcpyRecv visible on a reachable private LAN.
2. The primary display is selected automatically at launch. Use **Choose a
   display or window...** to change it; cancelling keeps the existing source.
   Default selection only creates the capture item and shows its name/size;
   it does not start capture, encoding or networking. If default selection fails,
   the app reports it and retains the picker as the explicit recovery path.
3. The first available, non-busy discovered receiver is selected initially.
   Choose another receiver, or choose **Manually specify...** (the last list item)
   to show numeric IP/TCP port fields. Hidden manual fields never override a
   discovered receiver. Discovery refreshes preserve the selected endpoint or
   explicit manual choice; an expired selection is shown unavailable, not
   silently redirected to another receiver with the same name.
   Manual entry initially shows a neutral hint and a clearly marked example,
   not a red error. IP/port errors appear beside their own field after it loses
   focus, clear as the input is corrected, and never blame a valid port for a
   missing IP. Play still requires valid actual input, not placeholder text.
4. Choose H.264, H.265 or AV1 and optional audio, then press Play.
   Select custom TCP or SRT/fMP4 explicitly; the latter requires an SRT-enabled
   receiver and uses AAC-LC when audio is on.
   Missing/incompatible hardware or receiver codecs fail visibly,
   with no software fallback. Controls remain disabled until cleanup completes.
5. Press Stop or close the window. Closing while casting waits for owned
   cleanup. A later cast always requires an explicit Play.

Maximum dimension (64-3840 pixels) and bitrate (0.1-100 Mbps, decimals allowed)
are ordinary text fields with inline validation; invalid text is never clamped
or silently replaced and prevents Play. Decimal bitrate uses the UI's current
number format. FPS presets are 15, 24, 25, 30, 50, 60, 90 and 120; new settings
default to 60. Existing saved FPS values, including non-preset rates, are
preserved. These are capture caps, not guaranteed measured FPS: source updates,
GPU/encoder support, network and receiver capacity determine actual performance.
The hardware encoder must accept the requested size/FPS; no lower-FPS or
software-encoder fallback is used.

The encoded canvas stays fixed for a session. Source resizing recreates the
capture frame pool, not the encoder: the new source is aspect-fitted with black
bars into that canvas. Protected content, unavailable capture devices, removed
sources and GPU/encoder failures are not bypassed.

UDP discovery uses port 27183; the receiver's default TCP port is 27184. Names
are labels; discovery uses the packet's actual source address and expires after
six seconds. Windows firewall or Wi-Fi client isolation can block discovery.
Manual IP bypasses discovery, not network reachability or receiver acceptance.
The app does not change firewall policy, require administrator rights, or install
drivers.

**No authentication or encryption:** use only a trusted private LAN. Do not
forward receiver ports to the internet.

## Architecture and performance

### SRT / fragmented MP4

Choose **SRT / fragmented MP4 / AAC-LC audio** explicitly. The Android receiver
must be an SRT-enabled build. Existing settings default to custom TCP; missing
or incompatible SRT acceptance fails without capture or transport fallback.

TCP still carries protocol-v3 control and liveness. After connecting control,
the sender binds an ephemeral SRT listener to the control socket's actual local
IP. The request advertises exactly the paired five-field `srt`/`fmp4` descriptor,
100 ms latency and `sc-` plus the zero-padded session ID. Native admission pins
the caller to the actual control-peer IP and exact stream ID; these are routing
constraints, not authentication. No STREAM_CONFIG/STREAM_READY/media messages
are sent over an SRT session's control TCP connection.

The FFmpeg MP4 muxer writes one initialization segment and standard `moof`/`mdat`
fragments, explicitly flushed every 100 ms even when video becomes static while
audio continues. H.264 uses `avc3`, HEVC uses `hev1`, AV1 uses `av01`, AAC-LC
uses `mp4a`/`esds`. Real encoder configuration/keyframes remain authoritative;
the fixed-format Windows session fails on incompatible configuration changes.
AAC encodes the existing 48 kHz stereo PCM without resampling, with a
sample-count timeline and one shared source-clock origin for A/V offsets.
Automatic mux timestamp shifting is disabled because this shared origin is
already nonnegative. Timed fragment boundaries use independent source-clock
decode-time anchors rather than forcing the next frame to follow an estimated
duration. The last video sample in a timed fragment has a minimal one-tick tail
instead of guessing a nominal-FPS duration that may overlap the next capture
timestamp; earlier sample durations follow actual decode-time differences.
Only an inert AAC sync extension explicitly declaring SBR absent is stripped
from the actual encoder ASC to meet the receiver's canonical AAC-LC schema.

Local mute starts only when the receiver reports `srt-playing` after a real
rendered video frame and advancing enabled audio. Merely connecting SRT or
writing an init segment is not reported as successful playback. The ten-second
startup deadline remains until that status. SRT message payloads are at most
1316 bytes, late-packet dropping is disabled, and one second without write
progress fails explicitly. Stop closes native transport before joining media
workers; there is no automatic retry.

SRT encryption is compiled off to match the current trusted-LAN protocol.
Configured 100 ms transport latency/fragment flushing is not a measured
end-to-end latency guarantee.

```text
WinUI picker / discovery / immutable settings
  -> session-v3 HELLO + acceptance
  -> Windows.Graphics.Capture BGRA D3D11 surface
  -> D3D11 video processor / NV12 GPU surface
  -> asynchronous hardware H.264 / H.265 / AV1 MFT
  -> actual codec configuration + STREAM_CONFIG / STREAM_READY
  -> Annex-B AVC/HEVC or low-overhead AV1 VIDEO_FRAME messages over TCP
  -> existing Android receiver

Optional WASAPI process loopback / shared QPC timestamps
  -> 48 kHz stereo s16le / track 2 config + readiness
  -> owned Windows-output mute lease / AUDIO_PACKET messages
```

Vortice provides typed Windows API bindings; it is not an alternate encoder or
media engine. Video pixels stay GPU-resident through conversion and encoder
input. Only compressed output is copied to managed memory for networking.
Output textures are separately owned so asynchronous encoding never reads a
surface that has been overwritten for a later frame.

Capture is paced before encoding. The pacer preserves rational cadence through timestamp rounding and small jitter
instead of restarting its deadline after every accepted frame. After a long gap,
it restarts its phase without a burst of catch-up frames.
Encoder polling uses a session-owned high-resolution waitable timer, not a
coarse thread-pool delay or a global Windows timer-resolution change.
The latest raw frame deferred by the FPS cap is coalesced in an owned GPU
texture and submitted when its deadline arrives even if the source becomes
static. Original source timestamps are retained; the pacing clock controls
submission, not media timestamps.
Encoded frames are never arbitrarily dropped: total queued media is bounded to
8 MiB and 500 ms per track. Video allows at most 64 queued frames; with audio
enabled, 1 MiB is reserved for its maximum 100 queued PCM packets.
SRT splits that admission budget between 3 MiB of queued video, 512 KiB of
queued PCM, 512 KiB of AAC input and 4 MiB of mux-pending encoded samples;
the native SRT send buffer is separately limited to 1 MiB.
Overflow fails with CONGESTION. A reader and heartbeat run independently of the
serialized writer; startup/write deadlines are ten seconds. An interrupted
partial write closes the connection rather than appending a corrupt Stop frame.
PING/PONG replies use a bounded, supervised queue independent of the control
reader. A blocked media write cannot prevent that reader from receiving STOP.
Late TCP readiness or SRT playback status after cancellation skips startup/mute
side effects without terminating the reader that must consume Stop acknowledgement.
SRT sources that return normally on cancellation still pass through the common
terminal-error check, so receiver failures and missing Stop acknowledgements
cannot be reported as successful completion.
Receiver-initiated Stop cancels media immediately and bounds its acknowledgement
to two seconds even with a backpressured writer. Closing an idle source wakes
the capture worker, and encoder stall timing excludes intervals with no pending
input.

GPU/driver, resolution, source content, network and receiver decoding determine
actual performance. Hardware presence alone does not establish smoothness or
latency.

## Verification

```powershell
# No screen capture: encode generated colors through the real GPU/hardware MFT.
dotnet run --project .\tools\ScrcpySend.Diagnostics\ScrcpySend.Diagnostics.csproj -- --self-test
dotnet run --project .\tools\ScrcpySend.Diagnostics\ScrcpySend.Diagnostics.csproj -- --self-test --fps 120
dotnet run --project .\tools\ScrcpySend.Diagnostics\ScrcpySend.Diagnostics.csproj -- --self-test --codec h265
dotnet run --project .\tools\ScrcpySend.Diagnostics\ScrcpySend.Diagnostics.csproj -- --self-test --codec av1

# Read-only output-control binding checks and real loopback PCM; no muting.
dotnet run --project .\tools\ScrcpySend.Diagnostics\ScrcpySend.Diagnostics.csproj -- --audio-probe

# Real hardware video + captured PCM over localhost; diagnostic does not mute.
dotnet run --project .\tools\ScrcpySend.Diagnostics\ScrcpySend.Diagnostics.csproj -- --av-probe --codec av1

# One static synthetic hardware frame + continuing AAC over matched localhost SRT.
# Also checks that a mismatched stream ID is rejected; no screen capture or muting.
dotnet run --project .\tools\ScrcpySend.Diagnostics\ScrcpySend.Diagnostics.csproj -- --srt-probe .\artifacts\srt-probe.mp4 --codec h264

# Reproduce large AV1 mux configuration and video-before-audio startup with synthetic content.
dotnet run --project .\tools\ScrcpySend.Diagnostics\ScrcpySend.Diagnostics.csproj -- --srt-probe .\artifacts\av1-large.mp4 --codec av1 --width 3456 --height 2160 --bitrate 20000000 --video-first

# Regression for the native second-cast abort: earlier audio, jitter and timed fragment boundaries.
dotnet run --project .\tools\ScrcpySend.Diagnostics\ScrcpySend.Diagnostics.csproj -- --mux-regression .\artifacts\mux-timestamps.mp4

# Test actual WinUI selection/validation controls without capture or networking.
.\src\ScrcpySend.App\bin\ARM64\Release\net10.0-windows10.0.26100.0\win-arm64\ScrcpySend.App.exe --ui-self-test

# Verify default display selection/name/size without starting capture.
dotnet run --project .\tools\ScrcpySend.Diagnostics\ScrcpySend.Diagnostics.csproj -- --display-probe

# Validate the x64 native DLL/AAC/SRT path (no GPU/audio-capture requirement).
dotnet run --project .\tools\ScrcpySend.Diagnostics\ScrcpySend.Diagnostics.csproj -r win-x64 -p:Platform=x64 -- --native-probe

# Open WinUI, capture only this app's own window, test localhost TCP, then exit.
.\src\ScrcpySend.App\bin\ARM64\Release\net10.0-windows10.0.26100.0\win-arm64\ScrcpySend.App.exe --capture-self-test

# Startup/rendering smoke test, without capture.
.\src\ScrcpySend.App\bin\ARM64\Release\net10.0-windows10.0.26100.0\win-arm64\ScrcpySend.App.exe --smoke-test
```

Own-window capture diagnostics refuse windows belonging to another process.
Normal Play uses the default primary display or an explicitly picked source.
Self-tests skip automatic desktop-source selection. Diagnostics never send screen content to a LAN
peer: the capture self-test uses only an ephemeral localhost receiver and checks
real media, source resizing, readiness and acknowledged Stop across three
successive sessions in one process. Errors and test outcomes are written to
`%LOCALAPPDATA%\ScrcpySend-Windows\diagnostics.log`; settings are in the same
directory. These files contain diagnostics/settings, not recorded media.
Native FFmpeg warnings/assertions are forwarded into the same diagnostic log,
along with SRT startup codec configuration and source timestamps. This helps
identify native aborts that cannot be caught as managed exceptions.

Core tests include the Android framing fixtures, strict JSON, discovery bounds,
default/manual/stale receiver selection, numeric validation and culture handling,
rational pacing at 30/60/90/120 FPS (including jitter and slower/faster sources),
explicit SRT negotiation/echo/no-fallback and canonical AAC metadata,
queue pressure, negotiation/readiness, rejected sessions and acknowledged Stop.
The hardware self-test validates actual codec configuration, authentic keyframes
and monotonic output PTS. Audio tests cover two-track readiness, no silent audio
removal, bounded PCM queues and app-owned mute restoration state. Read-only
native probes check endpoint-control/callback bindings and actual PCM capture;
localhost A/V probes use genuine hardware video and WASAPI packets but
intentionally do not mute physical outputs. Actual Android A/V playback,
physical mute/restore behavior, end-to-end latency, sustained high-resolution/high-FPS operation,
network-loss/device-loss/sleep-resume behavior and long-run resource use remain
release gates, not inferred successes from these tests.

## Protocol provenance

`PROTOCOL.md` and `protocol/fixtures/` are pinned copies of the Android sender's
v3 contract. `protocol/upstream.json` records the source revision and contract
hash. The document includes Android-specific capture policy; the Windows
capture implementation is described above. Keep the wire contract aligned
with both Android applications; do not introduce a Windows-only protocol.

See [THIRD_PARTY.md](THIRD_PARTY.md) for dependency licensing.
