"""Recenter clipped horror-decal PNGs by bbox-crop then pad to ~12% margin.

Per Day 6 handoff: AI-gen decals (occult sigils, pentagrams, ghost spawns,
blood splatters, graffiti) ship content hugging the canvas edge. The PSX
quad samples 0..1 UV so what looks "cut off" in-game is content authored
flush with the texture border.

Operates in-place on godot-ps1/assets/monitor/textures/. Idempotent:
re-running on an already-recentered file is a no-op (margin already ≥
target). Skips intentionally full-bleed textures (cobweb_corner,
fig_shadow, static_*, glitch_bars, etc.) by name.

Backup of original written next to the PNG with .orig.png suffix the
first time the script touches it.
"""

from pathlib import Path
from PIL import Image

ROOT = Path(r"D:/Documents/JetBrains/PS1Godot/godot-ps1/assets/monitor/textures")
TARGET_MARGIN_PCT = 0.12  # 12% breathing room on each side

# Textures that are AUTHORED full-bleed — leave alone.
FULL_BLEED = {
    "cobweb_corner.png",     # corner-anchored, content meets two edges
    "fig_shadow.png",        # full-canvas circular shadow
    "static_noise.png",      # full-screen overlay
    "static_overlay.png",
    "glitch_bars.png",       # horizontal scanline banding
    "static_interference.png",
}

# Explicit recenter list (matches handoff "obvious offenders" + AI-gen decals
# with margin < 8px). Anything not in this set is left alone.
RECENTER = {
    "blood_decal.png",
    "blood_decal_2.png",
    "blood_decal_3.png",
    "footprints.png",
    "ghost_figure.png",
    "graffiti_dont_look.png",
    "graffiti_he_is_here.png",
    "occult_pentagram.png",
    "occult_sigil.png",
    "shadow_blob.png",
}


def recenter(path: Path) -> tuple[bool, str]:
    im = Image.open(path)
    if im.mode != "RGBA":
        im = im.convert("RGBA")
    w, h = im.size
    bbox = im.getbbox()
    if bbox is None:
        return False, "empty alpha — skipped"

    l, t, r, b = bbox
    cur_margin = min(l, t, w - r, h - b)
    target_margin = int(round(min(w, h) * TARGET_MARGIN_PCT))
    if cur_margin >= target_margin:
        return False, f"margin={cur_margin}px >= {target_margin}px target"

    # Backup once
    backup = path.with_suffix(".orig.png")
    if not backup.exists():
        im.save(backup, "PNG")

    cropped = im.crop(bbox)
    cw, ch = cropped.size

    # Available content area inside target margin
    avail_w = w - 2 * target_margin
    avail_h = h - 2 * target_margin
    scale = min(avail_w / cw, avail_h / ch, 1.0)  # never upscale
    if scale < 1.0:
        new_w = max(1, int(round(cw * scale)))
        new_h = max(1, int(round(ch * scale)))
        cropped = cropped.resize((new_w, new_h), Image.LANCZOS)
        cw, ch = new_w, new_h

    canvas = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    ox = (w - cw) // 2
    oy = (h - ch) // 2
    canvas.paste(cropped, (ox, oy), cropped)
    canvas.save(path, "PNG")
    return True, f"margin {cur_margin}->{min(ox, oy, w-ox-cw, h-oy-ch)}px (target {target_margin})"


def main():
    touched = 0
    for path in sorted(ROOT.rglob("*.png")):
        if path.name in FULL_BLEED:
            continue
        if path.name not in RECENTER:
            continue
        if path.stem.endswith(".orig"):
            continue
        changed, msg = recenter(path)
        flag = "RECENTERED" if changed else "skip"
        print(f"  [{flag:11s}] {path.name:35s} {msg}")
        if changed:
            touched += 1
    print(f"\n{touched} files recentered.")


if __name__ == "__main__":
    main()
