# Source provenance

- `src/MouseInput.cs`, `SettingsStore.cs`, `SettingsPanel.cs`, `StandaloneSpeed.cs`: original to Spire Mouse.
- `src/Timeline/*`: Spire Mouse's own managed-state checkpoint engine for Undo/Redo and room restart. It captures mutable model fields and collections while excluding engine objects, delegates, and asynchronous runtime state. Stable restoration preserves model identity and uses a synchronous transaction with rollback validation. Old creature views exit before model mutation; replacement views use native constructors, durable animation states and plain appearance values. Persistent presentation components are recreated through native factories and reconnected to their model owners; no native objects are serialized. Pending choice continuations are recreated from the immediately preceding checkpoint.
  Its restore pipeline was informed by the publicly documented design of yuudong123's [Sts2UndoAndRestart](https://github.com/yuudong123/Sts2UndoAndRestart); no code from that project is included.
- `src/Speed/*`: adapted from OLC's MIT-licensed STS2-QuickAnimationMode, commit `432c2478ad6653678569171b885e86746e502014`. The complete license is included as `LICENSE-SpeedControl.txt`.
  Source: https://github.com/BAKAOLC/STS2-QuickAnimationMode
- Tests use custom verification helpers and the installed game's test save store.

Game files, SDKs, Steam files, user settings and save data are not included in this repository or its releases.
