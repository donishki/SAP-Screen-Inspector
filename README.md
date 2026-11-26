# SAP Screen Inspector 1.5.0

A portable Windows tool for recording SAP GUI actions and inspecting controls.

## Record a sequence

1. Open SAP GUI for Windows and log in.
2. Run the inspector in the same Windows desktop/session as SAP.
3. Select the SAP session and press **Record**.
4. Perform all your steps in SAP. No per-step hotkey is needed.
5. Return and press **Stop** to collect the final pending actions.
6. Review the ordered timeline and choose **Save script** or **JSON**.

**Resume recording** appends to the same session. **New sequence** starts fresh after checking for unsaved changes. Ctrl+Alt+F8 also starts/stops recording when available.

The recorder uses native SAP GUI Record/Change events. It retains property changes, button presses, virtual keys, selections, and grid/table methods supplied by SAP in order. SAP batches events until server communication or Stop; the count may not increase immediately. Timestamps indicate event delivery time.

## Requirements and limitations

- SAP GUI Scripting AND recording permission are required. Disabled recording, read-only scripting and an already active recorder produce an explicit error. The app cannot bypass SAP permissions.
- Recording includes action values such as entered text and selected keys. **Screen values** applies only to screen snapshots, not recorded arguments. Password control commands are omitted without reading their arguments or Text property.
- The starting screen is a full control snapshot. Later events contain the affected control and command, not full historical screen snapshots. Session metadata comes from the starting snapshot.
- Only actions exposed by SAP scripting are recorded. OS dialogs, unsupported controls and backend-preset values may not produce events. Review scripts before running them.
- Undecodable events add an error step and mark the sequence incomplete.
- The app enables/disables recording but does not replay commands, edit fields or change server permissions. SAP recording mode can itself affect F4 dialogs and disable drag-and-drop.
- Stop before exporting. Closing stops recording and warns about unexported data. If Stop fails, retry when SAP is responsive. Sequences remain in memory until exported.
- JSON preserves native commands and supports legacy manual sequences. Recorded commands are read-only in the action editor; legacy manual steps remain editable.
- Exports contain session identity, metadata and recorded inputs. Keep real captures private. Never commit captures or credentials.
- Run inside the same Citrix/RDP environment as SAP when applicable.

## Screen inspection

Expand **Inspect screen controls** to capture, search, browse and copy IDs or inspect grid column IDs. Only instantiated controls are available; hidden tabs and virtualized rows require navigation. Optional auto-refresh and manual screen capture remain available while recording is stopped. Screen values are excluded by default.

## Build and verify

Install the .NET 8 SDK on Windows:

```powershell
dotnet build -c Release
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -o releases/win-x64
dotnet publish -c Release -r win-x86 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -o releases/win-x86
```

Both packages are portable; no separate .NET installation is needed. Choose the architecture compatible with your SAP scripting installation.

Run each executable with `--self-test` and `--smoke`. `--demo` opens sample data without SAP. Tests cover command rendering, chaining, quoting, ordering, Stop flush, cleanup, permissions, existing-recorder protection, password exclusion, reader/export behavior, timeline and JSON roundtrip.

Tests use simulated SAP objects. Live COM event delivery and installation-specific compatibility must be checked in your SAP environment.

## Source

- SapRecorder.cs: native COM events, recorder lifecycle and command rendering.
- StepCapture.cs: Record/Stop, timeline and export.
- Reader.cs and SapDispatch.cs: discovery and screen inspection.
- ScriptExport.cs: recorded commands and legacy manual script generation.
- Main.xaml, Program.cs and WorkspaceDesign.cs: WPF workspace.
- RecorderTests.cs, SelfTests.cs and ScriptExportTests.cs: simulated checks.

API reference: [SAP GUI Scripting API](https://help.sap.com/doc/9215986e54174174854b0af6bb14305a/800.04/en-US/sap_gui_scripting_api.pdf), GuiSession Record and Change events.

Independent project; not affiliated with or endorsed by SAP.
