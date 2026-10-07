#!/usr/bin/env python3
"""Fits the narrator's recordings to the intro's story cards.

For each Tools/voice/source/LineNN.mp3: trim the silence at both ends, slow it (pitch kept) so the speech fills
its card from VOICE_DELAY in until FADE_OUT before the card ends -- but never faster than recorded and never
slower than MIN_TEMPO, so the voice stays natural (a line that runs long makes its card longer instead) --
normalise the loudness, and write Assets/_Project/Resources/Story/Voice/LineNN.wav (mono, 16-bit; Unity compresses it in builds).

Card lengths come from Assets/_Project/Resources/Story/Intro.txt. Needs ffmpeg. Run from the project root:
    python3 Tools/voice/fit_voice.py
"""
import re, subprocess, pathlib

ROOT = pathlib.Path(__file__).resolve().parents[2]
SRC = ROOT / "Tools/voice/source"
OUT = ROOT / "Assets/_Project/Resources/Story/Voice"
INTRO = ROOT / "Assets/_Project/Resources/Story/Intro.txt"
VOICE_DELAY = 0.5   # seconds into the card before the narrator starts (TitleSequence.VoiceDelay)
FADE_OUT = 1.1      # the line ends this long before its card does (the words fade out over the last second)
MIN_TEMPO = 0.82    # slowest playback: 18% slower than recorded

def cards():
    out = []
    for line in INTRO.read_text().splitlines():
        line = line.strip()
        if not line or line.startswith("#"): continue
        out.append(float(line.split("|")[0]))
    return out

def speech_span(path):
    dur = float(subprocess.run(["ffprobe", "-v", "error", "-show_entries", "format=duration", "-of", "csv=p=0", str(path)],
                               capture_output=True, text=True).stdout)
    log = subprocess.run(["ffmpeg", "-hide_banner", "-i", str(path), "-af", "silencedetect=noise=-40dB:d=0.2", "-f", "null", "-"],
                         capture_output=True, text=True).stderr
    starts = [float(x) for x in re.findall(r"silence_start: ([\d.]+)", log)]
    ends = [float(x) for x in re.findall(r"silence_end: ([\d.]+)", log)]
    begin = ends[0] if starts and starts[0] < 0.05 and ends else 0.0
    finish = starts[-1] if starts and (not ends or ends[-1] >= dur - 0.05) and starts[-1] > begin else dur
    return max(0.0, begin - 0.05), min(dur, finish + 0.15)

def main():
    OUT.mkdir(parents=True, exist_ok=True)
    for i, seconds in enumerate(cards(), start=1):
        src = SRC / f"Line{i:02}.mp3"
        if not src.exists():
            print(f"Line{i:02}: no recording"); continue
        a, b = speech_span(src)
        speech = b - a
        target = seconds - VOICE_DELAY - FADE_OUT
        tempo = max(MIN_TEMPO, min(1.0, speech / target))
        dst = OUT / f"Line{i:02}.wav"
        subprocess.run(["ffmpeg", "-hide_banner", "-loglevel", "error", "-y", "-i", str(src),
                        "-af", f"atrim={a:.3f}:{b:.3f},asetpts=PTS-STARTPTS,atempo={tempo:.4f},"
                               "loudnorm=I=-16:TP=-1.5:LRA=11,"
                               "afade=t=in:d=0.02,areverse,afade=t=in:d=0.08,areverse",
                        "-ac", "1", "-ar", "44100", "-c:a", "pcm_s16le", str(dst)], check=True)
        out_len = speech / tempo
        hold = max(seconds, out_len + VOICE_DELAY + 0.8)
        print(f"Line{i:02}: speech {speech:4.2f}s, card {seconds:3.1f}s -> tempo {tempo:.2f}, spoken {out_len:4.2f}s, card holds {hold:4.2f}s")

main()
