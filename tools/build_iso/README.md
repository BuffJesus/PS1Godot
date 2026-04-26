# build_iso — PS1Godot → mkpsxiso → game.bin

Builds a PSX-bootable ISO from the PS1Godot export triplet
(splashpack/.vram/.spu) plus the `.xa` sidecar produced when Route=XA
clips are converted via psxavenc.

## Why

PCdrv (the default dev workflow) can't play XA-ADPCM — the PSX SPU
decodes XA from CD Form-2 sectors, and host filesystem doesn't carry
that. To test the XA streaming path you need:

1. A `LOADER=cdrom` build of psxsplash.
2. An ISO with `.xa` files written as XA Form-2 sectors (mkpsxiso
   `type="xa"`).
3. PCSX-Redux running the ISO via its CD-ROM emulator (not PCdrv).

This script handles step 2 and the surrounding XML scaffolding.

## Prerequisites

- **mkpsxiso** ≥ 2.20: <https://github.com/Lameguy64/mkpsxiso/releases>
  (set `MKPSXISO` env var or put on PATH; falls back to
  `C:\tools\mkpsxiso\mkpsxiso-2.20-win64\mkpsxiso.exe`).
- **psxsplash CDROM build**: `make LOADER=cdrom` in `psxsplash-main/`,
  then copy `psxsplash.ps-exe` into `godot-ps1/build/`.
- **Optional**: a `SYSTEM.CNF` for the ISO. Without one the disc may
  not boot on hardware (emulators are forgiving).

## Usage

```sh
# Default: writes godot-ps1/build/game.bin + .cue + .xml
python tools/build_iso/build_iso.py

# Custom output
python tools/build_iso/build_iso.py --out /tmp/jam_demo.bin

# Just emit the XML (skip mkpsxiso); useful for inspecting the layout
python tools/build_iso/build_iso.py --xml-only
```

## Output

- `<out>.xml` — mkpsxiso config (kept around so you can inspect / tweak).
- `<out>.bin` — combined data + XA tracks.
- `<out>.cue` — cue sheet pointing at `<out>.bin`.

Mount the `.cue` file in PCSX-Redux's CD-ROM tab. XA-routed clips will
then route through `XaAudioBackend::play()` instead of the PCdrv
short-circuit path.

## What the XML lays out

```
disc layout
├── SYSTEM.CNF             (optional)
├── PSXSPLASH.PS-EXE       (CDROM-loader build of the runtime)
├── SCENE_0.SPK / .VRM / .SPU
├── SCENE_1.SPK / .VRM / .SPU
├── ...
└── SCENE_<n>.XA           (Form-2, type="xa")
```

`SCENE_<n>.XA` files are tagged `type="xa"` so mkpsxiso writes them as
Form-2 sectors with the XA-CD-ROM flag set. The runtime loads them via
`psyqo::CDRomDevice::readSectors()` once `XaAudioBackend::play()` is
finished (next session).

## Status

- [x] XML emit
- [x] mkpsxiso invocation
- [x] mkpsxiso install detection
- [ ] LBA write-back into the splashpack — once `XaAudioBackend` needs
      a starting LBA per XA clip, we'll either parse iso9660 root dir
      at runtime OR patch the splashpack post-mkpsxiso with the LBAs
      from `mkpsxiso -lba`. Decision pending the runtime side.
