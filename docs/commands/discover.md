# Discover

Read-only commands for exploring a model. All of them work against the
[active connection](../guides/connections.md) or an explicit model argument,
and all print JSON/CSV with `--output-format`.

## `ls` — list model objects

```
tx ls [path-filter] [model] [options]
```

| Option | Description |
|--------|-------------|
| `--type <type>` | Filter by type: `table`, `measure`, `column`, `calculatedcolumn`, `hierarchy`, `partition`, `relationship`, `role`, `perspective`, `culture`. |
| `--paths-only` | One object path per line, suitable for piping. |
| `--no-multiline` | Collapse multi-line cell content (e.g. measure expressions) to a single line. Text output only. |

```sh
tx ls                                # everything
tx ls --type table --paths-only
tx ls Sales/Measures                 # children of a container
tx ls "Sa*"                          # wildcard filter
```

## `get` — properties of one object

```
tx get <path> [model] [options]
```

| Option | Description |
|--------|-------------|
| `-q, --query <property>` | Query a specific property (e.g. `-q expression`, `-q formatString`). |
| `-t, --type <type>` | Disambiguate when the path matches multiple table-children. |

```sh
tx get "Sales/Total Sales"
tx get "Sales/Total Sales" -q expression
tx get Sales --output-format tmdl    # the object as TMDL
```

## `find` — search across the model

```
tx find <pattern> [model] [options]
```

| Option | Description |
|--------|-------------|
| `--in <scope>` | `names`, `expressions`, `descriptions`, `displayFolders`, `formatStrings`, `annotations`, `all` (default; annotations only when requested explicitly). |
| `--regex` | Treat the pattern as a regular expression. |
| `--case-sensitive` | Case-sensitive matching. |
| `--paths-only` | One matching object path per line, suitable for piping. |
| `--no-multiline` | Collapse multi-line match context to a single line. Text output only. |

```sh
tx find "SUM" --in expressions
tx find "TODO|FIXME" --regex --in descriptions
```

## `deps` — dependency analysis

```
tx deps [path] [model] [options]
```

| Option | Description |
|--------|-------------|
| `--upstream` | Only upstream dependencies (what this object uses). |
| `--downstream` | Only downstream dependents (what uses this object). |
| `--deep` | Recursive dependency tree. |
| `--max-depth <n>` | Maximum depth for `--deep` traversal (default: 10). |
| `--unused` | Find unreferenced measures and columns. |
| `--hidden` | With `--unused`: only list unused objects that are hidden. |
| `-t, --type <type>` | Disambiguate when the path matches multiple table-children. |

```sh
tx deps "Sales/Total Sales" --upstream
tx deps "Sales/Amount" --downstream --deep
tx deps --unused --hidden
```
