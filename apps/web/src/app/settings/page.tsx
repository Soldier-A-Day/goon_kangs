"use client";

import { useEffect, useState } from "react";
import {
  DEFAULT_SETTINGS,
  loadSettings,
  saveSettings,
  type Settings,
} from "@/lib/settings";

/**
 * 설정 · 접근성 (목업 §11 SCREEN 7.10-E · SAD-ART-001 §15.0).
 *
 * 이 화면의 마지막 블록이 목적을 말한다 —
 * **"타이핑을 전혀 하지 않고도 18일 완주가 가능해야 한다."**
 *
 * 그래서 여기 있는 것은 취향 설정이 아니라 **완주 가능성**이다. 화면 효과는
 * 멀미로 못 하게 만들 수 있고, 색만으로 온도를 알리면 색각 이상인 사람은
 * 밴드를 영영 못 읽는다.
 *
 * **미니게임 홀드 대체는 여기서 빠졌다.** 원형 14종이 각각 다른 조작을 요구하는
 * 설계로 바뀌면서(`docs/game_spec.md`), 전부 홀드로 떨어뜨리는 스위치 하나는
 * 14종을 통째로 없애는 것과 같아졌다. 연타(`MASH`)도 예외 없이 켜져 있다.
 */
export default function SettingsPage() {
  const [settings, setSettings] = useState<Settings>(DEFAULT_SETTINGS);
  const [loaded, setLoaded] = useState(false);

  useEffect(() => {
    setSettings(loadSettings());
    setLoaded(true);
  }, []);

  function set<K extends keyof Settings>(key: K, value: Settings[K]) {
    const next = { ...settings, [key]: value };
    setSettings(next);
    saveSettings(next);
  }

  // 저장값을 읽기 전에 그리면 기본값이 한 번 번쩍인다 — 토글이 제멋대로
  // 움직이는 것처럼 보인다
  if (!loaded) return <main className="p-14 text-ink-2">불러오는 중…</main>;

  return (
    <main className="mx-auto flex w-full max-w-[1000px] flex-1 flex-col px-6 py-6">
      <p className="label">SCREEN 7.10-E — 설정 · 접근성 · 15.0 필수 구현 항목</p>

      <div className="mt-4 border-2 border-rule bg-paper">
        <header className="border-b-2 border-ink bg-paper-2 px-8 py-5">
          <span className="label" style={{ color: "var(--accent)" }}>
            ACCESSIBILITY
          </span>
          <h1 className="mt-1 text-2xl font-extrabold">접근성</h1>
        </header>

        <div className="flex flex-col gap-8 px-8 py-8">
          <Group title="화면 효과 — 개별 토글">
            <Toggle
              label="열 왜곡 (혹서 · 극혹서)"
              on={settings.heatDistort}
              onChange={(v) => set("heatDistort", v)}
            />
            <Toggle
              label="결빙 프레임 (극혹한 · 동상)"
              on={settings.frostFrame}
              onChange={(v) => set("frostFrame", v)}
            />
            <Toggle
              label="패닉 화면 진동 (정신력 0)"
              hint="꺼짐 권장"
              on={settings.panicShake}
              onChange={(v) => set("panicShake", v)}
            />
            <Toggle
              label="화면 흔들림 전반"
              on={settings.screenShake}
              onChange={(v) => set("screenShake", v)}
            />
          </Group>

          <Group title="색 · 표기">
            <Toggle
              label="색각 이상 모드 — 온도 밴드를 아이콘 형태 + 텍스트로 이중 표기"
              on={settings.colorBlind}
              onChange={(v) => set("colorBlind", v)}
            >
              <BandIcons />
            </Toggle>
          </Group>

          <Group title="입력">
            <Toggle
              label="하달 창 키보드 배정 (드래그 대체)"
              on={settings.keyboardDelegation}
              onChange={(v) => set("keyboardDelegation", v)}
            />
          </Group>

          <Group title="온보딩">
            <div className="flex flex-wrap items-center gap-4 bg-paper-3 px-6 py-4">
              <span className="flex-1 text-[1.0625rem]">
                튜토리얼 다시 보기
                <span className="ml-3 text-sm text-ink-2">
                  {settings.tutorialResetAt > 0
                    ? "다음 접속부터 D-01 조작 안내가 처음부터 다시 뜬다"
                    : "이동 · 일과표 · 상호작용 등 D-01 조작 안내를 다시 띄운다"}
                </span>
              </span>
              <button
                type="button"
                onClick={() => set("tutorialResetAt", Date.now())}
                className="h-[38px] w-[150px] shrink-0 text-[0.9375rem]"
                style={{
                  background: "var(--sunk)",
                  border: "1px solid var(--accent)",
                  color: "var(--accent)",
                  fontWeight: 700,
                }}
              >
                다시 보기
              </button>
            </div>
          </Group>

          <Group title="UI 크기">
            <div className="flex flex-wrap items-center gap-4 bg-paper-3 px-6 py-4">
              <span className="w-28 text-[1.0625rem]">UI 스케일</span>
              {([100, 125, 150] as const).map((value) => {
                const on = settings.uiScale === value;
                return (
                  <button
                    key={value}
                    type="button"
                    onClick={() => set("uiScale", value)}
                    aria-pressed={on}
                    className="h-[30px] w-[90px] text-[0.9375rem]"
                    style={{
                      background: on ? "var(--sunk)" : "var(--void)",
                      border: `1px solid ${on ? "var(--accent)" : "var(--rule)"}`,
                      color: on ? "var(--accent)" : "#5a6053",
                      fontWeight: on ? 700 : 400,
                    }}
                  >
                    {value}%
                  </button>
                );
              })}
              <span className="ml-auto text-sm text-ink-2">
                텍스트 크기 별도 조절
              </span>
            </div>
          </Group>

          <div
            className="flex flex-col gap-2 px-6 py-5"
            style={{
              background: "var(--sunk)",
              borderLeft: "5px solid var(--accent)",
            }}
          >
            <span className="label" style={{ color: "var(--accent)" }}>
              DESIGN GUARANTEE
            </span>
            <p className="text-[1.1875rem] font-bold">
              타이핑을 전혀 하지 않고도 18일 완주가 가능해야 한다
            </p>
            <p className="text-sm text-ink-2">
              퀵 커맨드 + 상황판만으로 모든 합동 퀘스트가 해결 — 자유 채팅은 심화
              수단일 뿐
            </p>
          </div>
        </div>
      </div>

      <footer className="mt-6 flex justify-between">
        <button
          type="button"
          onClick={() => {
            setSettings(DEFAULT_SETTINGS);
            saveSettings(DEFAULT_SETTINGS);
          }}
          className="h-[46px] w-[150px] border text-base"
          style={{ borderColor: "var(--ink-2)", color: "var(--ink-2)" }}
        >
          기본값으로
        </button>
        <a
          href="/lobby"
          className="flex h-[46px] w-[150px] items-center justify-center text-base font-extrabold"
          style={{ background: "var(--accent)", color: "var(--paper)" }}
        >
          돌아가기
        </a>
      </footer>
    </main>
  );
}

function Group({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <section className="flex flex-col gap-3">
      <span className="label">{title}</span>
      <div className="flex flex-col gap-2">{children}</div>
    </section>
  );
}

/** 목업의 알약 토글 — 켜지면 손잡이가 오른쪽으로 간다 */
function Toggle({
  label,
  hint,
  on,
  onChange,
  children,
}: {
  label: string;
  hint?: string;
  on: boolean;
  onChange: (on: boolean) => void;
  children?: React.ReactNode;
}) {
  return (
    <div className="flex flex-col gap-3 bg-paper-3 px-6 py-4">
      <div className="flex items-center gap-4">
        <span className="flex-1 text-[1.0625rem]">{label}</span>
        {hint && (
          <span className="text-sm" style={{ color: "var(--heat)" }}>
            {hint}
          </span>
        )}
        <button
          type="button"
          role="switch"
          aria-checked={on}
          aria-label={label}
          onClick={() => onChange(!on)}
          className="relative h-[26px] w-[72px] shrink-0 transition-colors"
          style={{
            background: on ? "var(--sunk)" : "var(--void)",
            border: `1px solid ${on ? "var(--accent)" : "#5a6053"}`,
          }}
        >
          <span
            className="absolute top-1/2 h-[18px] w-[18px] -translate-y-1/2 rounded-full transition-[left]"
            style={{
              left: on ? 48 : 6,
              background: on ? "var(--accent)" : "#5a6053",
            }}
          />
        </button>
      </div>
      {children}
    </div>
  );
}

/**
 * 색각 이상 모드의 실물 (§4.3 · 목업 §11).
 *
 * 온도 밴드를 색으로만 알리면 그 정보가 통째로 사라지는 사람이 있다.
 * 모양이 다르면 색을 못 봐도 읽힌다 — 극혹한은 각진 결정, 평시는 빈 원,
 * 혹서는 채운 원에 뻗은 선.
 */
function BandIcons() {
  return (
    <div className="flex flex-wrap items-center gap-8">
      <span className="flex items-center gap-2" style={{ color: "var(--cold)" }}>
        <svg width="20" height="24" viewBox="0 0 20 24" aria-hidden>
          <path
            d="M10 2V22M2 7L18 17M18 7L2 17"
            stroke="currentColor"
            strokeWidth="2.5"
            fill="none"
          />
        </svg>
        <span className="text-sm">극혹한</span>
      </span>

      <span className="flex items-center gap-2" style={{ color: "var(--ink-2)" }}>
        <svg width="24" height="24" viewBox="0 0 24 24" aria-hidden>
          <circle
            cx="12"
            cy="12"
            r="9"
            fill="none"
            stroke="currentColor"
            strokeWidth="2.5"
          />
        </svg>
        <span className="text-sm">평시</span>
      </span>

      <span className="flex items-center gap-2" style={{ color: "var(--heat)" }}>
        <svg width="24" height="24" viewBox="0 0 24 24" aria-hidden>
          <circle cx="12" cy="12" r="7" fill="currentColor" />
          <path
            d="M12 0v3M12 21v3M0 12h3M21 12h3"
            stroke="currentColor"
            strokeWidth="2"
          />
        </svg>
        <span className="text-sm">혹서</span>
      </span>
    </div>
  );
}
