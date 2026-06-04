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
- `hint` field on `MdlDiagnostic` and `MdlResult.Fail()` for actionable error guidance.
- `--dry-run` option on `deploy` to preview changes without deploying.
- `--yes` / `-y` global option to skip confirmation prompts.
- `ConfirmationHelper` for interactive confirmation on destructive operations (deploy, replace, rm, connect).
- `TypeValidation` helper for consistent `--type` error messages.
- `CONTRIBUTING.md`.

### Changed

- Migrate remaining `Console.Error.WriteLine` calls to Spectre.Console styled output.
- Enhance JSON error output to include `code`, `severity`, and `hint` fields.
- Improve empty result messaging for `ls` and `find` with guidance hints.
- Refactor auth: `AuthSettingsFactory` moved from CLI to `Mdl.App.Auth`.
- Refactor `ModelObjectKindParser` moved from CLI to `Mdl.Core.Models`.
- Replace `ModelSourceResolver` static calls with `ActiveModelResolver` instance in all commands.
- `DoctorHandlerTests` parameterized for multiple version strings.
- CI release workflow: add `fetch-depth: 0` for MinVer tag resolution.

### Removed

- Standalone `InfoCommand.cs` (consolidated into connect flow).
- Hardcoded `<Version>` and `<IncludeSourceRevisionInInformationalVersion>` from `Mdl.Cli.csproj`.
