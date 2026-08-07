"use client";

/**
 * 접근성 설정 (SAD-ART-001 §15.0 · 목업 §11 SCREEN 7.10-E).
 *
 * §15.0이 **필수 구현**으로 못박은 항목들이다. "있으면 좋은 것"이 아니라
 * 없으면 못 하는 사람이 생기는 것들이라, 기본값도 그렇게 잡았다 —
 * 패닉 화면 진동만 기본으로 꺼져 있다(§4.3이 "개별 토글"을 요구했고,
 * 전정기관에 부담을 주는 유일한 효과다).
 *
 * **브라우저에 저장하고 Unity로 넘긴다.** 로비에서 고른 값이 게임에서
 * 안 먹으면 그 화면은 장식이다. `window.__sadSettings`로 실어 보내고,
 * Unity의 `AccessibilityBridge`가 읽는다.
 */
export interface Settings {
  /** §4.3 열 왜곡 — 혹서·극혹서 */
  heatDistort: boolean;
  /** §4.3 결빙 프레임 — 극혹한·동상 */
  frostFrame: boolean;
  /** §4.3 패닉 화면 진동 — 정신력 0. 기본 꺼짐 */
  panicShake: boolean;
  /** 화면 흔들림 전반 */
  screenShake: boolean;
  /** 색각 이상 모드 — 온도 밴드를 아이콘 + 텍스트로 이중 표기 */
  colorBlind: boolean;
  /** 하달 창 키보드 배정 (드래그 대체) */
  keyboardDelegation: boolean;
  /** UI 스케일 — 100 · 125 · 150 */
  uiScale: 100 | 125 | 150;
  /**
   * F1 — 온보딩(D-01 조작 안내) 다시 보기 신호.
   *
   * 0이면 아무 요청도 없다는 뜻이다. "다시 보기" 버튼을 누르면 그 시각(ms,
   * `Date.now()`)을 넣는다 — 불리언이 아니라 값을 쓰는 이유는 `panicShake`
   * 등과 달리 **1회성 요청**이기 때문이다. Unity의 `Tutorial.cs`가 마지막으로
   * 처리한 값보다 크면 그때 한 번만 초기화한다(같은 값을 계속 봐도 다시
   * 트리거되지 않는다).
   */
  tutorialResetAt: number;
}

export const DEFAULT_SETTINGS: Settings = {
  heatDistort: true,
  frostFrame: true,
  panicShake: false,
  screenShake: true,
  colorBlind: false,
  keyboardDelegation: true,
  uiScale: 100,
  tutorialResetAt: 0,
};

const KEY = "sad.settings";

export function loadSettings(): Settings {
  if (typeof window === "undefined") return DEFAULT_SETTINGS;
  try {
    const raw = localStorage.getItem(KEY);
    if (!raw) return DEFAULT_SETTINGS;
    // 항목이 늘어도 예전에 저장한 설정이 깨지지 않게 기본값 위에 덮는다
    return { ...DEFAULT_SETTINGS, ...(JSON.parse(raw) as Partial<Settings>) };
  } catch {
    return DEFAULT_SETTINGS;
  }
}

export function saveSettings(settings: Settings): void {
  localStorage.setItem(KEY, JSON.stringify(settings));
  publish(settings);
}

/**
 * Unity가 읽을 수 있게 전역에 놓는다.
 *
 * `AccessibilityBridge`가 매 프레임 이 값을 읽어 `WeatherGrading`·`Minigame`에
 * 반영한다. 이벤트를 쓰지 않는 이유는 Unity 인스턴스가 **나중에** 뜨기
 * 때문이다 — 로비에서 설정을 바꾼 뒤 게임이 시작되므로, 그때 그 자리에
 * 값이 있어야 한다.
 */
export function publish(settings: Settings): void {
  if (typeof window === "undefined") return;
  (window as unknown as Record<string, unknown>).__sadSettings = settings;
  document.documentElement.style.setProperty(
    "--ui-scale",
    String(settings.uiScale / 100),
  );
}
