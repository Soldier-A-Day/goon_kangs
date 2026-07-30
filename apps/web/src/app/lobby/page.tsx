"use client";

import { useRouter, useSearchParams } from "next/navigation";
import { Suspense, useState } from "react";
import { ROLE_LABELS, createRoom, joinRoom, type Role } from "@/lib/api";
import { saveSession } from "@/lib/session";

const ROLES = Object.keys(ROLE_LABELS) as Role[];

function LobbyForm() {
  const router = useRouter();
  const params = useSearchParams();
  const [mode, setMode] = useState<"create" | "join">(
    params.get("mode") === "join" ? "join" : "create",
  );

  const [name, setName] = useState("");
  const [role, setRole] = useState<Role>("rifle");
  const [code, setCode] = useState("");
  const [difficulty, setDifficulty] = useState<"regular" | "relaxed">("regular");
  const [season, setSeason] = useState<"cold" | "hot" | "random">("random");
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  async function submit(event: React.FormEvent) {
    event.preventDefault();
    setError(null);
    setBusy(true);
    try {
      const session =
        mode === "create"
          ? await createRoom({ name, role, difficulty, season })
          : await joinRoom(code, { name, role });
      saveSession(session);
      router.push(`/room/${session.code}`);
    } catch (cause) {
      setError(explain(cause));
      setBusy(false);
    }
  }

  return (
    <main className="mx-auto flex w-full max-w-2xl flex-1 flex-col gap-8 px-6 py-14">
      <header className="flex flex-col gap-2 border-t-2 border-ink pt-3">
        <span className="label">편성</span>
        <h1 className="text-3xl font-extrabold tracking-tight">분대 편성</h1>
        <p className="text-sm text-ink-2">
          보직당 정확히 1명이며 중복이 없다. 4인을 권장하고, 비는 보직은 NPC 대리가 채운다 —
          대리는 필수 일과만 수행한다.
        </p>
      </header>

      <div className="flex gap-px border border-rule bg-rule">
        {(["create", "join"] as const).map((value) => (
          <button
            key={value}
            type="button"
            onClick={() => setMode(value)}
            className={`flex-1 px-4 py-2 text-sm font-bold ${
              mode === value ? "bg-ink text-paper" : "bg-paper text-ink-2"
            }`}
          >
            {value === "create" ? "새 분대 만들기" : "초대 코드로 입장"}
          </button>
        ))}
      </div>

      <form onSubmit={submit} className="flex flex-col gap-6">
        <label className="flex flex-col gap-2">
          <span className="label">이름</span>
          <input
            value={name}
            onChange={(event) => setName(event.target.value)}
            maxLength={12}
            required
            placeholder="김이병"
            className="border border-rule bg-paper-3 px-3 py-2 outline-none focus:border-accent"
          />
        </label>

        {mode === "join" && (
          <label className="flex flex-col gap-2">
            <span className="label">초대 코드</span>
            <input
              value={code}
              onChange={(event) => setCode(event.target.value.toUpperCase())}
              maxLength={6}
              required
              placeholder="ABC123"
              className="border border-rule bg-paper-3 px-3 py-2 font-mono tracking-[0.3em] outline-none focus:border-accent"
            />
          </label>
        )}

        <fieldset className="flex flex-col gap-2">
          <span className="label">보직</span>
          <div className="grid gap-px border border-rule bg-rule sm:grid-cols-2">
            {ROLES.map((value) => {
              const meta = ROLE_LABELS[value];
              const selected = role === value;
              return (
                <button
                  key={value}
                  type="button"
                  onClick={() => setRole(value)}
                  className={`flex flex-col gap-1 px-4 py-3 text-left ${
                    selected ? "bg-ink text-paper" : "bg-paper hover:bg-paper-2"
                  }`}
                >
                  <span className="flex items-baseline gap-2">
                    <b className="text-sm font-bold">{meta.name}</b>
                    <span
                      className={`label ${selected ? "text-paper-2" : ""}`}
                      style={selected ? { color: "var(--paper-2)" } : undefined}
                    >
                      {meta.code}
                    </span>
                  </span>
                  <span className={`text-xs ${selected ? "" : "text-ink-2"}`}>
                    {meta.duty}
                  </span>
                </button>
              );
            })}
          </div>
        </fieldset>

        {mode === "create" && (
          <div className="grid gap-4 sm:grid-cols-2">
            <label className="flex flex-col gap-2">
              <span className="label">난이도</span>
              <select
                value={difficulty}
                onChange={(event) =>
                  setDifficulty(event.target.value as "regular" | "relaxed")
                }
                className="border border-rule bg-paper-3 px-3 py-2 outline-none focus:border-accent"
              >
                <option value="regular">정규 — 1회 미달 즉시 종료</option>
                <option value="relaxed">완화 — 3회 누적 시 종료</option>
              </select>
            </label>
            <label className="flex flex-col gap-2">
              <span className="label">계절</span>
              <select
                value={season}
                onChange={(event) =>
                  setSeason(event.target.value as "cold" | "hot" | "random")
                }
                className="border border-rule bg-paper-3 px-3 py-2 outline-none focus:border-accent"
              >
                <option value="random">랜덤</option>
                <option value="cold">혹한기</option>
                <option value="hot">혹서기</option>
              </select>
            </label>
          </div>
        )}

        {error && (
          <p className="border-l-[3px] border-alert bg-paper-3 px-4 py-3 text-sm text-alert">
            {error}
          </p>
        )}

        <button
          type="submit"
          disabled={busy}
          className="border-2 border-ink bg-ink px-6 py-3 font-bold text-paper transition-opacity hover:opacity-80 disabled:opacity-40"
        >
          {busy ? "처리 중…" : mode === "create" ? "분대 만들기" : "입장"}
        </button>
      </form>
    </main>
  );
}

function explain(cause: unknown): string {
  const message = cause instanceof Error ? cause.message : String(cause);
  const table: Record<string, string> = {
    roleTaken: "그 보직은 이미 찼다. 다른 보직을 골라라.",
    roomStarted: "이미 시작한 분대다.",
    roomNotFound: "그런 초대 코드가 없다.",
    invalidBody: "입력이 올바르지 않다.",
  };
  if (table[message]) return table[message];
  if (message.includes("fetch")) {
    return "게임 서버에 붙지 못했다. `npm run dev:server` 가 떠 있는지 확인하라.";
  }
  return message;
}

export default function LobbyPage() {
  return (
    <Suspense fallback={<main className="p-14 text-ink-2">불러오는 중…</main>}>
      <LobbyForm />
    </Suspense>
  );
}
