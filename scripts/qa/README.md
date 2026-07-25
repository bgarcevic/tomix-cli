# Deploy QA harness

Offline gate for `tx deploy`'s granular deployment. `deploy --xmla` emits the exact TMSL a
real deploy would execute, so most edge cases can be hunted by inspecting payloads instead
of spending live deploys. Nothing here executes a deploy — script generation is read-only
against the target, so the whole harness is safe to run against a real workspace.

| Script | Does |
|--------|------|
| `deploy-script-matrix.sh` | Generates one script per `--deploy-*` combination, checks each, and asserts the invalid combinations are rejected. |
| `check-deploy-script.sh` | Asserts invariants on a single generated script. Usable standalone on any `--xmla` output. |
| `selftest-checker.sh` | Mutates a clean script once per invariant and proves the checker catches it. Fully offline. |

## Run it

```sh
scripts/qa/deploy-script-matrix.sh \
  --model ./MyModel.SemanticModel \
  --server "powerbi://api.powerbi.com/v1.0/myorg/sandbox" \
  --database "MyDataset" \
  --out ./deploy-qa
```

Cells generated: `full` (the pure-source reference), `default`, `connections`, `partitions`,
`partitions-policy`, `expressions`, `roles`, `roles-members`. Rejected combinations checked:
`--deploy-full` with a granular flag, `--deploy-policy-partitions` alone,
`--deploy-role-members` alone.

The 6 granular flags are 64 combinations; the matrix covers each flag alone, both dependent
pairs, plus default and full. The rest are independent — a cross-product buys nothing.

Use `--tx ./tx` to run from source instead of the installed tool, and `-- --profile prod` to
pass anything else through to `tx`.

Diffing two cells shows exactly what a flag changes:

```sh
diff <(jq -S . deploy-qa/full.xmla) <(jq -S . deploy-qa/default.xmla)
```

## What the checks catch

Structural — hold for every cell, no reference needed:

| Invariant | Bug class |
|---|---|
| Addresses the target by name; payload name pinned to it | Deploying to the wrong database; Power BI rejecting a rename |
| `id` is the target's, not a copy of the name | ID churn on redeploy |
| No duplicate names in any collection, case-insensitively | A case-sensitive merge emitting two entries AS treats as one — invalid TMSL |
| Every table has ≥1 partition | A name-keyed preserve dropping partitions for a table with no target counterpart |
| Every partition binds to a data source / expression that exists | A preserved partition outliving a renamed source |
| Every relationship endpoint resolves to a table | A merge dropping a table but keeping its relationships |
| No `memberId` (cloud targets) | Stale service-assigned ids failing redeploy |
| No credential-shaped strings | The untested redaction path in issue #123 — reported as WARN for eyeballing, since names can legitimately contain these words |

Differential — needs `--reference full.xmla`, which the matrix wires up automatically:

- **Structure outside the preserved aspects is byte-identical to the source.** The broadest
  guard here: any splice reaching past its own aspect fails this, for every flag combination,
  without needing to know the target's state.
- Each aspect: deployed ⇒ must equal the source; preserved ⇒ must differ from it.
- No source-side data source or expression went missing (preserving still deploys entries
  that are new in the source).
- `--deploy-roles` without `--deploy-role-members` ⇒ the role set came from the source.

## Making the differentials conclusive

A preserved aspect that happens to match the source reports `WARN … inconclusive` — the check
can't tell "preserved correctly" from "preservation did nothing." Before the run, make the
target deliberately differ: change a connection string, an M parameter value, and a role
member in the portal. Every `WARN … inconclusive` that becomes `ok … preserved from the
target` is one more aspect actually proven.

## What this cannot prove

The payload being well-formed and containing what you intended is not the engine accepting
it — Analysis Services validation lives in their code. Still needs a live target:

- Engine acceptance of a spliced payload
- Processed data surviving a preserving deploy (compare `COUNTROWS` before/after)
- Incremental-refresh partitions surviving `--deploy-partitions` and being discarded by
  `--deploy-policy-partitions`
- Bound credentials surviving (change a connection string in the portal, deploy, refresh)
- ID pinning actually preventing churn across two deploys
- Sync-back paths still deploying `Full` (a `tx set` edit must not be preserved away)

Run this harness first; spend live deploys on that list.
