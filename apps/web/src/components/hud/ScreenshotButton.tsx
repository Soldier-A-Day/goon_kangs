"use client";

import { useCallback, useState, type RefObject } from "react";

/**
 * 게임 화면 캡처 버튼 (H-1).
 *
 * 런 요약 카드(C-1)와 결합하면 공유물이 공짜로 생긴다는 발주 그대로 —
 * 지금은 `canvas.toDataURL()` 한 줄로 PNG를 뽑아 바로 내려받는다.
 *
 * ── 검은 화면 함정 ────────────────────────────────────────────────────
 * WebGL 캔버스는 그린 뒤 브라우저가 화면에 합성하고 나면 기본적으로 버퍼를
 * 비운다(`preserveDrawingBuffer: false`가 기본값). 그 상태에서 `toDataURL`을
 * 부르면 검은 PNG만 나온다. Unity 로더 쪽 렌더 루프는 wasm 안에 있어
 * `requestAnimationFrame` 직후를 노려 캡처하는 타이밍 트릭을 JS에서 걸 수
 * 없으므로, `play/[code]/page.tsx`가 `createUnityInstance` 설정에
 * `webglContextAttributes: { preserveDrawingBuffer: true }`를 넘겨 버퍼
 * 자체를 유지하게 만들었다 — 이 버튼은 그 전제 위에서만 동작한다.
 *
 * ── 포커스 안 뺏기 ────────────────────────────────────────────────────
 * 버튼을 마우스로 누르면 브라우저가 기본적으로 포커스를 버튼으로 옮긴다.
 * 그러면 Unity 캔버스가 키 입력을 놓쳐 조작이 끊긴다 — `onMouseDown`에서
 * `preventDefault`로 그 기본 동작만 끊고, 클릭은 `onClick`에서 그대로
 * 처리한다. 키보드 탭 포커스는 열어 둔다(접근성, H-4).
 */
export function ScreenshotButton({
  canvasRef,
}: {
  /**
   * 엘리먼트가 아니라 ref를 받는다 — 렌더 중에 `ref.current`를 읽으면 린트가
   * 막는다(refs는 렌더 바깥, 이벤트 핸들러에서만 읽어야 한다). 실제 캔버스는
   * 클릭이 일어난 시점에야 필요하므로 이 편이 자연스럽기도 하다
   */
  canvasRef: RefObject<HTMLCanvasElement | null>;
}) {
  const [flash, setFlash] = useState(false);

  const capture = useCallback(() => {
    const canvas = canvasRef.current;
    if (!canvas) return;
    try {
      const dataUrl = canvas.toDataURL("image/png");
      const link = document.createElement("a");
      link.href = dataUrl;
      link.download = `SAD_${timestamp()}.png`;
      link.click();
      setFlash(true);
      window.setTimeout(() => setFlash(false), 200);
    } catch {
      // 캔버스가 아직 준비 전이거나 컨텍스트가 유실된 경우 — 조용히 무시한다.
      // 스크린샷 실패로 게임을 멈출 이유는 없다
    }
  }, [canvasRef]);

  return (
    <button
      type="button"
      aria-label="화면 캡처"
      title="화면 캡처"
      onMouseDown={(event) => event.preventDefault()}
      onClick={capture}
      className="absolute right-4 top-4 z-10 flex h-10 w-10 items-center justify-center border border-rule bg-paper/80 text-ink-2 backdrop-blur transition-colors hover:border-accent hover:text-ink"
    >
      <CameraIcon />
      {flash && <span className="absolute inset-0 bg-paper" />}
    </button>
  );
}

function CameraIcon() {
  return (
    <svg
      width="18"
      height="18"
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth="2"
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden="true"
    >
      <path d="M4 8h3l1.5-2h7L17 8h3a1 1 0 0 1 1 1v9a1 1 0 0 1-1 1H4a1 1 0 0 1-1-1V9a1 1 0 0 1 1-1Z" />
      <circle cx="12" cy="13" r="3.2" />
    </svg>
  );
}

/** `SAD_20260803_143022.png` 꼴 — 정렬용 구분자 없이 이어 붙여야 파일명이 안전하다 */
function timestamp(): string {
  const now = new Date();
  const pad = (n: number) => String(n).padStart(2, "0");
  const date = `${now.getFullYear()}${pad(now.getMonth() + 1)}${pad(now.getDate())}`;
  const time = `${pad(now.getHours())}${pad(now.getMinutes())}${pad(now.getSeconds())}`;
  return `${date}_${time}`;
}
