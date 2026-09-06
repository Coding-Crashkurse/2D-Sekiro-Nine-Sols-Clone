"""
Import the ElevenLabs audio from AudioRaw/ into the Unity project.

  * SFX  : trimmed (leading/trailing silence), peak-normalised to -1 dBFS, 44.1 kHz mp3
           -> AshenSol/Assets/_Game/Resources/Audio/SFX/<name>.mp3
  * Music: peak-normalised to -1.5 dBFS, 44.1 kHz mp3
           -> AshenSol/Assets/_Game/Resources/Audio/Music/<name>.mp3

Mapping for SFX comes from AudioRaw/sfx_map.txt ("<source file> <target name>" per line).
Looping beds (ambiences, drone hum) keep their tails so the crossfade loop stays smooth.

Run: python Tools/import_audio.py
"""
import os, re, subprocess, sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
RAW_SFX = os.path.join(ROOT, "AudioRaw", "sfx")
RAW_MUSIC = os.path.join(ROOT, "AudioRaw", "music")
MAP = os.path.join(ROOT, "AudioRaw", "sfx_map.txt")
OUT_SFX = os.path.join(ROOT, "AshenSol", "Assets", "_Game", "Resources", "Audio", "SFX")
OUT_MUSIC = os.path.join(ROOT, "AshenSol", "Assets", "_Game", "Resources", "Audio", "Music")

# music files are generated in this order (see the compose_music calls)
MUSIC_ORDER = ["music_title", "music_level1", "music_boss", "music_victory"]

# these keep their silence/tails: they loop or are meant to breathe
NO_TRIM = {"ambience_wind", "ambience_arena", "drone_hover"}

os.makedirs(OUT_SFX, exist_ok=True)
os.makedirs(OUT_MUSIC, exist_ok=True)


def peak_db(path):
    """Return the file's peak level in dBFS (0 = full scale)."""
    r = subprocess.run(["ffmpeg", "-hide_banner", "-i", path, "-af", "volumedetect", "-f", "null", "-"],
                       capture_output=True, text=True)
    m = re.search(r"max_volume:\s*(-?\d+(?:\.\d+)?) dB", r.stderr)
    return float(m.group(1)) if m else 0.0


def convert(src, dst, target_db, trim):
    gain = target_db - peak_db(src)
    filters = []
    if trim:
        # strip leading and trailing near-silence
        filters.append("silenceremove=start_periods=1:start_threshold=-50dB:start_silence=0.01")
        filters.append("areverse")
        filters.append("silenceremove=start_periods=1:start_threshold=-50dB:start_silence=0.01")
        filters.append("areverse")
    filters.append(f"volume={gain:.2f}dB")
    cmd = ["ffmpeg", "-hide_banner", "-loglevel", "error", "-y", "-i", src,
           "-af", ",".join(filters), "-ar", "44100", "-b:a", "192k", dst]
    subprocess.run(cmd, check=True)
    return gain


def main():
    if not os.path.exists(MAP):
        print("missing", MAP); sys.exit(1)

    done, missing = 0, []
    for line in open(MAP, encoding="utf8"):
        line = line.strip()
        if not line or line.startswith("#"):
            continue
        parts = line.split()
        if len(parts) < 2:
            continue
        src_name, target = parts[0], parts[1]
        src = os.path.join(RAW_SFX, src_name)
        if not os.path.exists(src):
            missing.append(src_name); continue
        dst = os.path.join(OUT_SFX, target + ".mp3")
        gain = convert(src, dst, -1.0, trim=target not in NO_TRIM)
        done += 1
        print(f"  sfx  {target:22} {gain:+6.1f} dB")

    music = sorted(f for f in os.listdir(RAW_MUSIC) if f.endswith(".mp3"))
    for i, f in enumerate(music):
        if i >= len(MUSIC_ORDER):
            print("  extra music file ignored:", f); continue
        dst = os.path.join(OUT_MUSIC, MUSIC_ORDER[i] + ".mp3")
        gain = convert(os.path.join(RAW_MUSIC, f), dst, -1.5, trim=False)
        print(f"  music {MUSIC_ORDER[i]:22} {gain:+6.1f} dB   <- {f}")

    print(f"\n{done} sfx + {min(len(music), len(MUSIC_ORDER))} music imported")
    if missing:
        print("MISSING SOURCES:", ", ".join(missing))


if __name__ == "__main__":
    main()
