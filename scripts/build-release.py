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

REPO_ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..'))
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

def write_runtime_zip(out_path):
    src_exe = os.path.join(REPO_ROOT, 'psxsplash-main', 'psxsplash.ps-exe')
    src_elf = os.path.join(REPO_ROOT, 'psxsplash-main', 'psxsplash.elf')
    if not os.path.exists(src_exe):
        raise SystemExit(
            f'Runtime binary missing: {src_exe}\n'
            f'Run scripts\\build-psxsplash.cmd first.')

    with zipfile.ZipFile(out_path, 'w', zipfile.ZIP_DEFLATED) as zf:
        zf.write(src_exe, 'psxsplash.ps-exe')
        if os.path.exists(src_elf):
            zf.write(src_elf, 'psxsplash.elf')
        # Stamp the splashpack format version so users know what to pair.
        zf.writestr('README.txt',
            'psxsplash PS1 runtime — prebuilt for the PS1Godot exporter.\n'
            '\n'
            'Splashpack format: v22 (check docs/splashpack-format.md in the main repo).\n'
            '\n'
            'To use: extract psxsplash.ps-exe into godot-ps1/build/ (create the\n'
            'folder if missing), then re-run scripts/launch-emulator.cmd. If you\n'
            'later upgrade PS1Godot to a newer splashpack version, re-download\n'
            'this runtime artifact to match — older runtimes will fail to load\n'
            'or silently corrupt reads on newer splashpacks.\n'
            '\n'
            'Built with MIPS toolchain (mipsel-none-elf-gcc) + PSYQo + psxlua.\n'
            'Source: https://github.com/psxsplash/psxsplash (our patches live in\n'
            'psxsplash-main/ inside the PS1Godot repo).\n'
        )
    size_kb = os.path.getsize(out_path) // 1024
    print(f'  wrote {out_path} ({size_kb} KB)')

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
    write_runtime_zip(runtime_zip)
    print()
    print('Done. Attach both to the GitHub release.')

if __name__ == '__main__':
    main()
