"use client";

import Link from "next/link";
import { useParams, useRouter } from "next/navigation";
import { useEffect, useRef, useState } from "react";
import { HTTP_BASE, WS_BASE } from "@/lib/api";
import { loadSession } from "@/lib/session";

/**
 * 게임 화면 — Unity WebGL.
 *
 * 여기 있던 DOM 클라이언트는 걷어냈다. 그건 Unity가 없던 시절 "이동 + 붙잡기 +
 * 판정만 있는 상태에서 하루 루프가 긴장을 만드는가"에 답하려고 세운 임시
 * 화면이었고, 그 질문에는 이미 답이 나왔다. **화면이 둘이면 규칙도 둘이 된다** —
 * 미니게임 원형 14종은 Unity에만 있으므로 DOM 쪽은 판을 통과할 방법이 없다.
 *
 * ── 로비가 잡아준 방에 붙는다 ────────────────────────────────────────────
 * 웹 로비가 방을 만들고 시작까지 마친 뒤 세션 토큰을 준다. 그 토큰을 쿼리로
 * 넘기면 Unity(`NetBootstrap`)가 **방을 새로 만들지 않고** 그대로 접속한다.
 * 안 넘기면 Unity가 혼자 있는 새 방을 만들어 들어가는데, 화면은 정상으로
 * 보이므로 같이 하려던 사람들과 왜 못 만나는지 알 길이 없다.
 *
 * ── 번들은 저장소에 없다 ─────────────────────────────────────────────────
 * `node tools/webgl-sync.mjs`가 `unity/Build/web`을 `public/game/`으로 나른다.
 * 브로틀리 헤더는 `next.config.ts`가 붙인다.
 */
const LOADER = "/game/Build/web.loader.js";

declare global {
  interface Window {
    createUnityInstance?: (
      canvas: HTMLCanvasElement,
      config: Record<string, unknown>,
      onProgress?: (progress: number) => void,
    ) => Promise<{ Quit: () => Promise<void> }>;
  }
}

export default function PlayPage() {
  const params = useParams<{ code: string }>();
  const router = useRouter();
  const code = (params.code ?? "").toUpperCase();

  const canvasRef = useRef<HTMLCanvasElement>(null);
  const [progress, setProgress] = useState(0);
  const [failed, setFailed] = useState<string | null>(null);

  useEffect(() => {
    const session = loadSession();
    if (!session || session.code !== code) {
      router.replace("/lobby?mode=join");
      return;
    }

    const canvas = canvasRef.current;
    if (!canvas) return;

    // 서버 주소와 토큰을 쿼리로 넘긴다. WebGL에는 커맨드라인 인자가 없어
    // 통로가 URL뿐이고, 빌드를 다시 하지 않고 로컬·원격을 바꿔 붙일 수 있다
    const query = new URLSearchParams({
      token: session.token,
      code: session.code,
      http: HTTP_BASE,
      ws: WS_BASE,
    });
    if (typeof window !== "undefined") {
      window.history.replaceState(null, "", `${window.location.pathname}?${query}`);
    }

    let instance: { Quit: () => Promise<void> } | null = null;
    let dead = false;

    const script = document.createElement("script");
    script.src = LOADER;
    script.onerror = () =>
      setFailed("번들을 못 찾았다 — `node tools/webgl-sync.mjs`로 빌드를 옮겨야 한다");
    script.onload = () => {
      if (dead || !window.createUnityInstance) return;
      window
        .createUnityInstance(
          canvas,
          {
            dataUrl: "/game/Build/web.data.br",
            frameworkUrl: "/game/Build/web.framework.js.br",
            codeUrl: "/game/Build/web.wasm.br",
            streamingAssetsUrl: "/game/StreamingAssets",
            companyName: "SoldierADay",
            productName: "SOLDIER : A DAY",
            productVersion: "1.0",
            // **레티나에서는 실제 픽셀 밀도로 그린다.**
            //
            // 1로 고정하면 캔버스가 CSS 픽셀 크기로 렌더된 뒤 브라우저가
            // 2배로 늘린다 — 글자도 HUD도 캐릭터도 전부 뭉개진다. §2.1이
            // 픽셀 퍼펙트를 위해 정수 배율을 요구한 것은 **월드 타일** 이야기이고,
            // UI는 네이티브 해상도로 그리는 것이 같은 §2.1의 결정이다.
            devicePixelRatio: typeof window === "undefined" ? 1 : window.devicePixelRatio,
          },
          (value) => setProgress(value),
        )
        .then((made) => {
          if (dead) {
            void made.Quit();
            return;
          }
          instance = made;
          canvas.focus();
        })
        .catch((message: unknown) => setFailed(String(message)));
    };
    document.body.appendChild(script);

    return () => {
      dead = true;
      script.remove();
      void instance?.Quit();
    };
  }, [code, router]);

  const loading = progress < 1 && !failed;

  return (
    <main className="relative flex min-h-screen flex-col bg-paper">
      <canvas
        ref={canvasRef}
        id="unity-canvas"
        tabIndex={1}
        className="h-screen w-screen outline-none"
        onMouseDown={() => canvasRef.current?.focus()}
      />

      {loading && (
        <div className="absolute inset-0 flex flex-col items-center justify-center gap-6 bg-paper">
          <div className="text-[0.8125rem] tracking-[0.2em] text-accent">
            SOLDIER : A DAY
          </div>
          <div className="h-1 w-80 bg-paper-3">
            <div
              className="h-full bg-accent transition-[width] duration-150"
              style={{ width: `${Math.round(progress * 100)}%` }}
            />
          </div>
          <div className="text-[1.0625rem] text-ink-2">
            번들 내려받는 중 {Math.round(progress * 100)}%
          </div>
        </div>
      )}

      {failed && (
        <div className="absolute inset-0 flex flex-col items-center justify-center gap-4 bg-paper p-14 text-center">
          <div className="text-[1.375rem] font-bold text-alert">게임을 못 띄웠다</div>
          <p className="max-w-xl text-[1.0625rem] text-ink-2">{failed}</p>
          <Link
            href="/lobby"
            className="mt-2 border border-rule px-6 py-3 text-[1.0625rem] text-ink hover:border-accent"
          >
            로비로
          </Link>
        </div>
      )}
    </main>
  );
}
