# Tomix.Provider.Tom

Adapter around Microsoft Tabular Object Model.

## Responsibilities

- Translate TOM objects into tomix core abstractions.
- Centralize TOM-specific logic.
- Hide TOM implementation details from the rest of the codebase.
- Resolve the model display name shown by every command (`ModelDisplayName`):
  TOM database name → sibling `.platform` displayName → file/folder name
  (a PBIP `definition` folder inherits its item root's name) → caller fallback
  → `(unnamed)`.
- Save TMDL without churn (`TomModelExporter` + `TmdlFolderSync`): serialize to a
  staging folder, then write only files whose content changed (ignoring EOLs and
  trailing newlines), keep each rewritten file's EOL/BOM, match each M partition's
  existing `source =` depth, and delete only stale `.tmdl` files.

## Mutation structure

`TomModelMutator` is the public facade all sessions construct (`new TomModelMutator(database)`); it delegates to internal collaborators:

- `TomObjectAdder` — the `AddObject` type dispatch and per-type builders, plus add-option validation.
- `TomMutationTargetResolver` — path → object resolution (DAX forms, slash paths, container keywords, relationship endpoints); defines `TomResolvedObject`.
- `TomPropertyApplier` — per-type property assignment, annotation handling, expression edits, and value parsers.
- `TomTextReplacer` — model-wide text find/replace with previews.
- `TomMutationPaths` — shared path/name/type normalization and the mutation-path regexes.
- `TomRemoveCascade` — cascade collection for removals (remove dispatch stays on the facade).

## Live session (planned)

[ADR 0001](../../docs/design/adr-0001-live-model-session.md),
[ADR 0002](../../docs/design/adr-0002-live-session-lease-gate-and-journal-first.md) and
[ADR 0003](../../docs/design/adr-0003-live-session-checkpoint-rollback.md) add
`TomLiveModelSession`, used for TMDL, `.bim` and XMLA sources (`Tomix.Provider.Tmdl` reuses it):

- A FIFO lease gate serializes all access to the TOM `Database`. TOM is reachable only through a
  lease's capability view, which dies with the lease; a lease is a transaction (commit publishes,
  dispose without commit rolls back). Snapshot reads are served from an immutable, versioned
  `LiveModelSnapshot`.
- `TomChangeJournal` records every TOM write as a primitive (`Set`, `Attach`, `Detach`,
  `Rebind`), made through `TomWriter`. Change events come only from it.
  **Every TOM write in the mutator collaborators must go through `TomWriter`**. A direct write
  leaves events silently incomplete; the event-vs-snapshot-diff oracle test is there to catch it.
  Entries also carry the object's ID, path and `LineageTag` plus a provider-neutral form of the
  operation, so unsaved transactions can be replayed onto a reloaded model (merge, #374).
- Rollback, savepoints and undo restore checkpoints, not inverse journal entries: a writing
  transaction takes `Database.Clone()` on its first write. File-backed sessions swap the clone
  in (byte-identical); XMLA sessions restore with `Model.CopyTo` (same content, sibling order may
  differ). Never cache the `Database` across leases; it can be replaced.
- `TomObjectIdMap` maps TOM instances to session `ObjectId`s. TOM cannot re-attach a removed
  object, so any operation that replaces an instance (move) must `Rebind` the new instance to
  the old ID; restoring a checkpoint re-maps every object to the ID the checkpoint recorded.

## Cross-folder dependencies

- Depends on `/src/Tomix.Core`.
- May be used by `/src/Tomix.Provider.Tmdl`.
- Must not depend on `/src/Tomix.Cli`.
- Must not leak TOM types into `/src/Tomix.Core` or `/src/Tomix.App`.

## Rules

- Return `Tomix.Core` types from public APIs.
- Keep mapping code explicit and tested.
- Treat provider behavior as infrastructure, not domain logic.

## Test

```bash
dotnet test tests/Tomix.Provider.Tom.Tests
```
