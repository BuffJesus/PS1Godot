#!/usr/bin/env python3
"""Build a PSX ISO from a PS1Godot export.

What this does:
  1. Detect mkpsxiso (env MKPSXISO, then PATH, then C:\\tools\\mkpsxiso\\
     well-known fallback).
  2. Walk godot-ps1/build/ for splashpack triplets:
       scene_<n>.splashpack + .vram + .spu  (required)
       scene_<n>.xa                          (optional; XA-routed clips)
  3. Generate an mkpsxiso XML config that lays the data track out as:
       SYSTEM.CNF
       PSXSPLASH.PS-EXE      (the runtime, built with LOADER=cdrom)
       SCENE_n.SPK / .VRM / .SPU / .XA  (per scene)
  4. Run mkpsxiso → game.bin + game.cue.

Why XA needs this: the PSX SPU decodes XA-ADPCM in hardware from the
data track's Form-2 sectors. mkpsxiso has to mark each .xa file with
`type="xa"` so it lands in Form-2 sectors with the XA flag. PCdrv
(host-filesystem dev workflow) does not support this — testing XA
playback requires a real ISO mounted in PCSX-Redux's CD emulator.

Usage:
    python tools/build_iso/build_iso.py [--build-dir godot-ps1/build]
                                        [--out game.bin]

The runtime PS-EXE has to be a CDROM build of psxsplash:
    cd psxsplash-main && make clean && make LOADER=cdrom
…and copied to godot-ps1/build/psxsplash.ps-exe before running this.
PCdrv builds will boot but won't read scene files off the disc.
"""

from __future__ import annotations

import argparse
import os
import shutil
import subprocess
import sys
from pathlib import Path
from xml.sax.saxutils import escape


def find_mkpsxiso() -> str | None:
    env = os.environ.get("MKPSXISO")
    if env and Path(env).is_file():
        return env
    on_path = shutil.which("mkpsxiso")
    if on_path:
        return on_path
    # Well-known Wonderful Toolchain release layout used by build_iso.py's
    # sibling install path. Hard-coded fallback only — set MKPSXISO to
    # override.
    fallback = Path(r"C:\tools\mkpsxiso\mkpsxiso-2.20-win64\mkpsxiso.exe")
    if fallback.is_file():
        return str(fallback)
    return None


def find_scenes(build_dir: Path) -> list[dict]:
    """Find scene_*.splashpack files and their sidecars. Returns list of
    {index, splashpack, vram, spu, xa?} dicts in numeric order."""
    scenes = []
    for splashpack in sorted(build_dir.glob("scene_*.splashpack")):
        # Parse "scene_<n>.splashpack"
        try:
            idx = int(splashpack.stem.split("_", 1)[1])
        except (IndexError, ValueError):
            print(f"  skipping: {splashpack.name} (bad scene index)")
            continue
        record = {
            "index":      idx,
            "splashpack": splashpack,
            "vram":       splashpack.with_suffix(".vram"),
            "spu":        splashpack.with_suffix(".spu"),
            "xa":         splashpack.with_suffix(".xa"),
        }
        # The .xa sidecar is optional — only present when at least one
        # clip in the scene is Route=XA AND psxavenc was available.
        if not record["xa"].is_file():
            record["xa"] = None
        if not record["vram"].is_file() or not record["spu"].is_file():
            print(f"  skipping: {splashpack.name} (missing .vram or .spu sidecar)")
            continue
        scenes.append(record)
    return scenes


def emit_xml(
    *,
    scenes: list[dict],
    psxexec_src: Path,
    system_cnf_src: Path | None,
    image_path: Path,
    cue_path: Path,
) -> str:
    """Produce an mkpsxiso XML configuration for one data track holding
    the runtime + all scene files. License file injection is left to the
    caller — most homebrew ISOs ship without a Sony license stub for
    hobby use. Add a `<license file="..."/>` line under <track> if you
    have one."""

    lines: list[str] = []
    add = lines.append

    add('<?xml version="1.0" encoding="UTF-8"?>')
    add(f'<iso_project image_name="{escape(str(image_path))}" cue_sheet="{escape(str(cue_path))}" no_xa="0">')
    add('  <track type="data" xa_edc="true" new_type="false">')
    add('    <identifiers')
    add('      system       ="PLAYSTATION"')
    add('      application  ="PLAYSTATION"')
    add('      volume       ="PS1GODOT"')
    add('      volume_set   ="PS1GODOT"')
    add('      publisher    ="PS1GODOT"')
    add('      data_preparer="MKPSXISO via build_iso.py"')
    add('    />')
    add('    <directory_tree>')

    if system_cnf_src is not None:
        add(f'      <file name="SYSTEM.CNF" type="data" source="{escape(str(system_cnf_src))}"/>')

    add(f'      <file name="PSXSPLASH.PS-EXE" type="data" source="{escape(str(psxexec_src))}"/>')

    # mkpsxiso uppercases names + uses 8.3 style on the disc. We pin the
    # in-image names to match what the runtime fileloader.cpp expects.
    for s in scenes:
        idx = s["index"]
        add(f'      <file name="SCENE_{idx}.SPK" type="data" source="{escape(str(s["splashpack"]))}"/>')
        add(f'      <file name="SCENE_{idx}.VRM" type="data" source="{escape(str(s["vram"]))}"/>')
        add(f'      <file name="SCENE_{idx}.SPU" type="data" source="{escape(str(s["spu"]))}"/>')
        if s["xa"] is not None:
            # type="xa" routes the file through Form-2 sectors with the
            # XA flag set; PSX SPU decodes them directly from disc reads.
            add(f'      <file name="SCENE_{idx}.XA"  type="xa"   source="{escape(str(s["xa"]))}"/>')

    add('    </directory_tree>')
    add('  </track>')
    add('</iso_project>')
    return "\n".join(lines) + "\n"


def main() -> int:
    repo_root = Path(__file__).resolve().parent.parent.parent
    parser = argparse.ArgumentParser(description="Build a PSX ISO from PS1Godot exports.")
    parser.add_argument("--build-dir", type=Path,
                        default=repo_root / "godot-ps1" / "build",
                        help="directory containing scene_*.splashpack triplets (default: godot-ps1/build)")
    parser.add_argument("--psxexec", type=Path,
                        default=None,
                        help="psxsplash.ps-exe (default: <build-dir>/psxsplash.ps-exe; must be a LOADER=cdrom build)")
    parser.add_argument("--system-cnf", type=Path,
                        default=None,
                        help="SYSTEM.CNF for the ISO (optional; mkpsxiso usage notes recommend it for real hardware)")
    parser.add_argument("--out", type=Path,
                        default=repo_root / "godot-ps1" / "build" / "game.bin",
                        help="output image path (.bin); .cue will sit alongside")
    parser.add_argument("--xml-only", action="store_true",
                        help="emit XML to <out>.xml and skip mkpsxiso invocation")
    args = parser.parse_args()

    build_dir = args.build_dir.resolve()
    if not build_dir.is_dir():
        print(f"ERROR: build dir not found: {build_dir}")
        return 1

    psxexec = (args.psxexec or (build_dir / "psxsplash.ps-exe")).resolve()
    if not psxexec.is_file():
        print(f"ERROR: runtime executable not found: {psxexec}")
        print("       Build psxsplash with LOADER=cdrom and copy the .ps-exe to the build dir.")
        return 1

    system_cnf = args.system_cnf.resolve() if args.system_cnf else None
    if system_cnf is not None and not system_cnf.is_file():
        print(f"ERROR: --system-cnf points at a missing file: {system_cnf}")
        return 1

    scenes = find_scenes(build_dir)
    if not scenes:
        print(f"ERROR: no scene_*.splashpack files in {build_dir}")
        return 1

    out_bin = args.out.resolve()
    out_cue = out_bin.with_suffix(".cue")
    out_xml = out_bin.with_suffix(".xml")

    print(f"runtime: {psxexec}")
    print(f"scenes : {len(scenes)} (XA payload on {sum(1 for s in scenes if s['xa'])})")
    for s in scenes:
        marker = " [+xa]" if s["xa"] else ""
        print(f"  - scene_{s['index']}{marker}")
    print(f"output : {out_bin}")

    xml = emit_xml(
        scenes=scenes,
        psxexec_src=psxexec,
        system_cnf_src=system_cnf,
        image_path=out_bin,
        cue_path=out_cue,
    )
    out_xml.write_text(xml, encoding="utf-8")
    print(f"wrote  : {out_xml}")

    if args.xml_only:
        print("(--xml-only: skipping mkpsxiso invocation)")
        return 0

    mkpsxiso = find_mkpsxiso()
    if mkpsxiso is None:
        print("ERROR: mkpsxiso not found. Install from https://github.com/Lameguy64/mkpsxiso/releases,")
        print("       set MKPSXISO env var to the binary path, or put it on PATH.")
        return 1

    print(f"running: {mkpsxiso} -y \"{out_xml}\"")
    proc = subprocess.run([mkpsxiso, "-y", str(out_xml)],
                          capture_output=True, text=True)
    if proc.returncode != 0:
        print("mkpsxiso FAILED:")
        print(proc.stdout)
        print(proc.stderr, file=sys.stderr)
        return proc.returncode
    print(proc.stdout)
    print(f"done. mount {out_cue} in PCSX-Redux (CD-ROM mode, not PCdrv) to test XA.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
