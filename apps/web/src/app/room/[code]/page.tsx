"use client";

import Link from "next/link";
import { useParams, useRouter } from "next/navigation";
import { useCallback, useEffect, useState } from "react";
import type { LobbyState, Session } from "@sad/protocol";
import { ROLE_LABELS, fetchLobby, startRun, type Role } from "@/lib/api";
import { clearSession, loadSession } from "@/lib/session";

export default function RoomPage() {
  const params = useParams<{ code: string }>();
  const router = useRouter();
  const code = (params.code ?? "").toUpperCase();

  const [session, setSession] = useState<Session | null>(null);
  const [lobby, setLobby] = useState<LobbyState | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    const stored = loadSession();
    if (!stored || stored.code !== code) {
      router.replace(`/lobby?mode=join`);
      return;
    }
    setSession(stored);
  }, [code, router]);

  const refresh = useCallback(async () => {
    try {
      setLobby(await fetchLobby(code));
      setError(null);
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : String(cause));
    }
  }, [code]);

  // 로비는 폴링으로 충분하다 — WS는 게임 화면의 것이고, 여기서는 초를 다투지 않는다
  useEffect(() => {
    if (!session) return;
    void refresh();
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
