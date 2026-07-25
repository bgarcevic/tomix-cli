#!/usr/bin/env bash
# Prove check-deploy-script.sh actually catches what it claims to.
#
# Generates a clean script from a sample model (offline — --deploy-full needs no server),
# asserts it passes, then mutates it once per invariant and asserts the matching check
# fires. A harness nobody has watched fail is not evidence.
#
# Usage: selftest-checker.sh [--tx <cmd>] [--fixture <script.xmla>] [--out <dir>]

set -euo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "$HERE/../.." && pwd)"
CHECK="$HERE/check-deploy-script.sh"
TX="$ROOT/tx"
FIXTURE=""
OUT="${TMPDIR:-/tmp}/tx-deploy-qa-selftest"
DB="AI Sample"

while [[ $# -gt 0 ]]; do
  case "$1" in
    --tx) TX="${2:-}"; shift 2 ;;
    --fixture) FIXTURE="${2:-}"; shift 2 ;;
    --out) OUT="${2:-}"; shift 2 ;;
    -h|--help) sed -n '2,9p' "$0" | sed 's/^# \{0,1\}//'; exit 0 ;;
    *) echo "unknown option: $1" >&2; exit 2 ;;
  esac
done

command -v jq >/dev/null || { echo "jq is required (brew install jq)" >&2; exit 2; }
mkdir -p "$OUT"

if [[ -z $FIXTURE ]]; then
  FIXTURE="$OUT/fixture.xmla"
  if [[ ! -s $FIXTURE ]]; then
    echo "generating fixture from samples (no server contacted)..."
    "$TX" deploy "$ROOT/samples/Artificial Intelligence Sample.SemanticModel" \
      --xmla "$FIXTURE" --deploy-full --skip-bpa --yes --quiet \
      --server "powerbi://api.powerbi.com/v1.0/myorg/selftest" --database "$DB" >/dev/null
  fi
fi
[[ -s $FIXTURE ]] || { echo "no fixture at $FIXTURE" >&2; exit 1; }

pass=0
fail=0

# $1 = case name, $2 = jq mutation ("" = none), $3 = expected substring, $4.. = extra check args
expect_caught() {
  local name="$1" mutation="$2" expect="$3"; shift 3
  local file="$OUT/$name.xmla" out
  if [[ -z $mutation ]]; then cp "$FIXTURE" "$file"; else jq "$mutation" "$FIXTURE" >"$file"; fi

  out=$("$CHECK" "$file" --database "$DB" --quiet "$@" 2>&1) || true
  if grep -qF -- "$expect" <<<"$out"; then
    printf '  ok      %-22s caught: %s\n' "$name" "$expect"
    pass=$((pass + 1))
  else
    printf '  MISSED  %-22s expected to report: %s\n' "$name" "$expect"
    printf '%s\n' "$out" | sed 's/^/            /'
    fail=$((fail + 1))
  fi
}

expect_clean() {
  local name="$1"; shift
  local out
  if out=$("$CHECK" "$FIXTURE" --database "$DB" --quiet "$@" 2>&1); then
    printf '  ok      %-22s no failures on a clean script\n' "$name"
    pass=$((pass + 1))
  else
    printf '  MISSED  %-22s clean script reported failures:\n' "$name"
    printf '%s\n' "$out" | sed 's/^/            /'
    fail=$((fail + 1))
  fi
}

echo "== baseline =="
expect_clean "clean-full" --deployed full --cloud
expect_clean "clean-vs-reference" --reference "$FIXTURE"

echo "== structural invariants =="
expect_caught wrong-database \
  '.createOrReplace.object.database = "Some Other Model"' \
  "deploy would hit the wrong database" --deployed full
expect_caught renamed-payload \
  '.createOrReplace.database.name = "Something Else"' \
  "Power BI rejects dataset renames" --deployed full
expect_caught duplicate-name \
  '.createOrReplace.database.model.tables += [{"name":"accounts","partitions":[{"name":"p","source":{"type":"m","expression":"1"}}]}]' \
  'duplicate name "accounts"' --deployed full
expect_caught table-without-partitions \
  '.createOrReplace.database.model.tables[0] |= del(.partitions)' \
  "has no partitions" --deployed full
expect_caught dangling-datasource \
  '.createOrReplace.database.model.tables[0].partitions[0].source.dataSource = "GoneAway"' \
  'binds to missing dataSource "GoneAway"' --deployed full
expect_caught dangling-expression \
  '.createOrReplace.database.model.tables[0].partitions[0].source.expressionSource = "GoneAway"' \
  'binds to missing expression "GoneAway"' --deployed full
expect_caught orphaned-relationship \
  '.createOrReplace.database.model.tables[0].name = "Renamed"' \
  "references a missing table" --deployed full
expect_caught stale-member-id \
  '.createOrReplace.database.model.roles = [{"name":"R","members":[{"memberName":"a@b.c","memberId":"guid"}]}]' \
  "still carry memberId" --deployed full --cloud
expect_caught leaked-credential \
  '.createOrReplace.database.model.dataSources = [{"name":"DS","connectionString":"Server=x;Password=hunter2"}]' \
  "possible credential material" --deployed full

echo "== differential invariants (vs the pure-source reference) =="
expect_caught structure-clobbered \
  '(.createOrReplace.database.model.tables[] | select(.measures) | .measures[0].expression) = "999"' \
  "model structure differs from source outside the preserved aspects" --reference "$FIXTURE"
expect_caught aspect-not-deployed \
  '.createOrReplace.database.model.expressions[0].expression = "\"target-value\""' \
  "was asked to deploy but does not match the source" --reference "$FIXTURE" --deployed expressions
expect_caught source-entry-dropped \
  'del(.createOrReplace.database.model.expressions[0])' \
  "is missing from the script" --reference "$FIXTURE"
expect_caught role-set-not-from-source \
  '.createOrReplace.database.model.roles = [{"name":"FromTarget","modelPermission":"read"}]' \
  "the role set does not match the source" --reference "$FIXTURE" --deployed roles

printf '\n  %d passed, %d missed\n' "$pass" "$fail"
((fail == 0))
