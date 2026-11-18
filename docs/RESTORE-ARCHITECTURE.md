# Synchronous combat restoration

The model checkpoint engine remains independent of Undo And Restart. The beta.15 presentation refactor replaces selective repair of retained creature nodes with a common lifecycle, without reconstructing the room or replaying an ordinary action.

## Restore boundary

1. Wait until the current action releases the model; do not interrupt an enemy action halfway through.
2. Capture a rollback checkpoint. Pause action execution and every player queue, and temporarily disable player actions.
3. Remove the old creature views from the scene before mutating their models. Native exit hooks unsubscribe from the original power objects, stop owned sound loops and cancel death work. Retire native transient handles held by models.
4. Restore managed fields and collections, preserving model identity. Reassert execution locks because the snapshot also contains queue data.
5. Use the game's creature constructor to recreate views, health displays, power subscriptions, hitboxes and animation controllers. Restore durable visual data and reconnect persistent components to their model owners. Update the hand, potion belt and other room HUD elements through a separate adapter.
6. Validate the gameplay fingerprint before releasing the locks. On failure, rebuild the pre-operation checkpoint and preserve the Undo cursor. If rollback also fails, disable further navigation rather than continuing on a mismatched state.

There is no frame yield within steps 2–6. Ordinary settled Undo remains synchronous. Card-choice checkpoints retain the existing path that recreates the pending choice from its nearest settled anchor; this path can be asynchronous. The rollback transaction covers restoration and validation of that anchor, not every possible later choice-replay failure.

## Durable presentation data

- Animation: retain stable looping states and durable overlay tracks, using the game's own transition graph. Discard transient hit/death queues instead of freezing them halfway through. Rebind those states to a new native animation controller.
- Appearance: retain position, hue, size, facing and authored descendant scales. A hit-shake offset is transient and is not retained.
- Persistent components: inspect creature/power node references, recreate components owned by the replaced view through native factories, and reconnect the fields. Room-owned references keep their existing lifetime. Common form activation and fade-ramp values are data; old native nodes, tweens and asynchronous tasks are not checkpoints.
- Health: derive visibility from the model's visibility contract, remote-player/pet state and debug setting. No creature-name list decides which health bars to hide.

The implementation contains no monster-name or form-name restoration switch. Character matrices and named encounters in verification are test fixtures, not product exceptions.

## Boundaries

The adapter uses the installed game's private APIs. It originated on v0.111.0 and beta.19 was checked on v0.113.1. A game update can change those contracts. Native model-to-view fields were inventoried during development; third-party models and custom factories still need their own compatibility evidence. This is not a promise that every mod combination works.

Creature-owned presentation is covered by the new lifecycle. Global card VFX, the room HUD, background music and the pending-choice replay mechanism retain their existing ownership paths. The project does not serialize native engine objects or rerun gameplay `AfterApplied` hooks to recreate presentation.

Public verification scope is summarized in `VERIFICATION.md`. Combat restart boundaries are described in `RESTART-HISTORY.md`.
