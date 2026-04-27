"""Batch-generate the full set of alpha-keyed horror assets via gpt-image-1.

Outputs to assets/monitor/textures/:
  static_overlay.png       (256x256) — TV noise overlay
  blood_decal_2.png        (64x64)   — blood splatter variant
  blood_decal_3.png        (64x64)   — blood splatter variant (smear)
  graffiti_he_is_here.png  (128x64)  — wall text
  graffiti_dont_look.png   (128x64)  — wall text
  graffiti_run.png         (128x64)  — wall text
  occult_pentagram.png     (128x128) — pentagram symbol
  occult_sigil.png         (128x128) — eldritch sigil
  ghost_figure.png         (64x128)  — humanoid silhouette
  crack_wall.png           (128x64)  — wall crack streak
"""
import requests, base64, io, time
from pathlib import Path
from PIL import Image

ROOT = Path(r"D:/Documents/JetBrains/PS1Godot/godot-ps1/assets/monitor/textures")
KEY_TXT = Path(r"C:/Users/Cornelio/Desktop/key.txt")


def fetch_openai_key():
    if not KEY_TXT.exists(): return None
    return KEY_TXT.read_text().strip().splitlines()[1].strip()


def gen(key, prompt, out_path, size_xy):
    body = {"model": "gpt-image-1", "prompt": prompt, "size": "1024x1024",
            "quality": "low", "background": "transparent", "n": 1}
    print(f"  {out_path.name}...", end=" ", flush=True)
    resp = requests.post("https://api.openai.com/v1/images/generations",
                         headers={"Authorization": f"Bearer {key}",
                                  "Content-Type": "application/json"},
                         json=body, timeout=180)
    if resp.status_code != 200:
        print(f"HTTP {resp.status_code}: {resp.text[:200]}")
        return False
    img = Image.open(io.BytesIO(base64.b64decode(
        resp.json()["data"][0]["b64_json"]))).convert("RGBA")
    img = img.resize(size_xy, Image.Resampling.NEAREST)
    img.save(out_path)
    px = img.load()
    a0 = sum(1 for y in range(size_xy[1]) for x in range(size_xy[0])
             if px[x, y][3] == 0)
    total = size_xy[0] * size_xy[1]
    print(f"ok ({100*a0//total}% transparent)")
    return True


PROMPTS = [
    # (filename, size, prompt)
    ("static_overlay.png", (256, 256),
     "PS1-era CRT TV static white noise pattern. Dense field of small "
     "white and gray dots and grains, with patches of fully transparent "
     "pixels scattered through (so it can overlay other content). About "
     "60% of pixels visible (white/gray noise), 40% transparent. NO "
     "uniform background — it's a noise field, not a solid frame."),
    ("blood_decal_2.png", (64, 64),
     "PS1-era blood splatter, smaller compact pool with 5-6 satellite "
     "droplets in a wider scatter pattern, dark crimson, hand-painted "
     "pixel art, hard edges, transparent background, 1990s low-res look."),
    ("blood_decal_3.png", (64, 64),
     "PS1-era blood smear/streak, dark red horizontal smear like dragged "
     "across a wall, about 40 pixels long, irregular edges, transparent "
     "background, hand-painted pixel art, retro horror aesthetic."),
    ("graffiti_he_is_here.png", (128, 64),
     "Spray-painted text 'HE IS HERE' in dark red sloppy capital letters, "
     "horror movie aesthetic, dripping paint, transparent background, "
     "PS1-era pixel art style, hard edges, NO border or background."),
    ("graffiti_dont_look.png", (128, 64),
     "Spray-painted text 'DONT LOOK' in dark red dripping capital letters, "
     "horror aesthetic, transparent background, PS1-era pixel art, no "
     "background frame, just the text floating."),
    ("graffiti_run.png", (128, 64),
     "Large spray-painted single word 'RUN' in scrawled dark red dripping "
     "capital letters, urgent horror aesthetic, transparent background, "
     "PS1-era pixel art, no background frame."),
    ("occult_pentagram.png", (128, 128),
     "Dark red occult pentagram symbol, hand-drawn five-pointed star "
     "inside a circle, jagged hand-drawn lines, transparent background, "
     "PS1-era horror aesthetic, low-res pixel art, ritual marking style."),
    ("occult_sigil.png", (128, 128),
     "Eldritch occult sigil symbol, dark red angular geometric ritual "
     "marking with intersecting lines and small auxiliary marks, "
     "hand-drawn cult aesthetic, transparent background, PS1-era "
     "pixel art, looks like it was scratched into a wall."),
    ("ghost_figure.png", (64, 128),
     "Translucent ghostly humanoid silhouette, faint pale white-blue "
     "vertical figure with outstretched form, semi-transparent edges, "
     "horror game ghost, transparent background everywhere except the "
     "figure itself, PS1-era pixel art, low-res."),
    ("crack_wall.png", (128, 64),
     "Single jagged hairline crack pattern, thin dark gray-black "
     "branching line spreading horizontally about 100 pixels wide, "
     "looks like a cracked wall, transparent background, PS1-era "
     "pixel art, hard edges."),
]


if __name__ == "__main__":
    key = fetch_openai_key()
    if not key:
        print("no OpenAI key"); exit()
    print(f"generating {len(PROMPTS)} assets...")
    for fname, size, prompt in PROMPTS:
        gen(key, prompt, ROOT / fname, size)
        time.sleep(0.5)  # polite pacing
    print("done.")
