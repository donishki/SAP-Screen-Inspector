# SAP Screen Inspector 1.8.0

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
- Native recording includes action values such as entered text and selected keys. In observed mode, **Screen values** controls whether sampled field values are included; it is locked while recording. Selection metadata (row indices, column IDs and node keys) is included independently of that switch. Password controls remain excluded. Raw typed characters and clipboard contents are never collected by the input monitor.
- The starting screen is a control snapshot. Observed snapshots focus on interactive controls and their containers. Native events contain the affected control and command. Observed inputs reference the last sampled screen; sampled changes include the resulting screen. Input timestamps and sample timestamps may differ from the time SAP processed an action.
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

Recording reuses one isolated STA reader process over redirected pipes, avoiding a process launch and temporary JSON files for every sample. Reads remain sequential. The poll interval aims for 500 ms between sample starts, with at least a 100 ms pause after a slow read. Observed input requests an earlier sample after a 60 ms settling delay. Actual throughput depends on SAP response time. A timed-out or cancelled reader is terminated; a later read starts a fresh worker.

Observed sampling skips menu trees, decorative labels, tooltips and grid-column enumeration, fetches each child collection once, and reads bounds only for interactive controls. Only editable text fields, combos, checkboxes and radio buttons produce field-change steps. Failed field-value getters remain unavailable rather than appearing as cleared text. Full manual inspection still includes decorative controls and column metadata.

Sampling allows 15 seconds initially and 25 seconds on a retry. Partial samples never replace the last complete baseline for screen/field-change comparison. Their successfully read controls can supply inferred input targets only after the session identity, transaction/title, window handles/processes and window geometry have been rechecked. Each control must still be no more than four seconds old. Missing controls are not interpreted as removals. A sampling note preserves the first verified partial screen of an interval; persistent gaps remain flagged. Other failed reads retain only recent window scope, without stale targets.

Short busy intervals are retried without an immediate error step; repeated failures are grouped until recovery. Stop removes input monitoring immediately, cancels the in-flight read, tries up to two final reads of 10 seconds each, and disposes the worker. Samples carry per-control timestamps and a completion time in JSON. Text exports include the exporter version and a sampling-note count; the window title also shows the application version.

Enable **Screen values** before Record if you need sampled field contents. Without values, a field's Modified flag can indicate an edit, but repeated edits may not be distinguishable.

**This is approximate documentation, not a complete native SAP recording.** Fast actions between samples, transient dialogs and complex grids may be missed. Target matching uses sampled native control handles when available, compensates for window movement, and rejects resized or older-than-four-second bounds. The whole window is never used as a click-target fallback; unresolved clicks retain their position and window context. Two nearby left clicks within Windows double-click limits mark the second as a likely double-click; the first remains in the timeline, and SAP acceptance is unconfirmed. SAP-generated field changes can resemble user edits. Newly opened windows are observed only after a successful sample discovers them. Unsupported OS dialogs are not monitored. Review the timeline; exported observations are comments and need manual completion before replay. Export includes an observed-input/field/screen/gap summary and per-observation timestamps.

### Grid and tree selection details

- GridView controls: current zero-based row (`-1` means the header), technical column ID, selected row indices/ranges and row count.
- Tree controls: selection mode and exact node keys, including leading spaces and zeros. Single-item mode includes the item node and column. Multiple-item mode is explicitly unsupported. Unsupported getters remain unavailable; they are not guessed.
- Selection changes get their own review steps. Input steps retain the last selection sampled **before** the input; it may be a different item from the one clicked. A later selection sample is separate evidence, not proof of the click target. Changing a step's target in the editor does not change the original selection control recorded with that evidence.
- The reader does not fetch grid cell contents or node captions. Selection strings are limited to 2,048 characters and node lists to 100 keys; truncation is flagged. Sorting/filtering can change row indices, so automation still needs a way to identify the intended business record.

Export **both JSON and script text** when using a recording to develop a Windows automation program. JSON preserves structured input and selection evidence with control IDs and timestamps; text makes the same evidence readable. The export does not supply complete replay logic, expected business results, or all exception paths. Observed entries remain non-executable comments and require validation against the intended workflow.

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
- SelectionDetails.cs: bounded grid/tree selection reads, structured evidence and double-click timing.
- StepCapture.cs: Record/Stop, timeline and export.
- Reader.cs and SapDispatch.cs: discovery and screen inspection.
- ReaderSession.cs: persistent observation worker, pipe transport and cancellation/timeout cleanup.
- ScriptExport.cs: recorded commands and legacy manual script generation.
- Main.xaml, Program.cs and WorkspaceDesign.cs: WPF workspace.
- RecorderTests.cs, ObservationTests.cs, SelectionTests.cs, SelfTests.cs and ScriptExportTests.cs: simulated checks, including caption-noise filtering, stale/resized targeting, partial-screen retries, slow-response recovery, selection APIs, evidence preservation and double-click timing.
- SamplingTests.cs: partial-screen target retention, individual control freshness, unavailable field values, collection-read counts, worker lifecycle and native window-scope checks using synthetic controls.

API reference: [SAP GUI Scripting API](https://help.sap.com/doc/9215986e54174174854b0af6bb14305a/800.04/en-US/sap_gui_scripting_api.pdf), GuiSession Record/Change events, GuiGridView selection properties and GuiTree selection methods.

Independent project; not affiliated with or endorsed by SAP.
