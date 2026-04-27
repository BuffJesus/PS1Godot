"""Regenerate hum_electrical.wav (backroom feed tone) with a softer
ambience prompt. Original was too harsh/whiny — replacing with a
deeper, less attention-grabbing electrical thrum."""
import requests
import subprocess
from pathlib import Path

OUT = Path(r"D:/Documents/JetBrains/PS1Godot/godot-ps1/assets/monitor/audio/hum_electrical.wav")
KEY_TXT = Path(r"C:/Users/Cornelio/Desktop/key.txt")

PROMPT = (
    "Soft low-frequency electrical hum, deep gentle background ambience, "
    "subtle warm 60Hz transformer thrum with very faint high-frequency "
    "presence. Looping background tone for a horror surveillance room. "
    "NOT harsh, NOT whiny — should sit BENEATH attention, just barely "
    "noticeable. Calm and unobtrusive, no buzz, no whine, no clicks."
)

key = KEY_TXT.read_text().strip().splitlines()[0].strip()
mp3 = OUT.with_suffix(".mp3")
print("calling ElevenLabs...")
r = requests.post(
    "https://api.elevenlabs.io/v1/sound-generation",
    headers={"xi-api-key": key, "Content-Type": "application/json",
             "Accept": "audio/mpeg"},
    json={"text": PROMPT, "duration_seconds": 2.0, "prompt_influence": 0.7},
    timeout=60,
)
if r.status_code != 200:
    print(f"HTTP {r.status_code}: {r.text[:300]}")
    exit(1)
mp3.write_bytes(r.content)
# Convert to PSX-friendly mono 22050Hz 16-bit WAV (audio pipeline rejects MP3)
subprocess.run(
    ["ffmpeg", "-y", "-loglevel", "error", "-i", str(mp3),
     "-ac", "1", "-ar", "11025", "-sample_fmt", "s16",  # 11025 = ambient loops
     str(OUT)], check=True,
)
mp3.unlink()
print(f"wrote {OUT.name} ({OUT.stat().st_size} bytes)")
