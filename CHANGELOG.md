# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

See [docs/cli-ux-guidelines.md](docs/cli-ux-guidelines.md) for the versioning policy
and the API surface that major versions protect.

## [Unreleased]

### Added

- MinVer-based versioning from git tags (replaces hardcoded `0.1.0-dev`).
- `CHANGELOG.md` with Keep a Changelog format.
- `Directory.Build.props` with MinVer configuration.
- `README.md` with install, commands, scripting, and contributing docs.
- Versioning policy in `docs/cli-ux-guidelines.md`.
- CLI commands: `ls`, `get`, `find`, `info`, `deps`, `add`, `set`, `mv`, `rm`,
  `replace`, `format`, `script`, `macro`, `connect`, `deploy`, `load`, `save`,
  `auth`, `session`, `bpa`, `validate`, `diff`, `doctor`, `config`, `profile`,
  `init`, `completion`, `stage`, `interactive`.
- TMDL and TOM model providers with Power BI / Fabric connectivity.
- DAX and Power Query formatting via external APIs.
- Spectre.Console output with role-based color palette.
- JSON and CSV output formats (`--format`).
- BPA validation with `--fix` auto-fix pipeline.
- Credential persistence via MSAL with device-code and SPN auth.
- Interactive REPL with stdin piping.
- `--version` and `doctor` for environment diagnostics.
- CI release workflow: native binaries for 6 RIDs + .NET tool package.
- Install scripts (`install/install.sh` for Linux/macOS, `install/install.ps1` for Windows) with checksum verification and no-admin installs.
- Dev run wrapper (`./mdl`, `.\mdl.ps1`) and `scripts/install-dev.sh` so the short-command and global-tool dev workflows both work on macOS/Linux.
- CI workflow for pull requests and pushes to main (ubuntu + windows).
- Release job that publishes GitHub Release with checksums.txt on `v*` tags.
- `hint` field on `MdlDiagnostic` and `MdlResult.Fail()` for actionable error guidance.
- `--dry-run` option on `deploy` to preview changes without deploying.
- `--yes` / `-y` global option to skip confirmation prompts.
- `ConfirmationHelper` for interactive confirmation on destructive operations (deploy, replace, rm, connect).
- `TypeValidation` helper for consistent `--type` error messages.
- `CONTRIBUTING.md`.
- `--quiet` / `-q` global flag to suppress spinners, progress, and hints.
- `Spinner` wrapper around Spectre.Console `Status` with auto-suppression for JSON/CSV/pipe/--quiet.
- `DidYouMean` suggestion helper for unknown subcommands using Levenshtein distance.
- BPA structured result kinds — violations plus `DisabledRule`, `InvalidCompatibilityLevel`,
  `CompilationError`, and `EvaluationError` sentinels — surfaced as a diagnostics stream. `bpa run`
  reports real `ruleErrors`/`disabledRules`/`invalidCompatibilityRules`/`ignoredRules` counts (text
  footer + JSON `diagnostics[]`), instead of silently swallowing rules that fail to compile or run.
- BPA ignore/disable: `bpa rules ignore` / `unignore` write the model-level
  `BestPracticeAnalyzer_IgnoreRules` annotation (migrating the historical misspelled key), and the
  engine honors global rule disables and per-object suppressions (no parent inheritance).
  `bpa rules list --disabled` and its summary counts reflect a model's ignore set.
- BPA rule sources & precedence: model-embedded rules (`BestPracticeAnalyzer` annotation) and
  model-referenced external collections (`BestPracticeAnalyzer_ExternalRuleFiles`), plus
  machine/user-level rules (`~/.mdl` / `$MDL_CONFIG_DIR`), merged by documented precedence
  (machine < user < external < model-embedded) with case-insensitive de-duplication and best-effort
  load diagnostics. New `--no-model-rules` and `--allow-external-rules` flags on `bpa run`.
- Annotation write/remove support in the TOM mutator (`Annotation:<name>` on the model and on
  tables/columns/measures/…), and model-level annotations are now read into the snapshot.
- `Mdl.Provider.Tom.Tests` project covering the annotation read/write round-trip.

### Changed

- Migrate remaining `Console.Error.WriteLine` calls to Spectre.Console styled output.
- Enhance JSON error output to include `code`, `severity`, and `hint` fields.
- Improve empty result messaging for `ls` and `find` with guidance hints.
- Refactor auth: `AuthSettingsFactory` moved from CLI to `Mdl.App.Auth`.
- Refactor `ModelObjectKindParser` moved from CLI to `Mdl.Core.Models`.
- Replace `ModelSourceResolver` static calls with `ActiveModelResolver` instance in all commands.
- `DoctorHandlerTests` parameterized for multiple version strings.
- CI release workflow: add `fetch-depth: 0` for MinVer tag resolution.
- `doctor` now reports terminal capabilities (interactive, ANSI, color system, output
  redirected) to aid diagnosing rendering issues.
- `ls` measure expressions now show the first few lines and a `... (+N lines)`
  indicator on its own line when truncated, instead of silently dropping the rest.
- `bpa run` text output redesigned for readability instead of one flat truncated table.
  The default is now a compact, aligned, scannable list — one line per rule
  (severity · category · rule · count), ordered errors first. Full guidance and the
  affected objects are shown on demand via `--details`, `--full` (lists every object),
  or `bpa run --rule <ID>` (auto-detail for one rule); the detail view dims the object
  list, wraps text to a readable width, and shows each rule's guidance once.
  Adds `--errors`/`--warnings`/`--info` to filter displayed severities and repurposes
  `--no-multiline` to collapse a rule's guidance to a single line. JSON output is unchanged.
- Rewrote the BPA engine to evaluate each rule's `Expression` (a Dynamic-LINQ predicate
  dialect) generically via `System.Linq.Dynamic.Core`, replacing ~40 hardcoded per-rule C#
  checks. All 70 bundled rules are now honored (27 previously had no implementation and silently
  never ran), and any rule expressed in the `bpa-rules.json` format works without code changes.
  The dependency graph (`DependsOn`/`FullyQualified`) is derived from DAX via the shared
  `DaxReferenceExtractor`. Rules needing data the static snapshot lacks (e.g. VertiPaq
  annotations) simply don't fire until that data exists. JSON output, exit codes, and `--fix`
  are unchanged; `rules evaluated` now reports the true count.
- `bpa run` text output now prints the findings summary at the **bottom** (below the results) and
  adds an auto-fixable line — e.g. `62 of 233 can be auto-fixed — run  bpa run --fix`.
- BPA `Table` scope now **excludes** calculated tables and calculation-group tables (which have their
  own `CalculatedTable` / `CalculationGroup` scopes), matching the standard rule semantics.
- BPA expression evaluator distinguishes compilation from evaluation errors and surfaces them as
  diagnostics rather than silently skipping, while still preserving clean matches (never a false
  positive); the engine now enforces each rule's minimum `CompatibilityLevel`.

### Fixed

- `bpa run` no longer false-flags nearly every measure under `DAX_MEASURES_UNQUALIFIED`. The
  engine now distinguishes a qualified *measure* reference from a qualified *column* reference
  via the model dependency graph, so only measures that actually reference another measure in
  fully-qualified form are reported. Also repairs `DAX_COLUMNS_FULLY_QUALIFIED` (never fired due
  to a malformed regex) and the visibility-dependent rules broken by an always-false
  table-hidden check.
- BPA rules now evaluate **only over the object types named in their `Scope`** (matched by each
  object's actual type), instead of coarse buckets. This eliminates false positives such as
  `FIRST_LETTER_OF_OBJECTS_MUST_BE_CAPITALIZED` (scoped to calculated columns, not data columns)
  flagging every lowercase data column, and `REDUCE_USAGE_OF_CALCULATED_TABLES` running over all
  tables instead of only calculated ones.

- `scripts/install-dev.ps1` now clears stale packed tool packages before packing, so it
  reliably installs the current build (NuGet otherwise resolved an older `0.1.0-dev`
  prerelease that outranks `0.1.0-alpha.N`).

### Removed

- Standalone `InfoCommand.cs` (consolidated into connect flow).
- Hardcoded `<Version>` and `<IncludeSourceRevisionInInformationalVersion>` from `Mdl.Cli.csproj`.
