# ScrcpySend for Windows — privacy statement

ScrcpySend sends a selected Windows display/window and, when enabled, system
playback audio to a receiver you choose on a trusted private local network.
The primary display is selected at launch, but capture/transmission starts only
after you press Play and the receiver accepts the session. Use Stop or close the
application normally to end sharing.

## Data handled by the application

- Captured screen content and optional playback audio are held in bounded memory
  and sent to the selected receiver. Normal casting does not save screen/audio
  recordings to disk or upload them to a developer service.
- UDP discovery receives receiver names, IP addresses, ports and availability.
  Casting uses the actual selected endpoint, not a name-based redirection.
- Applied capture/codec/transport preferences are saved locally in
  `%LOCALAPPDATA%\ScrcpySend-Windows\settings.json`. The receiver selection and
  source selection are not persisted across application launches.
- Rotating local diagnostics may include receiver/network information, selected
  source dimensions, codec configuration, timestamps, local paths and errors.
  They are stored under `%LOCALAPPDATA%\ScrcpySend-Windows`, are not uploaded
  automatically, and should be reviewed/redacted before sharing for support.
- No account, microphone capture, remote input, clipboard forwarding or developer
  analytics service is required by the application.

## Network and audio considerations

Discovery, control and media traffic do **not** authenticate peers or encrypt
content. Use only a trusted private network; other network participants may
observe or spoof traffic. Do not expose receiver ports to the internet.
The selected receiver can display or otherwise process the content you send.

When audio is enabled, Windows playback outputs are muted while sound is sent
to the receiver. The application restores changes it still owns on normal Stop
and handled failures while preserving later user settings. Forced termination,
power loss or a process crash may leave an output muted.

## Dependencies and distribution

Microsoft Store and Microsoft Windows/.NET/Windows App SDK services have their
own privacy and diagnostics policies. The application does not claim to disable
operating-system, Store or runtime telemetry.

Developer diagnostic commands are explicit opt-in tools. Some produce synthetic
test media files or capture only the diagnostic application's own window; they
are not part of ordinary casting and do not automatically upload their outputs.

This statement describes the current application. Review it against the released
package and complete the Store's privacy/data declarations before publication.
