# Error Codes Reference

This document lists every diagnostic code emitted by `mdl`, grouped by prefix.
Codes are part of the public API surface — renaming or removing a code requires
a major version bump (see [cli-ux-guidelines.md](cli-ux-guidelines.md#versioning-policy)).

## JSON Error Envelope

Under `--output-format json` (or `--error-format json`), errors are emitted on **stderr**
as a single JSON object using the first Error/Fatal diagnostic:

```json
{
  "error": "Human-readable message describing what happened.",
  "code": "MDL_MUTATION_FAILED",
  "severity": "Error",
  "hint": "Optional guidance on how to fix the problem."
}
```

Fields:

| Field      | Type   | Description                                              |
|------------|--------|----------------------------------------------------------|
| `error`    | string | The diagnostic message. Always present.                  |
| `code`     | string | The diagnostic code (see tables below). Always present.  |
| `severity` | string | `Info`, `Warning`, `Error`, or `Fatal`.                  |
| `hint`     | string | Optional fix suggestion. May be `null`.                  |

## Exit Codes

| Code | Meaning |
|------|---------|
| 0    | Success. |
| 1    | General failure (most errors). |
| 2    | Usage/argument error, IO failure, or pre-condition violation. |

Handlers override the default via `MdlResult.Fail(..., exitCode: 2)`. If no exit code
is specified, the default is `1`.

## Naming Convention

All diagnostic codes use uppercase `SNAKE_CASE` prefixed with `MDL_`. For example:
`MDL_MUTATION_SAVE_FAILED`, `MDL_OBJECT_NOT_FOUND`.

## Mutation Codes (`MDL_MUTATION_*`)

Emitted by `MutationRunner` and handlers that participate in the mutation lifecycle
(`add`, `rm`, `set`, `mv`, `replace`, `format`, `script`, `bpa run --fix`,
`bpa rules ignore/unignore`).

| Code | Exit | Trigger |
|------|------|---------|
| `MDL_MUTATION_UNSUPPORTED_PROVIDER` | 1 | The model provider does not implement `IModelMutationSession`. |
| `MDL_MUTATION_UNSUPPORTED` | 1 | A `NotSupportedException` was thrown during mutation. |
| `MDL_MUTATION_INVALID_VALUE` | 1 | An `ArgumentException` was thrown — invalid argument value. |
| `MDL_MUTATION_FAILED` | 1 | An `InvalidOperationException` was thrown — generic mutation failure. |
| `MDL_MUTATION_SAVE_FAILED` | 2 | An `IOException` occurred while saving the model. |

## Object Lookup Codes (`MDL_OBJECT_*`)

Emitted by `get`, `deps`, and `format -p` when a model object path fails to resolve.

| Code | Exit | Trigger |
|------|------|---------|
| `MDL_OBJECT_NOT_FOUND` | 1 | The object path matched zero objects. Includes a hint. |
| `MDL_OBJECT_AMBIGUOUS` | 1 | The object path matched more than one object. |

## BPA Codes (`MDL_BPA_*`)

| Code | Exit | Trigger |
|------|------|---------|
| `MDL_BPA_INVALID_FAIL_ON` | 2 | Invalid `--fail-on` value (expected: error, warning). |
| `MDL_BPA_RULE_ID_REQUIRED` | 2 | `bpa rules ignore/unignore` called without a rule id. |
| `MDL_BPA_RULES_LOAD_FAILED` | 2 | Failed to load the BPA rules catalog. |
| `MDL_BPA_VIOLATIONS` | 1 | BPA run found violations (exit non-zero when `--fail-on` is triggered). |

## Staging Codes (`MDL_STAGE_*`)

| Code | Exit | Trigger |
|------|------|---------|
| `MDL_STAGE_NOTHING_TO_COMMIT` | 1 | `stage commit` called with no staged mutations. |
| `MDL_STAGE_SOURCE_DRIFT` | 1 | The staged model source has changed since staging. |
| `MDL_STAGE_COMMIT_REMOTE_FAILED` | 1 | Failed to deploy staged changes to the remote endpoint. |
| `MDL_STAGE_COMMIT_LOCAL_FAILED` | 1 | Failed to apply staged changes locally. |
| `MDL_STAGE_MATERIALIZE_FAILED` | 1 | Failed to materialize the working copy. |
| `MDL_STAGE_OPTIONS_CONFLICT` | 2 | Conflicting stage options (e.g. `--stage` and `--revert` together). |
| `MDL_STAGE_SAVE_CONFLICT` | 2 | Conflicting save options (e.g. `--save` and `--stage` together). |

## Save Codes (`MDL_SAVE_*`)

| Code | Exit | Trigger |
|------|------|---------|
| `MDL_SAVE_OUTPUT_REQUIRED` | 2 | `save` called without an output path and none could be inferred. |
| `MDL_SAVE_OUTPUT_EXISTS` | 2 | The output path already exists (use `--force` to overwrite). |
| `MDL_SAVE_FIX_UNSUPPORTED` | 2 | `save --fix` is not supported by the provider. |
| `MDL_SAVE_UNSUPPORTED_PROVIDER` | 1 | The provider does not support model export/saving. |
| `MDL_SAVE_UNSUPPORTED_SERIALIZATION` | 2 | The requested serialization format is not supported. |

## Deploy Codes (`MDL_DEPLOY_*`)

| Code | Exit | Trigger |
|------|------|---------|
| `MDL_DEPLOY_NO_TARGET` | 2 | `deploy` called without a target server/database. |
| `MDL_DEPLOY_UNSUPPORTED` | 2 | The source model cannot be deployed (wrong provider/type). |
| `MDL_DEPLOY_FAILED` | 1 | The deployment operation failed. |

## Init Codes (`MDL_INIT_*`)

| Code | Exit | Trigger |
|------|------|---------|
| `MDL_INIT_OUTPUT_REQUIRED` | 2 | `init` called without an output path. |
| `MDL_INIT_OUTPUT_EXISTS` | 2 | The output path already exists. |
| `MDL_INIT_UNSUPPORTED_SERIALIZATION` | 2 | The requested serialization format is not supported for `init`. |
| `MDL_INIT_UNSUPPORTED_COMPATIBILITY_MODE` | 2 | The requested compatibility level is not supported. |

## Auth Codes (`MDL_AUTH_*`)

> **Note:** Codes in this section are diagnostic codes, not environment variables.
> See [Environment Variables](#environment-variables) for auth-related env vars.

| Code | Exit | Trigger |
|------|------|---------|
| `MDL_AUTH_FAILED` | 1 | Authentication failed (invalid credentials, token expired, etc.). |
| `MDL_AUTH_REQUIRED` | 1 | Authentication is required but no credentials were provided. |

## Script Codes (`MDL_SCRIPT_*`)

| Code | Exit | Trigger |
|------|------|---------|
| `MDL_SCRIPT_FILE_NOT_FOUND` | 1 | The script file was not found. |
| `MDL_SCRIPT_REQUIRED` | 2 | `script` called without a script file or inline script. |

## Macro Codes (`MDL_MACRO_*`)

| Code | Exit | Trigger |
|------|------|---------|
| `MDL_MACRO_FILE_EXISTS` | 2 | A macro file already exists at the target path. |
| `MDL_MACRO_FILE_NOT_FOUND` | 1 | The macro file was not found. |
| `MDL_MACRO_NOT_FOUND` | 1 | The macro name was not found in the catalog. |
| `MDL_MACRO_SCRIPT_NOT_FOUND` | 1 | The macro's script template was not found. |
| `MDL_MACRO_UNKNOWN_PROPERTY` | 2 | The macro uses an unknown property. |

## Config Codes (`MDL_CONFIG_*`)

> **Note:** `MDL_CONFIG_DIR` is an environment variable, not a diagnostic code. See below.

| Code | Exit | Trigger |
|------|------|---------|
| `MDL_CONFIG_INVALID_VALUE` | 2 | `config set` called with an invalid value. |
| `MDL_CONFIG_UNKNOWN_KEY` | 2 | `config set` called with an unknown configuration key. |

## Profile Codes

| Code | Exit | Trigger |
|------|------|---------|
| `MDL_PROFILE_NOT_FOUND` | 1 | The named profile was not found. |
| `MDL_PROFILE_NAME_REQUIRED` | 2 | `profile set` called without a profile name. |

## General Codes

| Code | Exit | Trigger |
|------|------|---------|
| `MDL_NO_PROVIDER` | 2 | No registered provider can open the model. |
| `MDL_NO_MODEL` | 2 | No model reference was provided and none could be inferred. |
| `MDL_CONNECT_FAILED` | 1 | `connect` failed to establish a session. |
| `MDL_DATABASE_NOT_FOUND` | 1 | The database/model name was not found on the server. |
| `MDL_DEPS_PATH_REQUIRED` | 2 | `deps` called without an object path. |
| `MDL_MOVE_UNSUPPORTED` | 1 | `mv` called for an unsupported object type or operation. |
| `MDL_REPLACE_PATTERN_REQUIRED` | 2 | `replace` called without a search pattern. |
| `MDL_SET_PROPERTY_REQUIRED` | 2 | `set` called without a property to set. |
| `MDL_FORMAT_UNSUPPORTED_LANGUAGE` | 2 | `format` called with an unsupported expression language. |
| `MDL_COMPLETION_UNSUPPORTED_SHELL` | 2 | `completion` called with an unsupported shell name. |

## Environment Variables

The following `MDL_*` tokens are **environment variables**, not diagnostic codes:

| Variable | Description |
|----------|-------------|
| `MDL_AUTH_CLIENT_ID` | Azure AD client id for service principal auth. |
| `MDL_AUTH_TENANT` | Azure AD tenant id for service principal auth. |
| `MDL_AUTH_CLIENT_SECRET` | Azure AD client secret for service principal auth. |
| `MDL_AUTH_CERTIFICATE` | Path to a certificate for service principal auth. |
| `MDL_AUTH_CERTIFICATE_PASSWORD` | Password for the auth certificate. |
| `MDL_MACROS_PATH` | Custom path to the macro catalog directory. |
| `MDL_SESSION` | Session id for persisting the active model connection. |
| `MDL_CONFIG_DIR` | Custom path to the configuration directory. |
| `MDL_POWERQUERY_FORMATTER_API` | Custom Power Query Formatter API endpoint URL. |

## Migration: Unified Mutation Error Codes

The following error codes were replaced by unified `MDL_MUTATION_*` codes.
Scripts that pattern-match on the old codes should update to the new ones.

| Old code | New code |
|----------|----------|
| `MDL_REPLACE_INVALID_ARGUMENT` | `MDL_MUTATION_INVALID_VALUE` |
| `MDL_REPLACE_UNSUPPORTED` | `MDL_MUTATION_UNSUPPORTED` |
| `MDL_REPLACE_FAILED` | `MDL_MUTATION_FAILED` |
| `MDL_REPLACE_SAVE_FAILED` | `MDL_MUTATION_SAVE_FAILED` |
| `MDL_SCRIPT_SAVE_UNSUPPORTED` | `MDL_MUTATION_UNSUPPORTED` |
| `MDL_SCRIPT_SAVE_FAILED` | `MDL_MUTATION_SAVE_FAILED` |
| `MDL_BPA_FIX_UNSUPPORTED` | `MDL_MUTATION_UNSUPPORTED_PROVIDER` |
| `MDL_BPA_IGNORE_UNSUPPORTED` | `MDL_MUTATION_UNSUPPORTED_PROVIDER` |

### What did not change

Command-specific validation codes (checked before entering `MutationRunner`) are unchanged:

- `MDL_REPLACE_PATTERN_REQUIRED`
- `MDL_SET_PROPERTY_REQUIRED`
- `MDL_SCRIPT_FILE_NOT_FOUND`
- `MDL_SCRIPT_REQUIRED`
- `MDL_BPA_RULE_ID_REQUIRED`
- `MDL_FORMAT_UNSUPPORTED_LANGUAGE`
