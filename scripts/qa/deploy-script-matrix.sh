#!/usr/bin/env bash
# Generate a `tx deploy --xmla` script for every meaningful --deploy-* combination and
# check each one's invariants offline.
#
# Read-only against the target: scripts are generated, never executed. Safe to run
# against a real workspace before spending any live deploy. See scripts/qa/README.md.
#
# Usage:
#   deploy-script-matrix.sh --model <path> --server <endpoint> --database <name> [options]
#
#   --out <dir>     Where to write scripts and logs (default: ./deploy-qa).
#   --tx <cmd>      tx executable to use (default: tx). Use ./tx to run from source.
#   --cloud|--no-cloud
#                   Override cloud-endpoint detection (affects the memberId check).
#   --             Everything after -- is passed through to tx (e.g. --profile prod).
#
# Exit codes: 0 = all cells generated and clean, 1 = at least one failure.

set -euo pipefail

MODEL=""
SERVER=""
DATABASE=""
OUT="./deploy-qa"
TX="tx"
CLOUD=""
PASSTHRU=()

while [[ $# -gt 0 ]]; do
  case "$1" in
    --model|-m) MODEL="${2:-}"; shift 2 ;;
    --server|-s) SERVER="${2:-}"; shift 2 ;;
    --database|-d) DATABASE="${2:-}"; shift 2 ;;
    --out) OUT="${2:-}"; shift 2 ;;
    --tx) TX="${2:-}"; shift 2 ;;
    --cloud) CLOUD=1; shift ;;
    --no-cloud) CLOUD=0; shift ;;
    --) shift; PASSTHRU=("$@"); break ;;
    -h|--help) sed -n '2,19p' "$0" | sed 's/^# \{0,1\}//'; exit 0 ;;
    *) echo "unknown option: $1" >&2; exit 2 ;;
  esac
done

[[ -n $MODEL && -n $SERVER && -n $DATABASE ]] \
  || { echo "usage: $0 --model <path> --server <endpoint> --database <name> [--out <dir>]" >&2; exit 2; }
command -v jq >/dev/null || { echo "jq is required (brew install jq)" >&2; exit 2; }

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
CHECK="$HERE/check-deploy-script.sh"
[[ -x $CHECK ]] || chmod +x "$CHECK"

if [[ -z $CLOUD ]]; then
  case "$(printf '%s' "$SERVER" | tr '[:upper:]' '[:lower:]')" in
    localhost*|127.0.0.1*|*.local*) CLOUD=0 ;;
    *) CLOUD=1 ;;  # bare workspace names resolve to powerbi://
  esac
fi
((CLOUD)) && CLOUD_FLAG=(--cloud) || CLOUD_FLAG=()

mkdir -p "$OUT"

# cell-name | tx flags | aspects deployed
CELLS=(
  "full|--deploy-full|full"
  "default||"
  "connections|--deploy-connections|connections"
  "partitions|--deploy-partitions|partitions"
  "partitions-policy|--deploy-partitions --deploy-policy-partitions|partitions,policy-partitions"
  "expressions|--deploy-shared-expressions|expressions"
  "roles|--deploy-roles|roles"
  "roles-members|--deploy-roles --deploy-role-members|roles,role-members"
)

# Flag combinations the CLI must reject, with the code it must report.
REJECT=(
  "full-plus-connections|--deploy-full --deploy-connections"
  "policy-without-partitions|--deploy-policy-partitions"
  "members-without-roles|--deploy-role-members"
)

printf 'model    %s\nserver   %s\ndatabase %s\nout      %s\ncloud    %s\n\n' \
  "$MODEL" "$SERVER" "$DATABASE" "$OUT" "$((CLOUD))"

generate() { # $1 = cell name, $2 = flags
  local name="$1" flags="$2"
  # shellcheck disable=SC2086  # flags are intentionally word-split
  "$TX" deploy "$MODEL" \
    --server "$SERVER" --database "$DATABASE" \
    --xmla "$OUT/$name.xmla" \
    --skip-bpa --yes --quiet --non-interactive \
    ${flags} "${PASSTHRU[@]+"${PASSTHRU[@]}"}" >"$OUT/$name.log" 2>&1
}

results=()
failed=0

echo "== generating and checking =="
for cell in "${CELLS[@]}"; do
  IFS='|' read -r name flags deployed <<<"$cell"
  rm -f "$OUT/$name.xmla"

  if ! generate "$name" "$flags"; then
    echo "$name"
    echo "  FAIL  tx exited non-zero — see $OUT/$name.log"
    sed 's/^/          /' "$OUT/$name.log" | head -10
    echo
    results+=("$name|generate failed")
    failed=1
    continue
  fi

  if [[ ! -s $OUT/$name.xmla ]]; then
    echo "$name"
    echo "  FAIL  no script written"
    echo
    results+=("$name|no script")
    failed=1
    continue
  fi

  ref=()
  [[ $name != full && -s $OUT/full.xmla ]] && ref=(--reference "$OUT/full.xmla")

  if "$CHECK" "$OUT/$name.xmla" --database "$DATABASE" \
      ${deployed:+--deployed "$deployed"} "${ref[@]+"${ref[@]}"}" "${CLOUD_FLAG[@]+"${CLOUD_FLAG[@]}"}"; then
    results+=("$name|clean")
  else
    results+=("$name|CHECK FAILED")
    failed=1
  fi
done

echo "== rejected flag combinations =="
for cell in "${REJECT[@]}"; do
  IFS='|' read -r name flags <<<"$cell"
  set +e
  generate "$name" "$flags"
  code=$?
  set -e
  if [[ $code -eq 0 ]]; then
    echo "  FAIL  $name was accepted; it must be rejected"
    results+=("$name|wrongly accepted")
    failed=1
    continue
  fi
  if grep -q "TOMIX_DEPLOY_INVALID_FLAGS" "$OUT/$name.log" 2>/dev/null; then
    echo "  ok    $name rejected (exit $code, TOMIX_DEPLOY_INVALID_FLAGS)"
    results+=("$name|rejected")
  else
    echo "  WARN  $name rejected (exit $code) but without TOMIX_DEPLOY_INVALID_FLAGS:"
    sed 's/^/          /' "$OUT/$name.log" | head -5
    results+=("$name|rejected, wrong code")
  fi
done

echo
echo "== summary =="
for r in "${results[@]}"; do
  IFS='|' read -r name verdict <<<"$r"
  printf '  %-26s %s\n' "$name" "$verdict"
done
echo
echo "scripts in $OUT — diff any two cells to see exactly what a flag changes, e.g."
echo "  diff <(jq -S . $OUT/full.xmla) <(jq -S . $OUT/default.xmla)"

exit "$failed"
