#!/usr/bin/env python3
"""저장소 최초의 소리 4종을 파이썬으로 직접 합성한다 (A-2).

외부 음원을 받지 않는다 — 라이선스를 확인할 길이 없다. 사인파 + 노이즈 +
짧은 감쇠 봉투만으로 만든다. 컨셉은 "등사판 서류": 낮고 건조하게, 8비트
게임기의 밝고 통통 튀는 삑삑이가 되면 안 된다. 그래서

  - 기본 주파수를 전부 낮게 잡는다(85~330Hz대 — 화려한 칩튠은 500Hz 위에서 산다)
  - 서스테인·비브라토를 안 쓴다. 어택은 짧고 감쇠는 지수형이다 — 도장 찍듯 뚝 끊긴다
  - 사각파 대신 `tanh` 소프트클립("buzz")을 쓴다. 순수 구형파는 그 자체로
    레트로 게임기 소리를 낸다

재생성:
    python3 tools/audio/gen_sfx.py

출력: unity/Assets/Resources/Audio/{tap,success,miss,warning}.wav
      (PCM 16bit, 22.05kHz, 모노)

왜 `Resources/Audio`인가: `Sfx.cs`가 씬 수정도 인스펙터 배선도 없이 런타임에
`Resources.Load<AudioClip>()`로 클립을 찾아야 한다 — Unity에서 임포트 후
그게 되는 유일한 폴더가 `Resources`다. 발주서가 적은 원래 경로
`unity/Assets/Audio/`는 인스펙터에 수동으로 끌어다 놓는 워크플로에서만
동작하는데, 그러면 이 생성 스크립트가 "재생성 가능"하지 않게 된다.
"""
import math
import os
import random
import struct
import wave

SAMPLE_RATE = 22050
OUT_DIR = os.path.normpath(os.path.join(
    os.path.dirname(os.path.abspath(__file__)),
    "..", "..", "unity", "Assets", "Resources", "Audio"))


def _sine(freq, t):
    return math.sin(2.0 * math.pi * freq * t)


def _tone(freq, duration, amp=0.5, attack=0.004, decay_tau=0.05,
          wave_shape="sine", drift=0.0, sample_rate=SAMPLE_RATE):
    """짧은 톤 하나. attack 램프 뒤 지수 감쇠 — 서스테인이 없어 "드라이"하다."""
    n = int(duration * sample_rate)
    out = [0.0] * n
    for i in range(n):
        t = i / sample_rate
        env = (t / attack) if attack > 0 and t < attack else 1.0
        env = min(1.0, env) * math.exp(-t / decay_tau)
        f = freq * (1.0 + drift * t)
        phase = 2.0 * math.pi * f * t
        if wave_shape == "sine":
            s = math.sin(phase)
        elif wave_shape == "tri":
            s = 2.0 * abs(2.0 * ((f * t) % 1.0) - 1.0) - 1.0
        elif wave_shape == "buzz":
            # 소프트클립한 사인 — 거칠지만 순수 구형파의 "칩튠" 색은 없다
            k = 3.2
            s = math.tanh(k * math.sin(phase)) / math.tanh(k)
        else:
            s = math.sin(phase)
        out[i] = amp * env * s
    return out


def _noise(duration, amp=0.3, decay_tau=0.03, seed=0, smooth=2,
           sample_rate=SAMPLE_RATE):
    """감쇠하는 노이즈 질감. 이동평균으로 저역만 남겨 거친 히스를 죽인다."""
    rnd = random.Random(seed)
    n = int(duration * sample_rate)
    raw = [rnd.uniform(-1.0, 1.0) for _ in range(n)]
    if smooth > 0:
        sm = [0.0] * n
        for i in range(n):
            lo = max(0, i - smooth)
            hi = min(n, i + smooth + 1)
            sm[i] = sum(raw[lo:hi]) / (hi - lo)
        raw = sm
    out = [0.0] * n
    for i in range(n):
        t = i / sample_rate
        out[i] = amp * math.exp(-t / decay_tau) * raw[i]
    return out


def _mix(*layers):
    n = max(len(l) for l in layers)
    out = [0.0] * n
    for layer in layers:
        for i, v in enumerate(layer):
            out[i] += v
    return out


def _concat(*chunks_or_gaps, sample_rate=SAMPLE_RATE):
    """숫자가 오면 그만큼(초) 무음, 리스트가 오면 그 샘플을 이어붙인다."""
    out = []
    for item in chunks_or_gaps:
        if isinstance(item, (int, float)):
            out.extend([0.0] * int(item * sample_rate))
        else:
            out.extend(item)
    return out


def _write_wav(path, samples, sample_rate=SAMPLE_RATE):
    peak = max(0.0001, max(abs(s) for s in samples))
    # 헤드룸을 남기고 정규화한다(클리핑 방지, 대략 -1.4dBFS)
    scale = min(1.0, 0.85 / peak)
    frames = bytearray()
    for s in samples:
        v = max(-1.0, min(1.0, s * scale))
        frames += struct.pack("<h", int(v * 32767))
    with wave.open(path, "wb") as f:
        f.setnchannels(1)
        f.setsampwidth(2)
        f.setframerate(sample_rate)
        f.writeframes(bytes(frames))
    return len(samples) / sample_rate


def gen_tap():
    """80~120ms 짧은 톡 — 관인을 찍는 소리에 가깝게. 낮은 퍼커시브 톤 + 미세 노이즈."""
    body = _tone(170, 0.09, amp=0.55, attack=0.001, decay_tau=0.018, wave_shape="sine")
    click = _noise(0.02, amp=0.22, decay_tau=0.007, seed=1, smooth=1)
    return _mix(body, click)


def gen_success():
    """2음 상승, 300ms — 220Hz → 330Hz(완전5도). 벨이 아니라 드라이한 스탬프 톤."""
    a = _mix(
        _tone(220, 0.14, amp=0.5, attack=0.003, decay_tau=0.09, wave_shape="sine"),
        _tone(220, 0.14, amp=0.12, attack=0.003, decay_tau=0.07, wave_shape="tri"),
    )
    b = _mix(
        _tone(330, 0.16, amp=0.5, attack=0.003, decay_tau=0.11, wave_shape="sine"),
        _tone(330, 0.16, amp=0.12, attack=0.003, decay_tau=0.08, wave_shape="tri"),
    )
    return _concat(a, b)


def gen_miss():
    """낮은 버즈, 150ms — 저역 두 사인의 비팅 + 소프트클립 grit."""
    a = _tone(85, 0.15, amp=0.42, attack=0.001, decay_tau=0.06, wave_shape="buzz")
    b = _tone(91, 0.15, amp=0.30, attack=0.001, decay_tau=0.06, wave_shape="sine")
    grit = _noise(0.15, amp=0.06, decay_tau=0.05, seed=2, smooth=1)
    return _mix(a, b, grit)


def gen_warning():
    """2회 삐, 400ms — 낮고 건조한 두 펄스. 8비트 알림음이 되지 않게 buzz 파형."""
    beep = _tone(260, 0.13, amp=0.5, attack=0.002, decay_tau=0.05, wave_shape="buzz")
    return _concat(beep, 0.14, beep)


def main():
    os.makedirs(OUT_DIR, exist_ok=True)
    generators = {
        "tap": gen_tap,
        "success": gen_success,
        "miss": gen_miss,
        "warning": gen_warning,
    }
    for name, gen in generators.items():
        samples = gen()
        path = os.path.join(OUT_DIR, f"{name}.wav")
        duration = _write_wav(path, samples)
        size = os.path.getsize(path)
        print(f"{name}: {duration * 1000:.0f}ms, {size}B -> {path}")


if __name__ == "__main__":
    main()
