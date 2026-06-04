# Spectre.Console Migration

Tracks progress migrating `Console.WriteLine` calls to Spectre.Console styled output.

Color palette and style rules live in [`/docs/cli-color-strategy.md`](cli-color-strategy.md).

## Phase 0 — Shared Helpers

| Task                                  | Status |
|---------------------------------------|--------|
| Create `Styling.cs` with palette + helpers | Done |
| Consolidate `MarkupEscape` from 3 copies | Done |
| Consolidate `SeverityMarkup` from 2 copies | Done |

## Phase 1 — Commands with Hand-Rolled Tables

Replace manual box-drawing (`│`, `┼`, `─`) with Spectre `Table`.

| Command    | File              | Status |
|------------|-------------------|--------|
| validate   | `ValidateCommand.cs` | Done |
| find       | `FindCommand.cs`     | Done |

## Phase 2 — Commands with Key-Value / Status Output

Migrate `Console.WriteLine("Label: value")` to `AnsiConsole.MarkupLine` with bold labels.

| Command        | File                | Status |
|----------------|---------------------|--------|
| doctor         | `DoctorCommand.cs`  | Done |
| info           | `InfoCommand.cs`    | Done |
| load           | `LoadCommand.cs`    | Done |
| init           | `InitCommand.cs`    | Done |
| config         | `ConfigCommand.cs`  | Done |
| session        | `SessionCommand.cs` | Done |
| profile        | `ProfileCommand.cs` | Done |
| connect        | `ConnectCommand.cs` | Done |
| stage          | `StageCommand.cs`   | Done |
| deps           | `DepsCommand.cs`    | Done |

## Phase 3 — Commands with Success / Warning Messages

Short-form output — swap `Console.WriteLine` for `AnsiConsole.MarkupLine` with color roles.

| Command  | File                | Status |
|----------|---------------------|--------|
| add      | `AddCommand.cs`     | Done |
| set      | `SetCommand.cs`     | Done |
| rm       | `RmCommand.cs`      | Done |
| mv       | `MvCommand.cs`      | Done |
| replace  | `ReplaceCommand.cs` | Done |
| save     | `SaveCommand.cs`    | Done |
| deploy   | `DeployCommand.cs`  | Done |
| format   | `FormatCommand.cs`  | Done |
| script   | `ScriptCommand.cs`  | Done |
| auth     | `AuthCommand.cs`    | Done |
| macro    | `MacroCommand.cs`   | Done |

## Phase 4 — Diff Command

Color diff prefixes and summary.

| Command | File             | Status |
|---------|------------------|--------|
| diff    | `DiffCommand.cs` | Done |

## Phase 5 — Edge Cases / Remaining

| Area                | File                        | Status  |
|---------------------|-----------------------------|---------|
| Root help           | `RootHelpRenderer.cs`       | Done    |
| Interactive prompt  | `InteractiveCommand.cs`     | Done    |
| Stub errors         | `CompatibilityStubCommands.cs` | Skipped |
| Program error       | `Program.cs`                | Skipped |
| CommandOutput error | `CommandOutput.cs`          | Skipped |

### Skipped items rationale

- **CompatibilityStubCommands** — error messages for unimplemented commands, plain stderr
- **Program.cs** — error before Spectre is fully initialized
- **CommandOutput.cs** — validation error in dispatch, plain stderr

### Root help styling details

- `AnsiConsole.Profile.Width` set to `int.MaxValue` during help rendering to prevent line wrapping (restored in `finally`)
- Title: Sage, subtitle: Slate (muted)
- Section headers (`Usage:`, `Global options:`, `Commands:`): Bold
- Option flags and command usage strings: Sand (Value color)
- Descriptions: plain text
- Footer: Slate (muted)

## Already Migrated

| Command | File              | Notes                                        |
|---------|-------------------|----------------------------------------------|
| ls      | `LsRenderer.cs`   | Spectre `Table`, `[bold]` headers, `[grey]` booleans |
| bpa     | `BpaCommand.cs`   | Spectre `Table`, colored severity, markup summary |
| errors  | `ErrorOutput.cs`  | Spectre stderr console, colored severity labels     |

## Not Migrated (by design)

These output paths must remain plain text and never receive Spectre markup:

- `JsonOutput.Write` — raw JSON
- `CsvOutput.Write` — raw CSV
- `GetCommand` TMDL/BIM output — raw syntax
- `ScriptCommand` child process stdout/stderr forwarding — raw passthrough
- `AuthCommand` "Opening browser..." — stderr during interactive auth
- CI annotation blocks (`::error::`, `##vso[task.logissue...]`) — plain-text CI protocols
- Completion script output — shell script content
