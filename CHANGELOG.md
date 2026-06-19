# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

See [docs/cli-ux-guidelines.md](docs/cli-ux-guidelines.md) for the versioning policy
and the API surface that major versions protect.

## [Unreleased]

- Remote model support: `connect`, mutate (`add`/`set`/`rm`/`mv`/`replace`), and `deploy` over XMLA
  (Power BI / Fabric / Analysis Services), with local workspace mirroring, staging, and revert.
- BPA engine rewritten to evaluate all 70 bundled rules generically via Dynamic-LINQ, with structured
  diagnostics (errors/disabled/ignored), per-model and per-user ignore/disable, and external rule collections.
- Project renamed `mdl-cli` → `tomix-cli` and command `tomix` → `tx`; namespaces, env vars, config dir,
  and `TOMIX_*` diagnostic codes updated accordingly. MinVer-based versioning and CI/release automation added.

### Added

- `tx refresh` triggers a data refresh on a deployed model with Tabular Editor-compatible flags
  (`--type`, `--table`, `--partition`, `--apply-refresh-policy` / `--skip-refresh-policy`,
  `--effective-date`, `--max-parallelism`, `--dry-run`, `--no-progress`, `--trace`). Targets the
  active remote connection by default, or the remote workspace-mode secondary when the default is
  local. Live per-table row counts during refresh are streamed from the XMLA `SessionTrace` into a
  Spectre `Live` table, and a final summary table reports per-table `Rows`, `Query`, `Read`, `Total`,
  and `Rows/s` plus a roll-up. `--output-format json|csv` machine output is supported.
- New `TOMIX_REFRESH_*` diagnostic codes (`TOMIX_REFRESH_NO_REMOTE_TARGET`, `TOMIX_REFRESH_UNSUPPORTED`,
  `TOMIX_REFRESH_BAD_TYPE`, `TOMIX_REFRESH_TABLE_PARTITION_CONFLICT`, `TOMIX_REFRESH_BAD_PARTITION`,
  `TOMIX_REFRESH_FAILED`).
- `Styling.Number(long)` and `Styling.DurationSeconds(double)` helpers for human-only output.

### Changed

- `refresh` promoted from a compatibility stub to a real command.
- `TomServerModelSession` now implements `IModelRefreshSession`; refresh is only supported on
  sessions connected to a live XMLA endpoint.

### Removed

- `tx info` command: dropped the standalone exploration entry. Model summaries remain available via
  `tx load` and `tx connect`, which reuse the same summary handler internally.

### Fixed

- `refresh` summary now reports accurate per-table `Query`, `Read`, and `Total` columns matching
  the reference CLI's arithmetic. The trace sink previously mapped the source-query phase to the
  wrong `TraceEventSubclass` (`SqlQuery`, used for ad-hoc SQL discovery) instead of `ExecuteSql`
  (the refresh source-query phase), so `QueryMs` was never populated and a fallback set
  `Query = Total`. It also never captured `ReadData` events at all, leaving `Read` always 0. The
  sink now routes from the actual subclasses observed in real Power BI Service trace dumps
  (`ExecuteSql` → Query, `ReadData` → Read + row count), computes `Total = Query + Read`, and
  keeps the `TabularRefresh` partition-end duration as a defensive fallback when neither phase
  event fires. Subclass constants are now named `AsTraceEventSubclass.*` enum references instead
  of brittle int literals.
- `refresh` row counts now come from `ReadData.IntegerData` captured during the refresh itself.
  The previous post-refresh `$SYSTEM.MDSCHEMA_DIMENSIONS` DMV query (`ApplyRowCounts` /
  `ExtractCardinalities`) — which existed only because the broken subclass mapping lost the
  in-flight row counts — has been removed along with its raw XMLA rowset XML parser.
- `refresh --trace` no longer silently does nothing when the flag is passed bare (no value).
  The previous `ResolveTracePath` collapsed both "absent" and "bare `--trace`" to `null` because
  `ArgumentArity.ZeroOrOne` surfaces both states as `null` from `GetValue`; the call site now
  gates on `ParseResult.GetResult(option)` to distinguish them, and bare `--trace` resolves to
  stderr (`"-"`) as documented. Without this, the trace sink never received a writer and the
  raw XMLA event stream was lost even though the summary table rendered.
- `connect --workspace` no longer drops the primary connection when the workspace
  overwrite prompt is declined. The primary source (local path or remote) is now
  persisted as the active connection first, and only the mirror setup is cancelled.
  The command now exits `0` in that case (primary connected) instead of `1`.
- `connect --workspace` shows a spinner ("Checking workspace target...") while probing
  the remote workspace target, closing the silent gap between the primary connect and
  the overwrite prompt.
- `connect` now rejects a server argument that is neither a remote endpoint nor a local
  model path (e.g. a typo like `tx connect clear`) with `TOMIX_CONNECT_INVALID_TARGET`,
  instead of silently storing a connection that no command could open.
- `--save-to` on mutation commands now honors the absence of `--force`: the TOM and TMDL sessions
  previously forwarded `Force: true` to the exporter, bypassing `TomModelExporter`'s overwrite guard
  and silently overwriting existing BIM/TMDL targets. They now forward the user's `--force` choice.
- Source model resolution now honors the recursive global `--server` (with `--database`) option.
  Commands like `ls`, `get`, `find`, and other source-resolving commands previously ignored
  `--server`, so `tx ls --server powerbi://... --database Model` resolved to an empty model and
  failed with `TOMIX_NO_PROVIDER` instead of opening the requested remote model.
- `deploy --fix-bpa` no longer proceeds when error-severity BPA violations remain after auto-fix.
  The gate's fail condition was previously suppressed whenever `--fix-bpa` was set, so deploys
  went through with known violations. The engine is now re-evaluated after fixes are applied and
  the deploy is blocked if any error-severity violation is still present; use `--skip-bpa` to
  bypass. `deploy --fix-bpa` on a provider whose session cannot apply fixes now fails clearly with
  `TOMIX_DEPLOY_FIX_UNSUPPORTED` instead of silently skipping the fixes and proceeding.
- `refresh --trace` no longer disposes the process-shared `Console.Error`. The trace writer for
  the stderr path (`-` and the file-open fallback) was returned unwrapped, so the command's
  `using` scope disposed `Console.Error`; the cached writer then threw `ObjectDisposedException`
  on the next stderr write. stderr is now wrapped in a non-disposing `TextWriter` so only the
  file-based `StreamWriter` the CLI owns is disposed.
- `refresh` now honors the injected connection session when resolving its target.
  `RefreshModelHandler` stored a `resolveSession` delegate but never read it, always resolving
  against the real user state dir. `ActiveModelResolver` now accepts a session-source delegate
  and the handler wires it through, so resolution (and the `HandleAsync` tests) no longer depend
  on an active remote session existing on the machine.
