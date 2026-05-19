#!/usr/bin/env python3
"""scripts/run.py - cross-platform PS1Godot launcher.

Dispatches every action the legacy ``scripts/*.cmd`` files cover, on
Windows, Linux, and macOS. Each .cmd / .sh shim alongside this file is
a one-line wrapper that delegates here so muscle memory keeps working.

Actions::

    python scripts/run.py bootstrap-env
    python scripts/run.py build-psxsplash [--loader=pcdrv|cdrom]
    python scripts/run.py launch-editor
    python scripts/run.py launch-emulator [--iso]
    python scripts/run.py launch-game

Path resolution prefers the environment variable; falls back to a
reasonable per-OS default; surfaces a clear error if neither exists.
"""
from __future__ import annotations

import argparse
import os
import platform
import shutil
import subprocess
import sys
from pathlib import Path
from typing import Optional

REPO_ROOT = Path(__file__).resolve().parent.parent
GODOT_PROJECT = REPO_ROOT / "godot-ps1"
BUILD_OUT = GODOT_PROJECT / "build"
PSXSPLASH = REPO_ROOT / "psxsplash-main"

_OS = platform.system()  # "Windows" / "Linux" / "Darwin"
IS_WINDOWS = _OS == "Windows"


# ---------------------------------------------------------------------------
# Tool discovery
# ---------------------------------------------------------------------------

def _first_existing(*candidates: Optional[Path]) -> Optional[Path]:
    for c in candidates:
        if c and Path(c).exists():
            return Path(c)
    return None


def godot_exe() -> Optional[Path]:
    if env := os.environ.get("GODOT_EXE"):
        return Path(env)
    defaults = {
        "Windows": Path(r"D:\Programs\Godot_v4.7-dev5_mono_win64\Godot_v4.7-dev5_mono_win64.exe"),
        "Linux":   Path.home() / ".local" / "bin" / "godot",
        "Darwin":  Path("/Applications/Godot.app/Contents/MacOS/Godot"),
    }
    on_path = shutil.which("godot") or shutil.which("godot4") or shutil.which("godot-mono")
    return _first_existing(defaults.get(_OS), Path(on_path) if on_path else None)


def pcsx_redux_exe() -> Optional[Path]:
    if env := os.environ.get("PCSX_REDUX_EXE"):
        return Path(env)
    defaults = {
        "Windows": Path(r"C:\tools\pcsx-redux\pcsx-redux.exe"),
        "Linux":   Path.home() / ".local" / "bin" / "pcsx-redux",
        "Darwin":  Path("/Applications/PCSX-Redux.app/Contents/MacOS/pcsx-redux"),
    }
    on_path = shutil.which("pcsx-redux")
    return _first_existing(defaults.get(_OS), Path(on_path) if on_path else None)


def mips_gcc() -> Optional[str]:
    """Return the MIPS toolchain prefix command, e.g. ``mipsel-none-elf-gcc``.

    Linux distros ship ``mipsel-linux-gnu-gcc``; Windows / macOS users
    install ``mipsel-none-elf-gcc`` via the PCSX-Redux mips.ps1 script
    or the Homebrew tap. Either prefix is acceptable; the matching
    ``PREFIX=...`` value gets passed to make below.
    """
    for candidate in ("mipsel-none-elf-gcc", "mipsel-linux-gnu-gcc"):
        if shutil.which(candidate):
            return candidate
    return None


def mips_prefix() -> Optional[str]:
    """Toolchain prefix WITHOUT trailing dash — matches nugget's PREFIX var
    (third_party/nugget/common.mk:13 already declares ``PREFIX ?= mipsel-none-elf``,
    so we just need to override on the command line for non-default prefixes)."""
    gcc = mips_gcc()
    return gcc[: -len("-gcc")] if gcc else None


def dotnet_exe() -> Optional[str]:
    return shutil.which("dotnet")


def python_exe() -> str:
    """Interpreter to invoke nested Python scripts (build_iso.py, …)."""
    return sys.executable or ("python" if IS_WINDOWS else "python3")


# ---------------------------------------------------------------------------
# Output helpers
# ---------------------------------------------------------------------------

def info(msg: str) -> None:
    print(f"[run.py] {msg}", flush=True)


def fail(msg: str) -> int:
    print(f"[run.py] ERROR: {msg}", file=sys.stderr, flush=True)
    return 1


def require(path: Optional[Path], description: str, env_hint: str) -> Path:
    if path is None or not path.exists():
        print(f"[run.py] ERROR: {description} not found.", file=sys.stderr)
        print(f"         Set the {env_hint} environment variable or install"
              f" the tool to its default location.", file=sys.stderr)
        if path is not None:
            print(f"         Tried: {path}", file=sys.stderr)
        sys.exit(1)
    return path


# ---------------------------------------------------------------------------
# Actions
# ---------------------------------------------------------------------------

def cmd_bootstrap_env(_args: argparse.Namespace) -> int:
    """Show the env vars run.py honours and where they currently resolve.

    The legacy ``bootstrap-env.cmd`` used ``setx`` to write user-scope
    env vars on Windows. That's not portable, so this command instead
    prints the values plus a per-platform 'how to set them' hint.
    """
    info(f"Platform: {_OS}")

    def line(name: str, val: Optional[Path]) -> None:
        env = os.environ.get(name, "")
        resolved = str(val) if val else "(not found)"
        marker = "set" if env else "default"
        print(f"  {name:18s} [{marker}]  {env or resolved}")

    line("GODOT_EXE",       godot_exe())
    line("PCSX_REDUX_EXE",  pcsx_redux_exe())

    mips = mips_gcc()
    print(f"  {'MIPS toolchain':18s} [{'auto' if mips else 'missing'}]  "
          f"{mips or '(install mipsel-none-elf-gcc or mipsel-linux-gnu-gcc)'}")

    dn = dotnet_exe()
    print(f"  {'dotnet':18s} [{'auto' if dn else 'missing'}]  "
          f"{dn or '(install the .NET SDK)'}")

    print()
    if IS_WINDOWS:
        info("To persist an override (Windows): setx GODOT_EXE \"path\\to\\Godot.exe\"")
    else:
        info("To persist an override: add `export GODOT_EXE=/path/to/godot`"
             " to ~/.bashrc / ~/.zshrc.")
    return 0


def cmd_build_psxsplash(args: argparse.Namespace) -> int:
    """Build the psxsplash MIPS runtime. ``--loader=cdrom`` switches to
    the CD-ROM build (lands in ``godot-ps1/build/psxsplash-cdrom.*``);
    default ``pcdrv`` produces ``godot-ps1/build/psxsplash.*``.
    """
    if not shutil.which("make"):
        return fail("`make` not on PATH. Install MSYS2/Git Bash on Windows, "
                    "or `apt install build-essential` on Debian/Ubuntu.")
    if mips_gcc() is None:
        return fail("MIPS toolchain not on PATH (mipsel-none-elf-gcc or "
                    "mipsel-linux-gnu-gcc). See SETUP.md for install steps.")

    BUILD_OUT.mkdir(parents=True, exist_ok=True)

    make_args = ["make", "all", "-j"]
    if args.loader == "cdrom":
        make_args += ["LOADER=cdrom", "PERFOVERLAY=1"]
    else:
        make_args += ["PCDRV_SUPPORT=1", "PERFOVERLAY=1"]

    prefix = mips_prefix()
    if prefix and prefix != "mipsel-none-elf":
        # Linux distros ship mipsel-linux-gnu-gcc; nugget's common.mk
        # defaults PREFIX to mipsel-none-elf, override here.
        make_args.append(f"PREFIX={prefix}")

    info(f"Building psxsplash (loader={args.loader}, prefix={prefix})...")
    if args.loader == "cdrom":
        # Makefile shares one obj cache; clean before switching loaders.
        subprocess.run(["make", "clean"], cwd=PSXSPLASH, check=False,
                       stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    rc = subprocess.run(make_args, cwd=PSXSPLASH).returncode
    if rc != 0:
        return fail(f"psxsplash build failed (exit {rc}).")

    suffix = "-cdrom" if args.loader == "cdrom" else ""
    for ext in ("elf", "ps-exe"):
        src = PSXSPLASH / f"psxsplash.{ext}"
        dst = BUILD_OUT / f"psxsplash{suffix}.{ext}"
        if src.exists():
            shutil.copy2(src, dst)
            info(f"  -> {dst.relative_to(REPO_ROOT)}")
    if args.loader == "cdrom":
        info("PCdrv build cache invalidated; re-run "
             "`run.py build-psxsplash` to switch back.")
    return 0


def cmd_launch_editor(args: argparse.Namespace) -> int:
    """Pre-build the C# plugin DLL (so a stale DLL doesn't silently run
    old code), then launch Godot editor and tee output to .editor.log.
    """
    godot = require(godot_exe(), "Godot editor", "GODOT_EXE")

    if not os.environ.get("GODOT_NO_BUILD"):
        if dotnet := dotnet_exe():
            info("Pre-build: dotnet build (set GODOT_NO_BUILD=1 to skip)")
            rc = subprocess.run([dotnet, "build", "--nologo", "-v", "q"],
                                cwd=GODOT_PROJECT).returncode
            if rc != 0:
                return fail(f"dotnet build failed (exit {rc}). Fix compile "
                            "errors or set GODOT_NO_BUILD=1.")
        else:
            info("WARN: dotnet not on PATH; skipping pre-build. Plugin may be stale.")

    log_path = GODOT_PROJECT / ".editor.log"
    info(f"Logging to {log_path}")
    info(f"Launching Godot...")
    cmd = [str(godot), "--editor", "--path", str(GODOT_PROJECT)]
    proc = subprocess.Popen(cmd, stdout=subprocess.PIPE,
                             stderr=subprocess.STDOUT,
                             text=True, bufsize=1, encoding="utf-8",
                             errors="replace")
    assert proc.stdout is not None
    with open(log_path, "w", encoding="utf-8") as log:
        for line in proc.stdout:
            sys.stdout.write(line)
            sys.stdout.flush()
            log.write(line)
    rc = proc.wait()
    info(f"Godot exited (code {rc}). Log saved to {log_path}.")
    return rc


def cmd_launch_emulator(args: argparse.Namespace) -> int:
    redux = require(pcsx_redux_exe(), "PCSX-Redux", "PCSX_REDUX_EXE")

    if args.iso:
        cue = BUILD_OUT / "game.cue"
        exe = BUILD_OUT / "psxsplash-cdrom.ps-exe"
        if not exe.exists():
            exe = BUILD_OUT / "psxsplash-cdrom.elf"
        if not cue.exists():
            return fail(f"{cue} does not exist. Build the ISO first: "
                        f"`python tools/build_iso/build_iso.py`.")
        if not exe.exists():
            return fail(f"{exe} does not exist. Build the CD-ROM runtime: "
                        f"`run.py build-psxsplash --loader=cdrom`.")
        redux_args = [str(redux), "-stdout", "-iso", str(cue),
                      "-loadexe", str(exe), "-fastboot", "-run"]
    else:
        # PCdrv mode. Prefer .ps-exe; ELF loading has been flaky on the
        # current PCSX-Redux build (PC sometimes set without the code
        # segment being copied — see launch-emulator.cmd notes).
        exe = BUILD_OUT / "psxsplash.ps-exe"
        if not exe.exists():
            exe = BUILD_OUT / "psxsplash.elf"
        if not exe.exists():
            return fail(f"{exe} does not exist. Build first: "
                        f"`run.py build-psxsplash`.")
        redux_args = [str(redux), "-stdout", "-pcdrv",
                      "-pcdrvbase", str(BUILD_OUT),
                      "-loadexe", str(exe), "-fastboot", "-run"]

    log = BUILD_OUT / "pcsx.log"
    log.unlink(missing_ok=True)
    info(f"Launching PCSX-Redux ({'ISO' if args.iso else 'PCdrv'} mode)...")
    with open(log, "w", encoding="utf-8") as logf:
        return subprocess.run(redux_args, stdout=logf,
                              stderr=subprocess.STDOUT).returncode


def cmd_launch_game(_args: argparse.Namespace) -> int:
    """Run the Godot project in standalone (non-editor) mode."""
    godot = require(godot_exe(), "Godot", "GODOT_EXE")
    return subprocess.run([str(godot), "--path", str(GODOT_PROJECT)]).returncode


# ---------------------------------------------------------------------------
# Dispatcher
# ---------------------------------------------------------------------------

DISPATCH = {
    "bootstrap-env":   cmd_bootstrap_env,
    "build-psxsplash": cmd_build_psxsplash,
    "launch-editor":   cmd_launch_editor,
    "launch-emulator": cmd_launch_emulator,
    "launch-game":     cmd_launch_game,
}


def main(argv: Optional[list[str]] = None) -> int:
    ap = argparse.ArgumentParser(prog="scripts/run.py",
                                  description=__doc__,
                                  formatter_class=argparse.RawDescriptionHelpFormatter)
    subs = ap.add_subparsers(dest="action", required=True)

    subs.add_parser("bootstrap-env",
                    help="show / hint at env-var overrides")

    p_build = subs.add_parser("build-psxsplash",
                               help="build the psxsplash MIPS runtime")
    p_build.add_argument("--loader", choices=["pcdrv", "cdrom"], default="pcdrv",
                         help="loader variant to build (default: pcdrv)")

    subs.add_parser("launch-editor",
                    help="rebuild plugin + launch Godot editor on godot-ps1")

    p_emu = subs.add_parser("launch-emulator",
                             help="launch PCSX-Redux with the built runtime")
    p_emu.add_argument("--iso", action="store_true",
                       help="mount the built ISO instead of PCdrv mode")

    subs.add_parser("launch-game",
                    help="run godot-ps1 standalone (no editor)")

    args = ap.parse_args(argv)
    return DISPATCH[args.action](args)


if __name__ == "__main__":
    sys.exit(main())
