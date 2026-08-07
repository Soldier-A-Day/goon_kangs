"use client";

import { useParams, useRouter } from "next/navigation";
import { useEffect, useState } from "react";
import type { LobbyState } from "@sad/protocol";
import { ROLE_LABELS, fetchLobby, joinRoom, type Role } from "@/lib/api";
import { useClientValue } from "@/lib/client-value";
import { loadName, saveName } from "@/lib/name";
import { saveSession } from "@/lib/session";

const ROLE_ORDER = Object.keys(ROLE_LABELS) as Role[];

/** 빈 보직 중 하나를 고른다. 순서는 카드가 뜨는 순서와 같다(§3.0). */
function pickOpenRole(lobby: LobbyState, candidates: Role[] = ROLE_ORDER): Role | null {
  const open = candidates.filter((role) => {
    const seat = lobby.seats.find((s) => s.role === role);
    return seat && !seat.memberId;
  });
  return open[0] ?? null;
}

/**
 * 초대 링크 착지 화면 (H-1).
 *
 * 확산 조사가 잰 초대받는 사람의 7단계 — 코드를 눈으로 읽어 타이핑 → 사이트 →
 * 탭 전환 → 이름 → 보직 → 코드 입력 → 대기 — 를 **링크 탭 → 이름 → 입장** 3단계로
 * 줄이는 자리다. 코드는 URL에 실려 오니 안 쳐도 되고, 보직은 자동으로 배정되니
 * 안 골라도 된다. 남는 결정은 이름 하나뿐이고, 그마저도 재방문이면 채워져 있다.
 */
export default function JoinPage() {
  const params = useParams<{ code: string }>();
  const router = useRouter();
  const code = (params.code ?? "").toUpperCase();

  // 저장된 이름은 브라우저에만 있다 — 서버에서는 빈 문자열로 그리고
  // 하이드레이션 직후 채운다. 사용자가 치기 시작하면 `typed`가 덮는다
  const storedName = useClientValue(loadName, "");
  const [typed, setTyped] = useState<string | null>(null);
  const name = typed ?? storedName;
  const setName = setTyped;

  const [lobby, setLobby] = useState<LobbyState | null>(null);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [submitError, setSubmitError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    let alive = true;
    (async () => {
      try {
        const state = await fetchLobby(code);
        if (alive) setLobby(state);
      } catch (cause) {
        if (alive) setLoadError(explain(cause));
      }
    })();
    return () => {
      alive = false;
    };
  }, [code]);

  useEffect(() => {
    if (lobby?.started) router.replace(`/play/${code}`);
  }, [code, lobby?.started, router]);

  async function submit(event: React.FormEvent) {
    event.preventDefault();
    setSubmitError(null);
    setBusy(true);
    try {
      // 제출 직전에 다시 받아 최신 빈 자리로 배정한다 — 폴링 사이에 남이 먼저
      // 앉았을 수 있다.
      const fresh = await fetchLobby(code);
      const remaining = [...ROLE_ORDER];
      let lastError: unknown = null;
      while (remaining.length) {
        const role = pickOpenRole(fresh, remaining);
        if (!role) break;
        remaining.splice(remaining.indexOf(role), 1);
        try {
          const session = await joinRoom(code, { name, role });
          saveName(name);
          saveSession(session);
          router.push(`/room/${code}`);
          return;
        } catch (cause) {
          lastError = cause;
          if (!(cause instanceof Error) || cause.message !== "roleTaken") throw cause;
          // 그 사이 다른 사람이 앉았다 — 남은 자리로 재시도
        }
      }
      throw lastError ?? new Error("roomFull");
    } catch (cause) {
      setSubmitError(explain(cause));
    } finally {
      setBusy(false);
    }
  }

  const filled = lobby?.seats.filter((seat) => seat.memberId).length ?? 0;
  const assignedRole = lobby ? pickOpenRole(lobby) : null;

  return (
    <main className="mx-auto flex w-full max-w-lg flex-1 flex-col justify-center gap-8 px-6 py-14">
      <header className="flex flex-col gap-2 border-t-2 border-ink pt-3">
        <span className="label" style={{ color: "var(--accent)" }}>
          초대받음
        </span>
        <h1 className="text-3xl font-extrabold tracking-tight">
          SOLDIER<span style={{ color: "var(--accent)" }}> : </span>A DAY
        </h1>
        <p className="font-mono text-2xl font-extrabold tracking-[0.3em]">{code}</p>
      </header>

      {loadError ? (
        <div
          className="flex flex-col gap-3 px-6 py-5"
          style={{ background: "var(--alert-bg)", borderLeft: "5px solid var(--alert)" }}
        >
          <p className="text-base">{loadError}</p>
          <a href="/lobby" className="text-sm font-bold underline" style={{ color: "var(--accent)" }}>
            로비로 가기
          </a>
        </div>
      ) : (
        <form onSubmit={submit} className="flex flex-col gap-5">
          <p className="text-sm text-ink-2">
            {lobby ? `현재 ${filled}명 참여 중 · 권장 인원 4명` : "방 정보를 확인하는 중…"}
          </p>

          <label className="flex flex-col gap-2 border border-rule bg-paper-3 px-6 py-4">
            <span className="text-sm text-ink-2">이름</span>
            <input
              autoFocus
              value={name}
              onChange={(event) => setName(event.target.value)}
              maxLength={12}
              required
              placeholder="김이병"
              className="h-[46px] border border-rule bg-void px-3 text-lg outline-none focus:border-accent"
            />
          </label>

          {assignedRole && (
            <p className="text-sm text-ink-2">
              보직 자동 배정 — <b style={{ color: "var(--accent)" }}>{ROLE_LABELS[assignedRole].name}</b>
              {" "}(대기실에서 확인)
            </p>
          )}

          {submitError && (
            <p
              className="px-5 py-4 text-sm"
              style={{
                background: "var(--alert-bg)",
                borderLeft: "5px solid var(--alert)",
                color: "var(--ink)",
              }}
            >
              {submitError}
            </p>
          )}

          <button
            type="submit"
            disabled={busy || !name || !lobby}
            className="flex h-[56px] items-center justify-center text-lg font-extrabold disabled:opacity-40"
            style={{ background: "var(--accent)", color: "var(--paper)" }}
          >
            {busy ? "입장하는 중…" : "입장"}
          </button>

          <a href="/lobby" className="text-center text-sm text-ink-2 underline">
            코드를 직접 입력하려면 로비로
          </a>
        </form>
      )}
    </main>
  );
}

function explain(cause: unknown): string {
  const message = cause instanceof Error ? cause.message : String(cause);
  const table: Record<string, string> = {
    roleTaken: "보직 배정에 실패했다. 다시 시도하라.",
    roomStarted: "이미 시작한 분대다.",
    roomNotFound: "그런 초대 코드가 없다. 링크를 다시 확인하라.",
    roomFull: "방이 가득 찼다.",
    invalidBody: "입력이 올바르지 않다.",
  };
  if (table[message]) return table[message];
  if (message.includes("fetch")) {
    return "게임 서버에 붙지 못했다. 잠시 후 다시 시도하라.";
  }
  return message;
}
