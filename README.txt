SAP SCREEN INSPECTOR
====================

1. Open SAP GUI for Windows and log in.
2. Run SAP-Screen-Inspector.exe in the same Windows desktop/session as SAP.
3. Select the correct SAP session.
4. Navigate manually in SAP to the screen you need.
5. Click Capture screen, or enable Auto-refresh.
6. Search for a field/button, select it and Copy control ID.
7. Export capture to share a JSON file for automation development.

The inspector never clicks SAP controls, edits fields, scrolls SAP grids,
starts transactions or changes scripting settings. It does not use recording
events, so it can work when recording is disabled but scripting access remains
available. It cannot operate when SAP scripting access itself is disabled.

Values are excluded by default. Password controls are never read.
Exports include SAP username, system/client, transaction, control IDs, names,
tooltips and grid column metadata. Include screen values only when needed.
Export capture saves the latest displayed snapshot. Export steps saves the full sequence.
Navigate each relevant tab/dialog manually and capture each step with the hotkey.
Only currently instantiated SAP controls can be discovered. Grid row contents
and off-screen/virtualized table cells are not extracted.

Copy read-only code produces a VBScript snippet for an already-connected
variable named session. It does not create a complete runnable script.

x64: use first on 64-bit Windows.
x86: use if matching 32-bit SAP scripting components is necessary.
Both builds are self-contained; no separate .NET installation required.
No database connection or other application installation required.

Troubleshooting:
- SAP and the inspector must run in the same Windows/RDP/Citrix environment.
- If SAP is in Citrix, run the inspector inside that same environment.
- If access is denied, ask SAP support to verify scripting permissions.
- SAP attachment prompts may need acknowledgement in SAP.
- A busy SAP screen is skipped; auto-refresh pauses after errors.
- Capture worker stops after 25 seconds without freezing the interface.

Validation: simulated SAP tests and rendered UI checks passed.
Live SAP compatibility must be confirmed in your environment.

Developer:
dotnet build -c Release
SAP-Screen-Inspector.exe --demo       (sample screen, no SAP)
SAP-Screen-Inspector.exe --self-test  (writes self-test.txt)
SAP-Screen-Inspector.exe --smoke      (writes preview.png)

Standalone Windows utility; no database backend is needed.


STEP CAPTURE (v1.1)
==================
1. Select your SAP session and choose Start. This saves the starting screen.
2. Perform ONE action in SAP and wait for SAP to finish loading.
3. Press Ctrl+Alt+F8 while SAP is active. This saves the next screen without
   switching to the inspector. Alternatively click Capture step.
4. Repeat for each action. Check the step count if a capture appears to fail.
5. Choose Stop. Select steps in the timeline to review their captured controls.
6. Label each step with the action leading TO that screen, and choose the
   control used from the previous/current screen. Add notes or the key pressed.
7. Export steps and send the JSON file.

The hotkey is registered while the inspector is open. If another application
already uses it, the inspector displays a warning and Capture step still works.
Wait until capture completes before moving to another screen.
Collapse "Timeline and action details" for more room to inspect controls.

This is manual step capture, not automatic click detection or a replayable
script. Labels are supplied by you; unspecified actions stay unspecified.
The export includes each step's time, session, control IDs, metadata, included
values, warnings, action, chosen control ID and notes.

Each snapshot preserves its own Include screen values setting. Turning it off
later does not remove values from earlier steps. Password fields stay excluded.
Sequences are held in memory until exported. Closing with unexported changes
prompts first. Start becomes Resume after stopping; New starts a fresh sequence.
