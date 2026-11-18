# Source provenance

The public project is separate from the earlier private integration.

- `src/MouseInput.cs`, `SettingsStore.cs`, `SettingsPanel.cs`, `StandaloneSpeed.cs`: custom implementation from this project, retained from the private build.
- `src/Timeline/*`: independent managed-state checkpoint engine. It captures mutable model fields and collections while excluding engine objects, delegates, and asynchronous runtime state. Stable restoration preserves model identity and uses a synchronous transaction with rollback validation. Old creature views exit before model mutation; replacement views use native constructors, durable animation states and plain appearance values. Persistent presentation components are recreated through native factories and reconnected to their model owners; no native objects are serialized. Pending choice continuations are recreated from the immediately preceding checkpoint. It does not contain the earlier Undo And Restart snapshot implementation or its translations.
- `src/Speed/*`: adapted from OLC's MIT-licensed STS2-QuickAnimationMode, commit `432c2478ad6653678569171b885e86746e502014`. The complete license is included as `LICENSE-SpeedControl.txt`.
  Source: https://github.com/BAKAOLC/STS2-QuickAnimationMode
- Tests use custom verification helpers and the installed game's test save store.

The original Undo source was inspected while making the private build; this is not a claim of a formal clean-room development process. No permission to distribute that source has been obtained, and it is excluded from this public project and release content. Do not package the private project's `src/Undo`, `language`, `localization`, `reference`, or `inspection` directories.

Game DLLs, decompiled game source, SDKs, Steam files, user settings and save data are build/runtime inputs only; they must never be included in the upload.

