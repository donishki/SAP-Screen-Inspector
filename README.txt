SAP SCREEN INSPECTOR 1.5.0

1. Open SAP GUI for Windows and log in.
2. Select your SAP session and press Record.
3. Go to SAP and perform ALL your steps.
4. Return and press Stop.
5. Review the timeline, then Save script or export JSON.

No per-step hotkey is needed. SAP may deliver pending actions only at Stop.
Resume recording appends to the same session; New sequence starts fresh.
Recording requires SAP scripting AND recording permission. If disabled, the
app reports it. Screen inspection remains available while stopped.

Recording includes entered action values regardless of the Screen values
snapshot switch. Password control commands are omitted. Keep exports private.
Only SAP scripting-supported actions are recorded, not OS dialogs or backend
preset values. Review scripts before use. The inspector never runs them.

The initial screen is a snapshot; later events contain affected controls and
commands, not full historical screens. Export JSON to preserve the sequence.
Data remains in memory until exported. Closing warns about unsaved data.

Both x86 and x64 builds are self-contained. Use the one compatible with your
SAP installation. In Citrix/RDP, run inside the same environment as SAP.

--demo: sample workspace without SAP
--self-test: simulated recording, reader and export checks
--smoke: synthetic UI/timeline/export checks and preview

Live SAP compatibility must be checked in your environment.
See README.md for requirements, limitations and build instructions.
