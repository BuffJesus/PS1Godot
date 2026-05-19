#!/usr/bin/env python
"""Generate per-namespace Markdown pages for the PS1Lua API.

Parses the structured `// Namespace.Method(args) -> retval` comment
blocks in psxsplash-main/src/luaapi.hh — same source of truth as the
in-editor autocomplete (gen_api_data.py) and the EmmyLua stubs
(tools/LuaApiStubGenerator.cs). Emits one Markdown file per namespace
under docs/lua-api/, plus an overview index page.

Re-runnable: deletes existing docs/lua-api/*.md before writing so
removed APIs don't linger.

Invocation (from repo root):
    python scripts/py/gen_lua_api_docs.py
or pass paths explicitly:
    python scripts/py/gen_lua_api_docs.py \\
        psxsplash-main/src/luaapi.hh \\
        docs/lua-api
"""
import re
import sys
from pathlib import Path
from collections import defaultdict


REPO_ROOT = Path(__file__).resolve().parents[2]
DEFAULT_SRC = REPO_ROOT / "psxsplash-main" / "src" / "luaapi.hh"
DEFAULT_OUT = REPO_ROOT / "docs" / "lua-api"

# Marker used to distinguish auto-generated pages from handwritten ones
# in the same dir. Old auto-generated files get rm'd before the new
# pass writes; files without this marker are preserved.
GENERATED_SENTINEL = "<!-- gen_lua_api_docs:generated -->"


SIG_LINE = re.compile(
    r"^\s*//\s*(?P<ns>[A-Z][A-Za-z0-9]+)\."
    r"(?P<name>[A-Za-z_][A-Za-z0-9_]*)"
    r"\s*\((?P<args>[^)]*)\)"
    r"(?:\s*->\s*(?P<ret>.+?))?\s*$"
)


def parse(lines):
    """Walk lines top-to-bottom; return list of {ns, name, args, ret, doc}.

    Convention in luaapi.hh: signature comment FIRST, then any number
    of `//` description lines, then the C++ static declaration. A
    blank line or non-`//` line finalizes the current entry.
    """
    entries = []
    cur = None
    doc_lines = []

    def finalize():
        nonlocal cur, doc_lines
        if cur is None:
            return
        cur["doc"] = "\n".join(doc_lines).strip()
        entries.append(cur)
        cur = None
        doc_lines = []

    for line in lines:
        m = SIG_LINE.match(line)
        if m:
            finalize()
            cur = {
                "ns":   m.group("ns"),
                "name": m.group("name"),
                "args": (m.group("args") or "").strip(),
                "ret":  (m.group("ret") or "").strip(),
            }
            continue
        stripped = line.lstrip()
        if stripped.startswith("//") and cur is not None:
            doc_lines.append(stripped[2:].lstrip().rstrip())
        else:
            finalize()

    finalize()
    return entries


def slugify(ns):
    """Map a namespace name to a stable file slug."""
    out = []
    for i, ch in enumerate(ns):
        if ch.isupper() and i > 0 and not ns[i - 1].isupper():
            out.append("-")
        out.append(ch.lower())
    return "".join(out)


def fmt_signature(e):
    """Render a one-line call signature."""
    sig = f"{e['ns']}.{e['name']}({e['args']})"
    if e["ret"]:
        sig += f" -> {e['ret']}"
    return sig


def write_namespace_page(ns, entries, out_dir):
    """Emit docs/lua-api/<ns_slug>.md for one namespace."""
    slug = slugify(ns)
    path = out_dir / f"{slug}.md"
    out = [
        GENERATED_SENTINEL,
        f"# `{ns}`",
        "",
        "!!! info \"Generated\"",
        "    This page is auto-generated from "
        "`psxsplash-main/src/luaapi.hh` by "
        "`scripts/py/gen_lua_api_docs.py`. Edits won't survive the next "
        "build — fix the source comments instead.",
        "",
        f"{len(entries)} entries.",
        "",
        "## Methods",
        "",
    ]

    for e in entries:
        anchor = f"{slug}-{e['name'].lower().replace('_', '-')}"
        out.append(f"### `{fmt_signature(e)}` {{ #{anchor} }}")
        out.append("")
        if e["doc"]:
            out.append(e["doc"])
            out.append("")
        else:
            out.append("*No description.*")
            out.append("")

    path.write_text("\n".join(out).rstrip() + "\n", encoding="utf-8")
    return path


def write_overview(grouped, out_dir):
    """Emit docs/lua-api/index.md listing every namespace + method count."""
    total = sum(len(v) for v in grouped.values())
    path = out_dir / "index.md"
    out = [
        GENERATED_SENTINEL,
        "# Lua API",
        "",
        "PS1Lua exposes a runtime-bound C++ API to game scripts. The",
        "binding surface lives in `psxsplash-main/src/luaapi.hh` and is",
        "consumed three ways:",
        "",
        "- **In the Godot editor** — the PS1Lua language extension reads",
        "  the same signatures for autocomplete and hover.",
        "- **In external editors** (Rider, VS Code) — "
        "`LuaApiStubGenerator` emits EmmyLua stubs from the same source.",
        "- **On this docs site** — `scripts/py/gen_lua_api_docs.py`",
        "  parses the structured `// Namespace.Method(...)` comments",
        "  and writes one page per namespace.",
        "",
        f"**{len(grouped)} namespaces, {total} entries** across the surface.",
        "",
        "## Namespaces",
        "",
        "| Namespace | Entries | Page |",
        "| --- | --- | --- |",
    ]
    for ns in sorted(grouped.keys()):
        slug = slugify(ns)
        out.append(f"| `{ns}` | {len(grouped[ns])} | [`{ns}`]({slug}.md) |")
    out.append("")
    out.append("## Calling convention")
    out.append("")
    out.append("All entries are static methods on global tables. From a")
    out.append("`PS1LuaScript`-bound script:")
    out.append("")
    out.append("```lua")
    out.append("-- Get the camera's current world position")
    out.append("local px, py, pz = Camera.GetPosition()")
    out.append("")
    out.append("-- Play a clip; returns the active voice id or nil")
    out.append('local v = Audio.PlaySfx("door_creak")')
    out.append("")
    out.append("-- Fixed-point math (PS1 GTE uses Q12.20)")
    out.append("local d = FixedPoint.Mul(velocity, dt)")
    out.append("```")
    out.append("")
    out.append("Coroutines, closures, and standard Lua tables work as")
    out.append("normal — the PS1Lua runtime is psyqo-lua atop the PS1's")
    out.append("MIPS CPU. No JIT; expect interpreted-Lua speed budgets.")
    out.append("")
    path.write_text("\n".join(out), encoding="utf-8")
    return path


def main(argv):
    src = Path(argv[1]) if len(argv) > 1 else DEFAULT_SRC
    out_dir = Path(argv[2]) if len(argv) > 2 else DEFAULT_OUT

    if not src.exists():
        print(f"[gen_lua_api_docs] source not found: {src}", file=sys.stderr)
        return 1

    out_dir.mkdir(parents=True, exist_ok=True)

    # Only delete previously-generated pages so handwritten siblings
    # (recipes, FAQs, etc.) survive.
    for p in out_dir.glob("*.md"):
        try:
            head = p.read_text(encoding="utf-8", errors="replace")[:512]
        except OSError:
            continue
        if GENERATED_SENTINEL in head:
            p.unlink()

    lines = src.read_text(encoding="utf-8", errors="replace").splitlines()
    entries = parse(lines)
    if not entries:
        print(
            "[gen_lua_api_docs] no signatures matched — luaapi.hh format "
            "change? Aborting so a 0-entry site doesn't ship.",
            file=sys.stderr,
        )
        return 1

    grouped = defaultdict(list)
    for e in entries:
        grouped[e["ns"]].append(e)

    written = [write_overview(grouped, out_dir)]
    for ns in sorted(grouped.keys()):
        written.append(write_namespace_page(ns, grouped[ns], out_dir))

    print(
        f"[gen_lua_api_docs] {len(grouped)} namespaces, "
        f"{len(entries)} entries -> {out_dir}"
    )
    for p in written:
        print(f"  wrote {p.relative_to(REPO_ROOT)}")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
