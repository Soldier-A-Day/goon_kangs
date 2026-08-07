"use client";

import Link from "next/link";
import { useParams, useRouter } from "next/navigation";
import { useCallback, useEffect, useState } from "react";
import type { LobbyState } from "@sad/protocol";
import { ROLE_LABELS, fetchLobby, startRun, type Role } from "@/lib/api";
import { useClientValue } from "@/lib/client-value";
import { clearSession, loadSession } from "@/lib/session";

/** `useClientValue`에 넘길 읽기 함수들 — 모듈 상수라 렌더마다 새로 안 만들어진다 */
const alwaysTrue = () => true;
const supportsShare = () => typeof navigator !== "undefined" && "share" in navigator;

export default function RoomPage() {
  const params = useParams<{ code: string }>();
  const router = useRouter();
  const code = (params.code ?? "").toUpperCase();

  // 세션도 공유 지원 여부도 브라우저에만 있는 값이다 — 서버 렌더에서는
  // 각각 `null`·`false`로 그리고, 하이드레이션 직후 실제 값으로 넘어간다.
  // `hydrated`가 그 경계다: 이게 `false`인 동안의 `session === null`은
  // "저장된 세션이 없다"가 아니라 **"아직 못 읽었다"**라서, 이걸 구별하지
  // 않으면 아래 리다이렉트가 매번 로비로 튕겨 보낸다
  const hydrated = useClientValue(alwaysTrue, false);
  const session = useClientValue(loadSession, null);
  const canShare = useClientValue(supportsShare, false);

  const [lobby, setLobby] = useState<LobbyState | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [copied, setCopied] = useState(false);

  const inviteUrl =
    typeof window !== "undefined" ? `${window.location.origin}/join/${code}` : "";

  async function copyInvite() {
    try {
      await navigator.clipboard.writeText(inviteUrl);
      setCopied(true);
      setTimeout(() => setCopied(false), 2000);
    } catch {
      setError("클립보드 복사에 실패했다. 링크를 직접 선택해 복사하라.");
    }
  }

  async function shareInvite() {
    try {
      await navigator.share({
        title: "SOLDIER : A DAY",
        text: `${code} 코드로 분대에 합류하라`,
        url: inviteUrl,
      });
    } catch {
      // 사용자가 공유를 취소한 경우도 여기로 온다 — 조용히 무시한다
    }
  }

  // 이 방의 세션이 아니면 로비로 돌려보낸다. 리다이렉트는 진짜 부수효과라
  // 이펙트가 맞다 — 상태를 채우는 일만 위 `useClientValue`로 옮겼다
  useEffect(() => {
    if (!hydrated) return;
    if (!session || session.code !== code) router.replace(`/lobby?mode=join`);
  }, [hydrated, session, code, router]);

  const refresh = useCallback(async () => {
    try {
      setLobby(await fetchLobby(code));
      setError(null);
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : String(cause));
    }
  }, [code]);

  // 로비는 폴링으로 충분하다 — WS는 게임 화면의 것이고, 여기서는 초를 다투지 않는다.
  //
  // 첫 조회를 `void refresh()`로 바로 부르지 않고 마이크로태스크로 미룬다.
  // 화면에 보이는 차이는 없지만(같은 프레임 안에서 실행된다) 이펙트 **본문**이
  // 상태 갱신 경로를 직접 부르지 않게 되어, 2초 간격 호출과 첫 호출이
  // 똑같이 "콜백에서 갱신한다"는 한 가지 모양이 된다.
  useEffect(() => {
    if (!session) return;
    queueMicrotask(refresh);
    const timer = setInterval(refresh, 2000);
    return () => clearInterval(timer);
  }, [refresh, session]);

  useEffect(() => {
    if (lobby?.started) router.push(`/play/${code}`);
  }, [code, lobby?.started, router]);

  if (!session) return <main className="p-14 text-ink-2">세션을 확인하는 중…</main>;

  const isHost = lobby?.hostId === session.memberId;
  const filled = lobby?.seats.filter((seat) => seat.memberId).length ?? 0;

  return (
    <main className="mx-auto flex w-full max-w-3xl flex-1 flex-col gap-8 px-6 py-14">
      <header className="flex flex-col gap-3 border-t-2 border-ink pt-3">
        <span className="label">대기실</span>
        <div className="flex flex-wrap items-end justify-between gap-4">
          <h1 className="font-mono text-4xl font-extrabold tracking-[0.2em]">{code}</h1>
          <p className="text-sm text-ink-2">
            초대 코드를 분대원에게 넘겨라 · 권장 인원 4명 (현재 {filled}명)
          </p>
        </div>
      </header>

      <div className="flex flex-wrap items-center gap-3 border border-rule bg-paper-3 px-5 py-4">
        <span className="flex-1 truncate font-mono text-sm text-ink-2">{inviteUrl}</span>
        <button
          type="button"
          onClick={copyInvite}
          className="border-2 border-ink bg-ink px-5 py-2.5 text-sm font-bold text-paper transition-opacity hover:opacity-80"
        >
          {copied ? "복사됨" : "초대 링크 복사"}
        </button>
        {canShare && (
          <button
            type="button"
            onClick={shareInvite}
            className="border-2 border-accent px-5 py-2.5 text-sm font-bold text-accent transition-opacity hover:opacity-80"
          >
            공유
          </button>
        )}
      </div>

      <ul className="grid gap-px border border-rule bg-rule">
        {(lobby?.seats ?? []).map((seat) => {
          const meta = ROLE_LABELS[seat.role as Role];
          const me = seat.memberId === session.memberId;
          return (
            <li
              key={seat.role}
              className="flex items-center justify-between gap-4 bg-paper px-4 py-3"
            >
              <div className="flex flex-col gap-1">
                <span className="flex items-baseline gap-2">
                  <b className="font-bold">{meta.name}</b>
                  <span className="label">{meta.code}</span>
                </span>
                <span className="text-xs text-ink-2">{meta.duty}</span>
              </div>
              <div className="text-right">
                {seat.memberId ? (
                  <span className="text-sm font-bold">
                    {seat.name}
                    {me && <span className="text-accent"> (나)</span>}
                  </span>
                ) : (
                  <span className="text-sm text-ink-2">
                    NPC 대리 — 필수 일과만 수행
                  </span>
                )}
              </div>
            </li>
          );
        })}
      </ul>

      <p className="border-l-[3px] border-accent bg-paper-3 px-4 py-3 text-sm text-ink-2">
        빈 보직은 NPC 대리가 채우지만 선택·돌발·히든 퀘스트를 하지 않는다. 처음부터 비어 있던
        자리에는 군기 페널티가 붙지 않는다.
      </p>

      {error && (
        <p className="border-l-[3px] border-alert bg-paper-3 px-4 py-3 text-sm text-alert">
          {error}
        </p>
      )}

      <div className="flex flex-wrap items-center gap-3">
        {isHost ? (
          <button
            type="button"
            onClick={async () => {
              try {
                await startRun(code, session.token);
                router.push(`/play/${code}`);
              } catch (cause) {
                setError(cause instanceof Error ? cause.message : String(cause));
              }
            }}
            className="border-2 border-ink bg-ink px-6 py-3 font-bold text-paper transition-opacity hover:opacity-80"
          >
            입소 — 1일차 시작
          </button>
        ) : (
          <span className="text-sm text-ink-2">방장이 시작할 때까지 대기한다.</span>
        )}

        <Link
          href="/lobby"
          onClick={clearSession}
          className="border-2 border-rule px-6 py-3 text-sm font-bold text-ink-2 hover:bg-paper-2"
        >
          나가기
        </Link>
      </div>
    </main>
  );
}
