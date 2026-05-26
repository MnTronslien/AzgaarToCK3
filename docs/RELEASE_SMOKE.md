# Release Smoke

The automated QA step we run before tagging a release. Confirms the current `develop` builds cleanly and converts every canonical test dataset to a non-crashing CK3 mod with plausible output.

## What it covers

Four datasets, all on the current `develop`:

| Dataset | Why |
|---|---|
| **Showcase** | Canonical reference map. Output lands at the default mod folder so CK3 can boot straight on top of it. |
| **Cerbois** | Major-river bug-prone (referenced in the river cell-swallow TODO). Catches regressions in the river pipeline. |
| **Oncyia** | Default test map. Small enough to iterate on, broad enough to cover most code paths. |
| **Handcrafted Edge Cases** | Hand-built data with known awkward geometry / sparse provinces / etc. Catches edge-case regressions. |

`100k_StressTest` is out of scope here — special-purpose run, do it when scaling concerns are the focus.

## How to run

From `CK3-claude/`:

```bash
mkdir -p /tmp/smoke-logs
cd AzgaarToCK3
dotnet build ConsoleUI/ConsoleUI.csproj    # expect 0 errors

# Showcase first — lands at default mod folder; CK3 can boot this if everything passes.
bash ../scorched-earth.sh
cd ConsoleUI/bin/Debug/net8.0
START=$(date +%s)
./ConsoleUI.exe --log-level debug > /tmp/smoke-logs/Showcase.log 2>&1
echo "Showcase: exit=$? elapsed=$(($(date +%s) - START))s"

# Cerbois, Oncyia, Edge Cases redirect output via -o so they don't trample Showcase.
for dataset in "Cerbois" "Oncyia" "Handcrafted Edge Cases"; do
    label=$(echo "$dataset" | tr ' ' '-')
    START=$(date +%s)
    ./ConsoleUI.exe \
      -d "../../../../TestData/$dataset" \
      -o "$LOCALAPPDATA/Temp/AzgaarToCK3-smoke/$label" \
      --log-level debug \
      > "/tmp/smoke-logs/$label.log" 2>&1
    echo "$dataset: exit=$? elapsed=$(($(date +%s) - START))s"
done
```

The whole run is ~20–25 min sequential. Each conversion is CPU-heavy, so concurrent runs in the same `bin/` don't work (the second build can't overwrite a locked binary). Parallel via separate worktrees is possible but see "What doesn't work" below.

## What to verify per dataset

Parse each log for:

| Field | Source line | Pass criterion |
|---|---|---|
| Build | `dotnet build` output | `0 Error(s)` |
| Conversion exit | wrapper `exit=N` | `0` |
| Baronies | `BaronyTerrainAssigner - assigned terrain for N baronies` | matches expected for the dataset |
| province_terrain rows | `Wrote 00_province_terrain.txt (N baronies)` | equals barony count |
| Terrain histogram | the 5–15 lines after the assigner banner | plausible for the dataset's geography |
| Warnings | `grep -ic warning` | known pre-existing categories only (tributary misclassification, orphan kingdom, invalid rivers) |
| Errors | `grep -iE "error|exception|fatal|crash"` | 0, OR only known graceful-fallback paths (e.g. NTS topology on small awkward baronies — they fall back to cell-by-cell fill) |

## Hard rules

- **Use CLI flags for input switching** (`-d`, `--json`/`--geojson`/`--rivers-geojson`). Never edit `settings.json` `InputDirectory` mid-session. (See memory `feedback_use_cli_flags_for_input_switching`.)
- **`--log-level debug` is non-optional** for non-interactive runs. The converter prompts `Start conversion?` at `Program.cs:151`. `YesNo()` auto-confirms only when log level ≤ Debug. Without the flag, redirected stdin can hang the run.
- **`-o <dir>` redirects mod output** away from the default `<ModsDirectory>/<ModName>`. Use it for every dataset except Showcase; Showcase wants the default so CK3 can boot from it.
- **Showcase last (or first, but its output must survive)**. Other runs use `-o` and don't touch the default mod folder, so order doesn't matter for them — but Showcase's output is the CK3-bootable mod, so its `scorched-earth` run shouldn't be followed by anything that writes to the same folder.

## What doesn't work (and why)

- **Agent `isolation: "worktree"`**: produces shadow directories at `.claude/worktrees/agent-*/` that are NOT visible to `git worktree list` and don't reliably materialise the current working-tree state. Three agents in 2026-05-26 all reported `-o doesn't exist`, `BaronyTerrainAssigner doesn't exist`, etc. — symptoms of running against a snapshot that predated the merge. Don't use this isolation mode for release smoke until the materialisation issue is understood.
- **Parallel runs in the same `bin/`**: the second build hits a `Converter.dll is locked` MSBuild error because the first run's process still has the DLL open. Either run sequentially or use real `git worktree add` directories with their own `bin/`.

## Future improvements

- Wrap the loop above in a script (e.g. `CK3-claude/release-smoke.sh`) so a release is one command + a wait.
- Add a structured-output mode (`--smoke-summary <path>`) that emits a machine-parseable record per run instead of relying on grep.
- Use real `git worktree add` (via Bash, not the Agent isolation feature) to parallelise. Saves ~15 min wall-clock when this becomes routine.
- Catch the known pre-existing warnings in a known-bugs allowlist so the smoke output is a diff vs. expected rather than a raw count.
