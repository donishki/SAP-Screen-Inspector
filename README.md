# SAP Screen Inspector 1.6.0

A portable Windows tool for recording SAP GUI actions and inspecting controls.

## Record a sequence

1. Open SAP GUI for Windows and log in.
2. Run the inspector in the same Windows desktop/session as SAP.
3. Select the SAP session and press **Record**.
4. Perform all your steps in SAP. No per-step hotkey is needed.
5. Return and press **Stop** to collect the final pending actions.
6. Review the ordered timeline and choose **Save script** or **JSON**.

**Resume recording** appends to the same session. **New sequence** starts fresh after checking for unsaved changes. Ctrl+Alt+F8 also starts/stops recording when available.

When SAP allows native recording, the recorder uses SAP GUI Record/Change events. It retains property changes, button presses, virtual keys, selections, and grid/table methods supplied by SAP in order. SAP batches events until server communication or Stop; the count may not increase immediately. Timestamps indicate event delivery time.

## Requirements and limitations

- SAP GUI Scripting read access is required. Native recording additionally requires recording permission. If recording is disabled, the app automatically uses observed mode without enabling SAP Record or subscribing to SAP events. An already active native recorder is left alone.
- Native recording includes action values such as entered text and selected keys. In observed mode, **Screen values** controls whether sampled field values are included; it is locked while recording. Password controls remain excluded. Raw typed characters and clipboard contents are never collected by the input monitor.
- The starting screen is a full control snapshot. Native events contain the affected control and command. Observed inputs reference the last sampled screen; sampled changes include the resulting screen. Input timestamps and sample timestamps may differ from the time SAP processed an action.
- Only actions exposed by SAP scripting are recorded. OS dialogs, unsupported controls and backend-preset values may not produce events. Review scripts before running them.
- Undecodable native events, observation queue overflow and screen sampling failures add error steps and mark the sequence incomplete. After a failed screen sample, input observation is suspended until a valid sample re-establishes session/window identity.
- Native mode enables/disables SAP recording. Observed mode never changes SAP recording or server settings. Neither mode replays commands or edits SAP fields. SAP native recording mode can itself affect F4 dialogs and disable drag-and-drop.
- Stop before exporting. Closing stops recording and warns about unexported data. If Stop fails, retry when SAP is responsive. Sequences remain in memory until exported.
- JSON preserves native commands, observation review markers and legacy manual sequences. Inferred actions export as TODO comments only; no replay command is generated from an inferred target. Notes and action labels may be edited, but the review marker remains.
- Exports contain session identity, metadata and recorded inputs. Keep real captures private. Never commit captures or credentials.
- Run inside the same Citrix/RDP environment as SAP when applicable.

## Observed recording when SAP recording is disabled

Press **Record**, perform your steps in SAP, then **Stop**. The app selects this fallback automatically when SAP reports recording disabled. You can also choose **Settings > Use observed recording (approximate)** before starting.

A Windows input monitor observes clicks, scrolling and navigation keys only in sampled windows belonging to the selected SAP session and process. Other applications and other SAP session windows are excluded. Printable keys are discarded, not translated or stored. Input hooks run on a dedicated thread and are removed immediately on Stop.

Read-only SAP screen samples run approximately every 750 ms plus the time needed to read the screen. These detect screen navigation and field changes. Enable **Screen values** before Record if you need sampled field contents. Without values, a field's Modified flag can indicate an edit, but repeated edits may not be distinguishable. Stop cancels an in-flight sample, drains queued inputs, and attempts a final sample with a six-second timeout.

**This is approximate documentation, not a complete native SAP recording.** Fast actions between samples, transient dialogs, moving/scaled windows and complex grids may be missed or associated with stale bounds. Double-clicks appear as separate clicks. SAP-generated field changes can resemble user edits. Newly opened windows are observed only after a successful sample discovers them. Unsupported OS dialogs are not monitored. Review the timeline; exported observations are comments and need manual completion before replay.

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

Run each executable with `--self-test` and `--smoke`. `--demo` opens sample data without SAP. Tests cover native command rendering and lifecycle, observation scope and handle reuse, printable-key exclusion, password exclusion, sampled changes, comment-only inference export, Stop cancellation/final sampling, actual Windows hook installation/removal on a synthetic demo window, timeline and JSON roundtrip.

Tests use simulated SAP objects. Live COM event delivery and installation-specific compatibility must be checked in your SAP environment.

## Source

- SapRecorder.cs: native COM events, recorder lifecycle and command rendering.
- ObservedRecorder.cs: scoped Windows input monitor, screen sampling and inferred steps.
- StepCapture.cs: Record/Stop, timeline and export.
- Reader.cs and SapDispatch.cs: discovery and screen inspection.
- ScriptExport.cs: recorded commands and legacy manual script generation.
- Main.xaml, Program.cs and WorkspaceDesign.cs: WPF workspace.
- RecorderTests.cs, SelfTests.cs and ScriptExportTests.cs: simulated checks.

API reference: [SAP GUI Scripting API](https://help.sap.com/doc/9215986e54174174854b0af6bb14305a/800.04/en-US/sap_gui_scripting_api.pdf), GuiSession Record and Change events.

Independent project; not affiliated with or endorsed by SAP.
