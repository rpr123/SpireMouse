# Combat restart history — beta.19, 2026-11-18

Scope: singleplayer combat only. Restart remains a native room reload with the normal opening draw and configured speed. Its completed opening state is appended to the existing history as “전투 재시작”. Undo returns to the preceding combat state; Redo returns to the recorded restart state. New input or another restart after Undo replaces the future branch. History is memory-only and still ends at the combat-ending boundary.

## Restoration

Each restart starts a new input segment sharing the original serialized combat-entry save. Points retain their segment, event offset, model fingerprint and label. Restart discards object-backed snapshots and choice anchors before destroying the room; it retains serialized inputs and fingerprints. It does not reuse references to retired combat models.

Navigation within a live segment uses the existing synchronous snapshot transaction. A point whose segment or snapshot is no longer live is rebuilt by loading the original combat-entry save and submitting only its recorded input prefix. Choice results are supplied through the existing replay hooks; a pending choice is recreated without automatically selecting anything. Intermediate settled checkpoints are fingerprint-validated and recaptured, allowing later ordinary Undo to use live snapshots again.

The cursor moves only after the target fingerprint matches. A failed rebuild attempts to recreate the pre-operation checkpoint and keeps the original cursor. If recovery also fails, further history navigation and automatic run saving are disabled, retaining the on-disk save. A pending card choice can be the state before restart. An enemy action already in progress must reach a safe player boundary before restart.

SaveRun and progress-file writes remain blocked by existing navigation guards throughout restart and replay. Non-combat restart retains its previous save-loading behavior and clears combat history. Event selections do not gain an Undo history.

## Verification

The tests run in game v0.113.1 with the author's enabled mod set and a memory-only save store. Each verifier temporarily installs its test DLL and restores the previous DLL on exit. Results and scope are summarized in `VERIFICATION.md`.

`restart-history`: actual native encounter, exact opening state, restart Undo/Redo, earlier history traversal, multiple restarts, enemy-turn/RNG state, new-action branch truncation, pending selection restoration and resolution, injected rebuild failure and retry, history capacity and history-panel navigation.

`restart-fool`: native Fool encounter, shot arrow model and stuck-arrow visual across repeated restart Undo/Redo.

`fool-melody`: Wah/Cho/Ki effect tags, visible symbol types, uniqueness, opacity, scale and texture identity after normal Undo/Redo, restart Undo/Redo, turn reset/Undo and same-frame Undo followed by Redo. The first run reproduced missing symbols in the last case: native `HasMelody` still found hidden, queued-for-deletion icons. `CombatHud.Refresh` now detaches those retired icons before the native combat-state notification rebuilds symbols. The original failing assertion is retained. Only this focused suite is repeated for the icon fix.

`combat` and `native-restart`: existing snapshot, selection, potion, speed, keyboard/mouse, history-capacity, victory/reward, non-combat room restart and visible native opening-draw regression suites. Existing assertions that restart clears history are updated because the requested behavior now preserves the past and appends a restart checkpoint. Separate fixture resets remain explicit where subsequent binding tests require index zero.

## Limits

Crossing a restart can take seconds and grows with the input prefix being replayed. Arbitrary third-party state, nondeterministic hooks and unrecorded custom game actions cannot be promised to reproduce; fingerprint mismatch triggers recovery rather than accepting divergent combat state. The existing combat fingerprint does not cover every external side effect. All characters, encounters, events and mod combinations are not exhaustively tested. This change does not add multiplayer, event-choice Undo, cross-floor history or history persistence across app restarts.
