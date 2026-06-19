# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

See [docs/cli-ux-guidelines.md](docs/cli-ux-guidelines.md) for the versioning policy
and the API surface that major versions protect.

## [Unreleased]

- Remote model support: `connect`, mutate (`add`/`set`/`rm`/`mv`/`replace`), `deploy` over XMLA.
- BPA engine rewritten with Dynamic-LINQ (70 bundled rules), structured diagnostics, ignore/disable, external rule collections.
- Project renamed `mdl-cli` → `tomix-cli` (`tx` command); MinVer-based versioning and CI automation added.

### Added

- `tx refresh` — triggers data refresh on deployed models via XMLA. Supports `--type`, `--table`, `--partition`, `--apply-refresh-policy`/`--skip-refresh-policy`, `--effective-date`, `--max-parallelism`, `--dry-run`, `--no-progress`, `--trace`. Live per-table progress from XMLA `SessionTrace` with summary table (`Rows`, `Query`, `Read`, `Total`, `Rows/s`). JSON/CSV output support.
- `TOMIX_REFRESH_*` diagnostic codes.
- `Styling.Number(long)` and `Styling.DurationSeconds(double)` helpers.

### Changed

- `refresh` promoted from compatibility stub to real command.
- `TomServerModelSession` implements `IModelRefreshSession`.

### Removed

- `tx info` command (use `tx load` or `tx connect` instead).

### Fixed

- `refresh` per-table `Query`/`Read`/`Total` accuracy — trace sink now maps `ExecuteSql` → Query, `ReadData` → Read + row count instead of wrong subclasses.
- `refresh` row counts captured in-flight via `ReadData.IntegerData`; removed broken post-refresh DMV query.
- `refresh --trace` bare flag now resolves to stderr as documented, instead of silently doing nothing.
- `connect --workspace` no longer drops primary connection when overwrite prompt is declined; exits 0 instead of 1.
- `connect --workspace` shows spinner during remote probe (no silent gap).
- `connect` rejects invalid targets (e.g. typos) with `TOMIX_CONNECT_INVALID_TARGET`.
- `--save-to` on mutation commands honors `--force` (no silent overwrite).
- Source resolution honors global `--server`/`--database` on `ls`, `get`, `find`, etc.
- `deploy --fix-bpa` blocks on remaining error-severity violations; unsupported sessions fail with `TOMIX_DEPLOY_FIX_UNSUPPORTED`.
- `refresh --trace` no longer disposes `Console.Error` — wrapped in `NonDisposingTextWriter`.
- `refresh` honors injected connection session in `ActiveModelResolver` (resolution + tests no longer require live remote session).
