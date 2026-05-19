#!/usr/bin/env python3
"""Build release zips for GitHub Releases.

Produces two artifacts in dist/:

1. PS1Godot-plugin-<version>.zip
   Drop-in plugin for any Godot 4.x .NET project. Extract into the
   project's `addons/` folder, enable in Project → Plugins.
   Includes the prebuilt PS1Lua GDExtension DLL (Windows x86_64).

2. psxsplash-runtime-<version>.zip
   Prebuilt PS1 runtime binary. Users who don't want to install the
   MIPS toolchain can drop this into `godot-ps1/build/` (or wherever
   their launcher expects) and skip step 2 of QUICKSTART.md.

Run from repo root:
    python scripts/build-release.py [version]

If version is omitted, uses a date-stamp: YYYYMMDD.
"""
import os, shutil, sys, zipfile
from datetime import date

REPO_ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))
DIST_DIR = os.path.join(REPO_ROOT, 'dist')

# Files/paths to EXCLUDE when packaging the plugin. Patterns are
# substring match on the relative archive path.
PLUGIN_EXCLUDE_SUBSTRINGS = [
    # Huge third-party SDK — users don't need godot-cpp source, the
    # compiled DLL is what matters.
    'scripting/lib/',
    # Intermediate build artifacts other than the DLL we actually ship.
    'scripting/build/liblibps1lua',   # the static-lib alongside the DLL
    '.sconsign.dblite',
    '.sconf_temp',
    # SCons stays as a reference, but the object files are just noise.
    'scripting/build/.obj/',
]

def should_include(rel_path):
    for pat in PLUGIN_EXCLUDE_SUBSTRINGS:
        if pat in rel_path.replace('\\', '/'):
            return False
    return True

def write_plugin_zip(out_path):
    src_root = os.path.join(REPO_ROOT, 'godot-ps1', 'addons', 'ps1godot')
    if not os.path.isdir(src_root):
        raise SystemExit(f'Plugin source not found: {src_root}')

    count = 0
    with zipfile.ZipFile(out_path, 'w', zipfile.ZIP_DEFLATED) as zf:
        for dirpath, dirnames, filenames in os.walk(src_root):
            # prune godot-cpp early for perf
            dirnames[:] = [d for d in dirnames if 'godot-cpp' not in d]
            for fn in filenames:
                abs_path = os.path.join(dirpath, fn)
                # Archive path lives under addons/ps1godot/ so users can
                # unzip straight into their project's addons/ folder.
                rel_from_plugin = os.path.relpath(abs_path, src_root)
                rel_arch = os.path.join('addons', 'ps1godot', rel_from_plugin).replace('\\', '/')
                if not should_include(rel_arch):
                    continue
                zf.write(abs_path, rel_arch)
                count += 1
    print(f'  wrote {out_path} ({count} files, {os.path.getsize(out_path)//1024} KB)')

def write_runtime_zip(out_path, version):
    """Package whichever psxsplash variants exist. Two flavors live at
    well-known paths:

    - PCdrv build: `psxsplash-main/psxsplash.ps-exe`
                   (default `make` target, scripts/run.py build-psxsplash)
    - CDROM build: `godot-ps1/build/psxsplash-cdrom.ps-exe`
                   (LOADER=cdrom variant, run.py build-psxsplash --loader=cdrom)

    Include both if present so the release zip covers PCdrv iteration AND
    ISO/real-hardware flows. Builders who only built one variant get a
    one-variant zip, with a README note explaining the other.
    """
    psxsplash = os.path.join(REPO_ROOT, 'psxsplash-main')
    build_out = os.path.join(REPO_ROOT, 'godot-ps1', 'build')

    sources = [
        ('psxsplash.ps-exe',       os.path.join(psxsplash, 'psxsplash.ps-exe')),
        ('psxsplash.elf',          os.path.join(psxsplash, 'psxsplash.elf')),
        ('psxsplash-cdrom.ps-exe', os.path.join(build_out, 'psxsplash-cdrom.ps-exe')),
        ('psxsplash-cdrom.elf',    os.path.join(build_out, 'psxsplash-cdrom.elf')),
    ]

    present = [(arc, src) for arc, src in sources if os.path.exists(src)]
    if not present:
        raise SystemExit(
            'No runtime binaries found.\n'
            '  Run `python scripts/run.py build-psxsplash` for the PCdrv build,\n'
            '  and `python scripts/run.py build-psxsplash --loader=cdrom` for ISO.')

    has_pcdrv = any(arc == 'psxsplash.ps-exe' for arc, _ in present)
    has_cdrom = any(arc == 'psxsplash-cdrom.ps-exe' for arc, _ in present)

    with zipfile.ZipFile(out_path, 'w', zipfile.ZIP_DEFLATED) as zf:
        for arc, src in present:
            zf.write(src, arc)

        # README documents what's in the zip + how to swap variants.
        # Splashpack version is read from splashpack.hh so we don't drift.
        version_line = _read_splashpack_version() or '(unknown)'

        notes = []
        notes.append('psxsplash PS1 runtime -- prebuilt for the PS1Godot exporter.\n')
        notes.append(f'Release: {version}\n')
        notes.append(f'Splashpack format: v{version_line} (docs/splashpack-format.md).\n\n')

        if has_pcdrv:
            notes.append(
                '  psxsplash.ps-exe         -- PCdrv loader. Reads scene files\n'
                '                              from the host filesystem via\n'
                '                              PCSX-Redux\'s PCdrv backend.\n'
                '                              Default Run-on-PSX target when\n'
                '                              the exported scene has no\n'
                '                              XA-routed audio. Faster\n'
                '                              iteration -- no ISO rebuild.\n\n')
        if has_cdrom:
            notes.append(
                '  psxsplash-cdrom.ps-exe   -- CD-ROM loader. Reads scene files\n'
                '                              from a mkpsxiso-built ISO.\n'
                '                              Required for XA audio streaming\n'
                '                              and real-hardware boot. F5\n'
                '                              auto-switches when the scene\n'
                '                              has XA-routed clips.\n\n')
        if not has_cdrom:
            notes.append('  (CD-ROM variant not built -- run `run.py build-psxsplash --loader=cdrom` to add.)\n\n')
        if not has_pcdrv:
            notes.append('  (PCdrv variant not built -- run `run.py build-psxsplash` to add.)\n\n')

        notes.append(
            'To use: drop the .ps-exe (and matching .elf, if you want debug\n'
            'symbols) into godot-ps1/build/, then `python scripts/run.py\n'
            'launch-emulator` (PCdrv) or `... launch-emulator --iso` (ISO).\n'
            'Re-download this artifact when upgrading PS1Godot -- older\n'
            'runtimes fail to load or silently corrupt reads on newer\n'
            'splashpacks.\n\n'
            'Built with the MIPS toolchain (mipsel-none-elf-gcc OR\n'
            'mipsel-linux-gnu-gcc) + PSYQo + psxlua.\n'
            'Source: https://github.com/psxsplash/psxsplash (our patches\n'
            'live in psxsplash-main/ inside the PS1Godot repo).\n')

        zf.writestr('README.txt', ''.join(notes))

    size_kb = os.path.getsize(out_path) // 1024
    print(f'  wrote {out_path} ({len(present)} binaries, {size_kb} KB)')


def _read_splashpack_version():
    """Best-effort: scan splashpack.cpp for the runtime's minimum-version
    assertion so the README doesn't drift from the actual format.

    The canonical line looks like::

        psyqo::Kernel::assert(header->version >= 32, "Splashpack version too old ...");

    Returns the string ("32") or None if the source isn't shaped how we
    expect."""
    import re
    cpp = os.path.join(REPO_ROOT, 'psxsplash-main', 'src', 'splashpack.cpp')
    try:
        text = open(cpp, 'r', encoding='utf-8', errors='replace').read()
        m = re.search(r'header->version\s*>=\s*(\d+)\s*,\s*"Splashpack version too old', text)
        if m:
            return m.group(1)
    except OSError:
        pass
    return None

def main():
    version = sys.argv[1] if len(sys.argv) > 1 else date.today().strftime('%Y%m%d')
    os.makedirs(DIST_DIR, exist_ok=True)

    print(f'Building release artifacts for version: {version}')
    print()
    plugin_zip = os.path.join(DIST_DIR, f'PS1Godot-plugin-{version}.zip')
    runtime_zip = os.path.join(DIST_DIR, f'psxsplash-runtime-{version}.zip')

    print('Plugin:')
    write_plugin_zip(plugin_zip)
    print()
    print('Runtime:')
    write_runtime_zip(runtime_zip, version)
    print()
    print('Done. Attach both to the GitHub release.')

if __name__ == '__main__':
    main()
