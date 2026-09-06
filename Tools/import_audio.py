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
RAW_INTRO_MUSIC = os.path.join(ROOT, "AudioRaw", "music_intro")
RAW_VOICE = os.path.join(ROOT, "AudioRaw", "voice")
MAP = os.path.join(ROOT, "AudioRaw", "sfx_map.txt")
VOICE_MAP = os.path.join(ROOT, "AudioRaw", "voice_map.txt")
OUT_SFX = os.path.join(ROOT, "AshenSol", "Assets", "_Game", "Resources", "Audio", "SFX")
OUT_MUSIC = os.path.join(ROOT, "AshenSol", "Assets", "_Game", "Resources", "Audio", "Music")
OUT_VOICE = os.path.join(ROOT, "AshenSol", "Assets", "_Game", "Resources", "Audio", "Voice")

# the intro narration, in panel order (files are used oldest-first)
VOICE_ORDER = ["intro_1", "intro_2", "intro_3", "intro_4", "intro_5"]

# music files are generated in this order (see the compose_music calls)
MUSIC_ORDER = ["music_title", "music_level1", "music_boss", "music_victory"]

# folder in AudioRaw/ -> Resources name
EXTRA_MUSIC = [("music_intro", "music_intro"), ("music_works", "music_works"), ("music_artisan", "music_artisan"), ("music_stair", "music_stair")]

# boss barks: a normal deep voice pitched down and doubled into something inhuman
BOSS_VOICE = [("tts_You_s", "warden_1"), ("tts_Now_h", "warden_2")]
MONSTER_FILTER = ("asplit=2[a][b];[a]asetrate=44100*0.80,aresample=44100,atempo=1.12[a1];"
                  "[b]asetrate=44100*0.74,aresample=44100,atempo=1.12,adelay=40|40,volume=0.5[b1];"
                  "[a1][b1]amix=inputs=2:normalize=0,aecho=0.8:0.85:120:0.25,"
                  "acompressor=threshold=-18dB:ratio=3,volume=3dB")

# these keep their silence/tails: they loop or are meant to breathe
NO_TRIM = {"ambience_wind", "ambience_arena", "drone_hover"}

os.makedirs(OUT_SFX, exist_ok=True)
os.makedirs(OUT_MUSIC, exist_ok=True)
os.makedirs(OUT_VOICE, exist_ok=True)


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

    # extra tracks each live in their own folder so they cannot shift the MUSIC_ORDER mapping
    for folder, target in EXTRA_MUSIC:
        d = os.path.join(ROOT, "AudioRaw", folder)
        files = sorted(f for f in os.listdir(d) if f.endswith(".mp3")) if os.path.isdir(d) else []
        if not files:
            continue
        gain = convert(os.path.join(d, files[-1]), os.path.join(OUT_MUSIC, target + ".mp3"), -1.5, trim=False)
        print("  music %-22s %+6.1f dB   <- %s" % (target, gain, files[-1]))

    # narration is mapped explicitly: ElevenLabs names files after the first words, so neither
    # alphabetical nor generation order matches the script
    voice = []
    if os.path.exists(VOICE_MAP):
        for line in open(VOICE_MAP, encoding="utf8"):
            line = line.strip()
            if not line or line.startswith("#"):
                continue
            parts = line.split()
            src = os.path.join(RAW_VOICE, parts[0])
            if not os.path.exists(src):
                missing.append(parts[0]); continue
            gain = convert(src, os.path.join(OUT_VOICE, parts[1] + ".mp3"), -1.0, trim=True)
            voice.append(parts[1])
            print("  voice %-22s %+6.1f dB   <- %s" % (parts[1], gain, parts[0]))

    print(f"\n{done} sfx + {min(len(music), len(MUSIC_ORDER))} music + {len(voice)} voice lines imported")
    if missing:
        print("MISSING SOURCES:", ", ".join(missing))


if __name__ == "__main__":
    main()
