# User-Submitted Test Data

This folder holds Azgaar map exports contributed by users (typically attached
to bug reports) that we want available locally for repro runs but don't want
to track in git — they're user content, often large, and not ours to redistribute.

Everything inside `_user-submitted/` is gitignored except this README, which
exists to document the convention itself so a fresh clone shows the layout.

## Convention

One subdirectory per submission, ideally named after the Azgaar map name
(e.g. `Trimoyers/`). Each submission directory contains:

- The Azgaar export files (Full `.json`, Cells `.geojson`, Rivers `.geojson`)
- A `CONTEXT.md` — one or two sentences linking the submission to its bug
  report or use case.

## Adding a new submission

1. Create `_user-submitted/<MapName>/`.
2. Drop the Azgaar export files into it.
3. Write a short `CONTEXT.md` (what map, when, which bug it reproduces).

That's it — none of those files will be staged.
