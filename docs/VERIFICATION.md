# Verification

The published beta.19 source was exercised on Windows with Slay the Spire 2 v0.113.1 public-beta and the author's enabled mod set, using the game's memory-only test save store.

| Suite | Passed checks | Scope |
|---|---:|---|
| restart-history | 96 | Native restart Undo/Redo, history traversal, choices, branches, injected failure recovery, capacity, history UI |
| restart-fool | 26 | Arrow model and stuck-arrow visuals across restart Undo/Redo |
| native-restart | 10 | Native opening draw, 1x timing, room reload, normal Undo afterward |
| combat | 183 | Existing card, potion, choice, speed, input, history, victory/reward and non-combat restart regression checks |
| fool-melody | 57 | Wah/Cho/Ki effect tags, visible symbols, opacity, scale, textures, ordinary/restart/turn Undo and same-frame Undo/Redo |

The first four suites passed before the small final melody UI fix. The focused melody suite then reproduced missing symbols after same-frame Undo/Redo and passed after that fix. A complete regression rerun after that final UI change is not claimed.

When preparing this public repository, production source and test files were copied without changing their contents. Only build configuration and scripts were made portable. A production compilation is checked separately; no additional actual-game suites are required just to publish the source.

## beta.21

The restart-history change in beta.21 (sound suppression, in-place rebuild, snapshot reuse across segments, faster covered replay) was run on 2026-11-23 against the same game version and mod set: `restart-quiet` 46, `restart-history` 98, `restart-fool` 26, `native-restart` 10, `fool-melody` 57 and `combat` 183 checks passed. `restart-quiet` measures what is perceptible while history crosses a restart: cover frames compared pixel-wise, one-shot sounds reaching the audio engine, replayed inputs outside the cover, and timing for a short and a five-turn combat.

## Run an opt-in suite

Install Spire Mouse first, start Steam, and close the game. Pass the exact DLL of the selected installed mod; the script replaces it temporarily and restores its backup in `finally` after the verification process exits.

```powershell
.\verify.ps1 -RunName melody-local -Suite fool-melody `
  -GameDirectory 'D:\SteamLibrary\steamapps\common\Slay the Spire 2' `
  -InstalledModDll 'D:\SteamLibrary\steamapps\workshop\content\2868840\3808397695\SpireMouse.dll'
```

The path may instead point to a manually installed `mods/SpireMouse/SpireMouse.dll`. Keep only the intended installation active. Results, the source fingerprint, logs and the backed-up DLL are written to the ignored `artifacts/<RunName>/` directory. Choose a new run name each time. An SDK outside PATH can be selected with `-DotnetPath`.

The game's save store is isolated in verification mode. The mod's test build is never intended for normal distribution. Abruptly terminating the shell or machine can prevent the script's `finally` block from running; the previous DLL is available as `artifacts/<RunName>/previous.dll` for manual restoration.

Other suites are selected through the dispatch in `tests/ReplayChecks.cs`, including `combat`, `restart-history`, `restart-fool`, `native-restart`, `fool`, `fool-*`, `matrix-*`, `animation`, `reattach`, `mode-restart-*`, `treasure`, and `menu`. Test-only diagnostic room hooks are excluded from production builds.

## Limits

The suites do not exhaust every card, encounter, event branch, character or third-party mod combination. Fingerprint validation does not cover all external side effects. Non-deterministic hooks and custom unrecorded actions can prevent replay across a restart. No multiplayer, event-choice Undo, cross-floor history or persistent history support is implied.
