# Personal Keypad

A small native Windows app that makes two USB keypad buttons open your chosen
programs, shortcuts, files or folders. It was built for Galatea Talk and Stop,
with a Browse-and-Save workflow inspired by Microsoft Mouse and Keyboard Center.

No vendor software, additional driver, package-manager runtime, cloud service
or telemetry is used. Full C# source is included.

## Use

1. Extract the Windows package to a writable folder and open `PersonalKeypad.exe`.
2. Browse to each button's target, then select **Save**.
3. If this supported keypad has not been prepared, use **Inspect keypad**, then
   **Prepare keypad**. Preparation backs up the original configuration and assigns
   F13/F14 to the two buttons. Keep the generated `data` folder.
4. Enable **Physical button test** to check both buttons without opening targets.
   Turn testing off when ready to use the saved assignments.

The suggested initial paths point to Galatea's `ButlerTalk.exe` and
`ButlerStop.exe` under the current user's Documents folder. Use Browse if your
installation is elsewhere. Galatea is not included.

**Test** opens the saved target; it does not use unsaved edits. Local Windows
shortcuts (`.lnk`) can supply program arguments. The path fields themselves
accept one existing local file or folder, without command-line arguments.

Closing the settings window keeps the launcher in the notification area.
Reopen the executable or use its tray menu to return to Settings; choose
**Exit** to stop it. **Start with Windows** is optional and takes effect on Save.
Disable it and Save before moving the installation folder.

## Supported hardware

The programmer is narrowly scoped to a tested two-key keypad with VID `514C`,
PID `8851`, a vendor configuration collection on `MI_00` (usage page `FF00`,
usage `1`), 65-byte input/output reports, and the supported legacy reply format.
It is not a general-purpose programmer for every device sold under that VID/PID.

Preparation requires two identical complete configuration reads and a saved
backup before writing. Only layer 1, physical slots 1 and 2 are changed. All
other stored records are compared after programming. There are no lighting,
geometry, bootloader or firmware commands.

**Restore previous bindings** restores those two records from this device's
saved backup. It refuses mismatched devices, invalid backups or later unrelated
configuration changes. Keep the backup local; never substitute another device's
backup.

F13/F14 are global Windows hotkeys, not device-specific events. Another keyboard
emitting them can trigger the assignments. Registration conflicts are reported
in the window. The launcher must remain running to open paths.

## Build and test

Requirements: Windows with .NET Framework 4.5 or later, its installed C# compiler,
and PowerShell. The tested machine has a newer .NET Framework installation.

```powershell
.\Test.ps1 -IncludeUi
.\Build.ps1
```

Output: `dist\Personal Keypad\PersonalKeypad.exe` and `KeypadControl.exe`.
The console executable supports `--inspect`, `--prepare` and `--restore`.
The GUI supports `--test-mode` and `--minimized`.

Tests use simulated transport/dispatch and do not program a keypad or launch
Galatea. `-IncludeUi` adds a native form harness and produces local UI captures.
An ordinary build does not change device bindings or existing settings.

The binaries are locally compiled and unsigned. No licence has been granted
for redistribution at this stage.

## Verification and privacy

See [VERIFICATION.md](VERIFICATION.md) for automated checks, independent review,
physical observations and the remaining human checks. A successful request to
open a helper is distinct from confirmed microphone/listening behavior.

Personal settings, device serials, raw HID readbacks, physical-event logs,
original device backups and the downloaded vendor archive are excluded from
this repository and its downloadable package. The app creates its own `data`
folder when used. It does not record general keyboard input.

Protocol sources and implementation notes are in [REFERENCES.md](REFERENCES.md).
