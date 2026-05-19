# Internal docs

These pages aren't on the published site at <https://buffjesus.github.io/PS1Godot/>.
They live in the repo for future Claude sessions, contributors, and the
project author — they're handoff notes, RFCs, planning docs, and
superseded design references that would clutter the user-facing nav.

Browse them on GitHub: <https://github.com/BuffJesus/PS1Godot/tree/main/docs/internal>.

## What's here

- **`handoff-*.md`** — End-of-session handoff notes. The newest one is the
  current state of the project (post-session).
- **`archive/`** — Older handoffs and one-off release notes, plus
  `superseded/` for plans that landed differently than originally drafted.
- **`projects/`** — Active side-project notes (jam games, experiments).
- **`rfc/`** — Design RFCs (proposals before they ship).
- **`*-plan.md`, `*-strategy.md`** — Planning docs for features that have
  shipped (kept for the *why*; the code is the *what*).

## How they get on the site

They don't, by design — the site nav excludes `internal/**` via
`not_in_nav` in `mkdocs.yml`. To promote a page, move it into one of
the user-facing buckets (`getting-started/`, `authoring/`, `docks/`,
`lua-api/`, `reference/`, `contributing/`) and add it to the nav.
