# Manage

Housekeeping: configuration, profiles, model scaffolding, shell integration,
and interactive use.

## `config` — CLI configuration

```
tx config <show|set|init|paths>
```

| Subcommand | Description |
|------------|-------------|
| `show` | Show current CLI configuration. |
| `set <key> <value>` | Set a configuration value. |
| `init` | Create a default `config.json`. |
| `paths` | Show resolved paths for local CLI files. |

```sh
tx config show
tx config set noColor true
```

## `profile` — named connection profiles

```
tx profile <list|show|set|remove>
```

| Subcommand | Description |
|------------|-------------|
| `list` | List all saved connection profiles. |
| `show <name>` | Show details of a profile. |
| `set <name>` | Create or update a profile. |
| `remove <name>` | Delete a profile. |

```sh
tx profile set dev -s DevWorkspace -d Sales
tx connect --profile dev
```

## `init` — scaffold a new model

```
tx init [output-path] [options]
```

| Option | Description |
|--------|-------------|
| `--name <name>` | Model/database name (default: directory name). |
| `--serialization <tmdl\|bim\|pbip>` | On-disk format (default: `tmdl`). |
| `--compatibility-mode <mode>` | `PowerBI` (default) or `AnalysisServices`. |
| `--compat <level>` | Compatibility level (default: 1702 for PowerBI, 1500 otherwise). |
| `--force` | Replace anything already at the target path. |

```sh
tx init ./my-model
tx init ./my-model --serialization pbip
```

## `completion` — shell completion

```
tx completion [bash|zsh|fish|powershell]
```

```sh
tx completion bash >> ~/.bashrc
tx completion powershell | Invoke-Expression
```

## `stage` — staged mutations

```
tx stage [status|list|commit|discard]
```

Inspect and manage staged (uncommitted) model mutations — see
[Editing & staging](../guides/editing.md) for the workflow.

```sh
tx stage             # staged mutations for the active model
tx stage commit      # promote them onto the source (and workspace mirror)
tx stage discard
```

## `interactive` — REPL

```
tx interactive [model]
```

Starts an interactive session for running multiple commands against a model
without re-loading it each time.

```sh
tx interactive ./model.tmdl
```
