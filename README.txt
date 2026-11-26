SAP SCREEN INSPECTOR 1.7.0

1. Open SAP GUI for Windows and select your session in the inspector.
2. Enable Screen values first if you need field contents in observed mode.
3. Press Record, go to SAP, and perform all your steps.
4. Return and press Stop. Review the timeline and export script text and JSON.

When SAP disables native recording, observed mode starts automatically.
Settings > Use observed recording also lets you select this mode manually.
It observes clicks/navigation keys in the selected SAP session windows and
repeatedly reads screens. It does not enable SAP recording or alter permissions.
SAP scripting read access must still be available.

Observed mode is approximate. Fast actions, transient dialogs, grids and stale
control positions can cause missing or incorrectly inferred steps. Field changes
can also come from SAP itself. New windows are monitored only after discovery.
Likely double-clicks are labelled using Windows timing and position limits.
The first click remains in the timeline; SAP acceptance is unconfirmed.
Review every observed action.
Observed steps export as TODO comments, not automatic replay commands.
JSON preserves screen control metadata, structured input/selection evidence
and review markers;
no bitmap screenshots are captured.

Raw typed characters and clipboard contents are never recorded by the input
monitor. Screen values controls sampled field contents in observed mode.
Selection metadata (row indices, column IDs, node keys) is included independently.
Password controls are excluded. Native mode still includes action arguments.
Exports contain session identity and potentially business data; keep them private.

Stop removes input monitoring immediately and attempts a final screen sample.
Sampling retries slow or partial responses. Recent selected-window input remains
observable without inferred targets during transient waits, for up to 30 seconds.
Identity loss suspends input immediately. Persistent gaps are flagged; recovery
adds a note. Stop may take about 20 seconds to obtain a complete final screen.
Data stays in memory until exported. Closing warns about unsaved data.

Both Windows x86 and x64 builds are portable and self-contained. Use the one
compatible with your SAP scripting installation. Run inside the same Citrix/RDP
environment as SAP when applicable.

--demo: synthetic workspace without SAP
--self-test: simulated recording, observation, reader and export tests
--smoke: synthetic UI and Windows hook lifecycle checks

Live SAP compatibility still requires verification in your environment.
See README.md for details and build instructions.

IMPROVEMENTS IN 1.7.0
GridView samples include current row, technical column ID, selected row ranges
and row count. Tree samples include exact node keys or a single item's node and
column. Unsupported getters and multi-item tree selections are flagged.
The reader does not fetch grid cell contents or node captions. Node lists are
limited to 100 keys and selection strings to 2,048 characters, with warnings.

Selection changes appear in the timeline. Selection attached to an input was
sampled BEFORE that input and may differ from the row/node actually clicked.
Later samples are separate evidence, not confirmation of a click target.
Row indices can change after sorting/filtering. Automation still needs a way
to identify the intended business record and verify the outcome.

Inputs trigger earlier screen samples when possible. JSON keeps structured
evidence; the text export shows readable selection details and timestamps.
Export both formats when developing an automation program. Observations remain
non-executable comments requiring review, and are not a complete replay script.
