# SAP Screen Inspector 1.6.1

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
- Undecodable native events, observation queue overflow and persistent sampling failures add error steps. Transient busy/partial/timeout samples are retried. Recently verified window/process scope is retained for up to 30 seconds, without stale control targets; identity loss or non-transient failure suspends input immediately. Recovery adds a sampling note. A reported gap remains visible after recovery.
- Native mode enables/disables SAP recording. Observed mode never changes SAP recording or server settings. Neither mode replays commands or edits SAP fields. SAP native recording mode can itself affect F4 dialogs and disable drag-and-drop.
- Stop before exporting. Closing stops recording and warns about unexported data. If Stop fails, retry when SAP is responsive. Sequences remain in memory until exported.
- JSON preserves native commands, observation review markers and legacy manual sequences. Inferred actions export as TODO comments only; no replay command is generated from an inferred target. Notes and action labels may be edited, but the review marker remains.
- Exports contain session identity, metadata and recorded inputs. Keep real captures private. Never commit captures or credentials.
- Run inside the same Citrix/RDP environment as SAP when applicable.

## Observed recording when SAP recording is disabled

Press **Record**, perform your steps in SAP, then **Stop**. The app selects this fallback automatically when SAP reports recording disabled. You can also choose **Settings > Use observed recording (approximate)** before starting.

A Windows input monitor observes clicks, scrolling and navigation keys only in sampled windows belonging to the selected SAP session and process. Other applications and other SAP session windows are excluded. Printable keys are discarded, not translated or stored. Input hooks run on a dedicated thread and are removed immediately on Stop.

Read-only SAP screen samples run approximately every 750 ms plus read time. Observed sampling skips hidden menu trees, tooltips and grid-column enumeration to reduce COM work. Only editable text fields, combos, checkboxes and radio buttons produce field-change steps. Menu captions, titles and read-only values do not count as edits; navigation produces one screen-change step rather than a burst of initial-value changes. Full manual inspection still includes menu and column metadata.

Sampling allows 15 seconds initially and 25 seconds on a retry. Partial samples never replace the last complete baseline. Short busy intervals are retried without an immediate error step; repeated failures are grouped until recovery. Stop removes input monitoring immediately, cancels the in-flight read, and tries up to two final reads of 10 seconds each.

Enable **Screen values** before Record if you need sampled field contents. Without values, a field's Modified flag can indicate an edit, but repeated edits may not be distinguishable.

**This is approximate documentation, not a complete native SAP recording.** Fast actions between samples, transient dialogs and complex grids may be missed. Target matching uses sampled native control handles when available, compensates for window movement, and rejects resized or older-than-four-second bounds. The whole window is never used as a click-target fallback; unresolved clicks retain their position and window context. Double-clicks appear as separate clicks. SAP-generated field changes can resemble user edits. Newly opened windows are observed only after a successful sample discovers them. Unsupported OS dialogs are not monitored. Review the timeline; exported observations are comments and need manual completion before replay. Export includes an observed-input/field/screen/gap summary and per-observation timestamps.

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
- RecorderTests.cs, ObservationTests.cs, SelfTests.cs and ScriptExportTests.cs: simulated checks, including caption-noise filtering, stale/resized targeting, partial-screen retries and slow-response recovery.

API reference: [SAP GUI Scripting API](https://help.sap.com/doc/9215986e54174174854b0af6bb14305a/800.04/en-US/sap_gui_scripting_api.pdf), GuiSession Record and Change events.

Independent project; not affiliated with or endorsed by SAP.
