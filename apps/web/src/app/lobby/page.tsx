"use client";

import { useRouter, useSearchParams } from "next/navigation";
import { Suspense, useEffect, useState } from "react";
import { RoleCard } from "@/components/RoleCard";
import { HTTP_BASE, ROLE_LABELS, createRoom, joinRoom, type Role } from "@/lib/api";
import { loadName, saveName } from "@/lib/name";
import { saveSession } from "@/lib/session";

const ROLES = Object.keys(ROLE_LABELS) as Role[];

/**
 * 분대 편성 (목업 §11 SCREEN 7.10-D).
 *
 * 목업이 로비를 **가로로 두 덩이**로 나눴다 — 왼쪽은 보직, 오른쪽은 런 설정.
 * 보직 선택이 이 화면의 결정이고 계절·난이도는 그 옆에 딸린 것이라, 세로로
 * 늘어놓으면 어느 쪽이 중요한지가 사라진다.
 *
 * 프리페치 막대는 §1.2가 "2D 전환으로 25MB"라고 적어둔 것의 값어치를 보여주는
 * 자리다. 편성하는 동안 번들이 내려오면 "분대 만들기"를 누르는 순간 게임이
 * 이미 와 있다.
 */
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

  // 재방문이면 이름을 다시 안 쳐도 되게 — H-1의 "이름 로컬 저장"
  useEffect(() => {
    setName((current) => current || loadName());
  }, []);

  // Render 무료 플랜은 15분 놀면 잠든다 — 로비에 들어오는 순간 한 번 찔러
  // 깨워두면, 편성을 마치고 "분대 만들기"를 누를 때는 이미 깨어나 있다.
  // fire-and-forget이다: 응답도 실패도 화면에 영향을 주지 않는다. 실제 방
  // 생성 요청(createRoom/joinRoom)이 여전히 최종 진실이고, 이건 그저 깨우기다.
  useEffect(() => {
    fetch(`${HTTP_BASE}/health`).catch(() => {});
  }, []);

  async function submit(event: React.FormEvent) {
    event.preventDefault();
    setError(null);
    setBusy(true);
    try {
      const session =
        mode === "create"
          ? await createRoom({ name, role, difficulty, season })
          : await joinRoom(code, { name, role });
      saveName(name);
      saveSession(session);
      router.push(`/room/${session.code}`);
    } catch (cause) {
      setError(explain(cause));
      setBusy(false);
    }
  }

  return (
    <form
      onSubmit={submit}
      className="mx-auto flex w-full max-w-[1500px] flex-1 flex-col px-8 py-6"
    >
      <p className="label">
        SCREEN 7.10-D — LOBBY · Next.js 웹 셸 · Unity 번들 백그라운드 프리페치
      </p>

      <header className="mt-4 flex flex-wrap items-end justify-between gap-6 border-b-2 border-ink bg-paper-2 px-8 py-6">
        <div className="flex flex-col gap-2">
          <span className="label" style={{ color: "var(--accent)" }}>
            SAD · 18-DAY CO-OP SURVIVAL
          </span>
          <h1 className="text-4xl font-extrabold tracking-tight">
            SOLDIER<span style={{ color: "var(--accent)" }}> : </span>A DAY
          </h1>
        </div>

        <div className="flex flex-col items-end gap-2">
          <span className="text-sm text-ink-2">
            {mode === "create" ? "만들면 코드가 나온다" : "초대 코드"}
          </span>
          {mode === "create" ? (
            <span
              className="flex h-[42px] w-[260px] items-center justify-center border font-mono text-2xl font-extrabold tracking-[0.4em]"
              style={{
                borderColor: "var(--rule)",
                background: "var(--void)",
                color: "var(--ink-2)",
              }}
            >
              ——————
            </span>
          ) : (
            <input
              value={code}
              onChange={(event) => setCode(event.target.value.toUpperCase())}
              maxLength={6}
              required
              placeholder="K7M2QX"
              aria-label="초대 코드"
              className="h-[42px] w-[260px] border bg-sunk text-center font-mono text-2xl font-extrabold tracking-[0.4em] outline-none"
              style={{ borderColor: "var(--accent)", color: "var(--accent)" }}
            />
          )}
        </div>
      </header>

      <div className="mt-8 flex flex-col gap-8 xl:flex-row">
        {/* ── 왼쪽: 보직 ── */}
        <section className="flex flex-1 flex-col gap-3">
          <span className="label">ROLE — 보직당 정확히 1명, 중복 불가</span>

          <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
            {ROLES.map((value) => (
              <RoleCard
                key={value}
                role={value}
                state={role === value ? "mine" : "open"}
                onPick={() => setRole(value)}
              />
            ))}
          </div>

          <div
            className="mt-2 flex flex-col gap-2 px-6 py-4"
            style={{
              background: "var(--alert-bg)",
              borderLeft: "5px solid var(--alert)",
            }}
          >
            <span className="label" style={{ color: "var(--alert)" }}>
              ROLE-03 인원 미달 처리
            </span>
            <p className="text-[1.0625rem]">
              빈 보직은 NPC 대리가 됩니다 — 필수 퀘스트만 수행하고 선택 · 돌발 · 히든은
              하지 않습니다.
            </p>
            <p className="text-[0.9375rem] text-ink-2">
              군기 −3/일은 면제되지만, 정보 비대칭 합동 패턴 일부가 막히고 보급
              포인트가 자연 감소합니다.
            </p>
          </div>
        </section>

        {/* ── 오른쪽: 런 설정 ── */}
        <section className="flex w-full flex-col gap-3 xl:w-[560px]">
          <span className="label">RUN SETUP</span>

          <div className="flex gap-px border border-rule bg-rule">
            {(["create", "join"] as const).map((value) => (
              <button
                key={value}
                type="button"
                onClick={() => setMode(value)}
                className={`flex-1 px-4 py-3 text-sm font-bold ${
                  mode === value
                    ? "bg-accent text-paper"
                    : "bg-paper-3 text-ink-2 hover:bg-paper-2"
                }`}
              >
                {value === "create" ? "새 분대 만들기" : "초대 코드로 입장"}
              </button>
            ))}
          </div>

          <label className="flex flex-col gap-2 border border-rule bg-paper-3 px-6 py-4">
            <span className="text-sm text-ink-2">이름</span>
            <input
              value={name}
              onChange={(event) => setName(event.target.value)}
              maxLength={12}
              required
              placeholder="김이병"
              className="h-[38px] border border-rule bg-void px-3 outline-none focus:border-accent"
            />
          </label>

          {mode === "create" && (
            <>
              <Choice
                title="계절"
                value={season}
                onPick={setSeason}
                options={[
                  { value: "cold", label: "혹한기", tint: "var(--cold)" },
                  { value: "hot", label: "혹서기", tint: "var(--heat)" },
                  { value: "random", label: "랜덤", tint: "var(--accent)" },
                ]}
              />
              <Choice
                title="난이도"
                value={difficulty}
                onPick={setDifficulty}
                options={[
                  {
                    value: "regular",
                    label: "정규 — 1회 미달 즉시 종료",
                    tint: "var(--accent)",
                  },
                  {
                    value: "relaxed",
                    label: "완화 — 3회 · 보상 60%",
                    tint: "var(--accent)",
                  },
                ]}
              />
            </>
          )}

          <Prefetch />

          {error && (
            <p
              className="px-5 py-4 text-sm"
              style={{
                background: "var(--alert-bg)",
                borderLeft: "5px solid var(--alert)",
                color: "var(--ink)",
              }}
            >
              {error}
            </p>
          )}
        </section>
      </div>

      {/* ── 아래: 요약 + 진입 ── */}
      <footer className="mt-8 flex flex-wrap items-center justify-between gap-6 border-t border-rule pt-6">
        <div className="flex flex-col gap-1">
          <p className="text-base text-ink-2">
            권장 인원{" "}
            <b className="font-extrabold" style={{ color: "var(--accent)" }}>
              4인
            </b>{" "}
            · 예상 소요 100~130분
          </p>
          <p className="text-sm" style={{ color: "#5a6053" }}>
            1~2인 방은 튜토리얼 · 연습 용도이며 기록에 남지 않습니다
          </p>
        </div>

        <div className="flex gap-4">
          <a
            href="/tutorial"
            className="flex h-[52px] w-[170px] items-center justify-center border text-base"
            style={{ borderColor: "var(--accent)", color: "var(--accent)" }}
          >
            튜토리얼
          </a>
          <a
            href="/settings"
            className="flex h-[52px] w-[170px] items-center justify-center border text-base"
            style={{ borderColor: "var(--ink-2)", color: "var(--ink-2)" }}
          >
            설정
          </a>
          <button
            type="submit"
            disabled={busy || !name}
            className="flex h-[52px] w-[190px] items-center justify-center text-[1.0625rem] font-extrabold disabled:opacity-40"
            style={{ background: "var(--accent)", color: "var(--paper)" }}
          >
            {busy ? "처리 중…" : mode === "create" ? "분대 만들기" : "입장"}
          </button>
        </div>
      </footer>
    </form>
  );
}

/** 목업의 가로 선택 줄 — 고른 것만 테두리가 밝다 */
function Choice<T extends string>({
  title,
  value,
  onPick,
  options,
}: {
  title: string;
  value: T;
  onPick: (value: T) => void;
  options: { value: T; label: string; tint: string }[];
}) {
  return (
    <fieldset className="flex flex-col gap-3 border border-rule bg-paper-3 px-6 py-4">
      <legend className="px-1 text-sm text-ink-2">{title}</legend>
      <div className="flex flex-wrap gap-3">
        {options.map((option) => {
          const on = value === option.value;
          return (
            <button
              key={option.value}
              type="button"
              onClick={() => onPick(option.value)}
              aria-pressed={on}
              className="h-[34px] flex-1 whitespace-nowrap px-4 text-[0.9375rem]"
              style={{
                background: on ? "var(--sunk)" : "var(--void)",
                border: `1px solid ${on ? option.tint : "var(--rule)"}`,
                color: on ? option.tint : "#5a6053",
                fontWeight: on ? 700 : 400,
              }}
            >
              {option.label}
            </button>
          );
        })}
      </div>
    </fieldset>
  );
}

/**
 * 번들 프리페치 (목업 §11 PREFETCH).
 *
 * 편성하는 동안 Unity 번들을 미리 받는다. **진짜로 받는다** — 막대만 움직이는
 * 가짜를 두면 게임에 들어갈 때 그만큼을 또 기다리게 되고, 그건 목업이 이 자리를
 * 만든 이유와 정반대다.
 *
 * 번들이 아직 없으면(개발 중) 통째로 감춘다. 0%에 멈춘 막대는 고장으로 읽힌다.
 */
function Prefetch() {
  const [percent, setPercent] = useState<number | null>(null);

  useEffect(() => {
    let alive = true;
    const controller = new AbortController();

    (async () => {
      try {
        const response = await fetch("/game/Build/web.data.br", {
          signal: controller.signal,
        });
        if (!response.ok || !response.body) return;

        const total = Number(response.headers.get("content-length") ?? 0);
        if (!total) return;

        const reader = response.body.getReader();
        let read = 0;
        for (;;) {
          const chunk = await reader.read();
          if (chunk.done) break;
          read += chunk.value.byteLength;
          if (!alive) return;
          setPercent(Math.min(100, Math.round((read / total) * 100)));
        }
      } catch {
        // 중단이거나 번들이 없다. 둘 다 막대를 안 그리는 것이 맞다
      }
    })();

    return () => {
      alive = false;
      controller.abort();
    };
  }, []);

  if (percent === null) return null;

  return (
    <div className="flex flex-col gap-3 border border-rule bg-paper-3 px-6 py-4">
      <span className="label" style={{ color: "var(--accent)" }}>
        PREFETCH — 대기 시간에 다운로드
      </span>
      <div className="flex items-baseline justify-between">
        <span className="text-[1.0625rem]">Unity 번들 내려받는 중</span>
        <span className="font-mono text-base" style={{ color: "var(--accent)" }}>
          {percent}%
        </span>
      </div>
      <div className="h-[14px]" style={{ background: "var(--rule-2)" }}>
        <div
          className="h-full transition-[width]"
          style={{ width: `${percent}%`, background: "var(--accent)" }}
        />
      </div>
      <span className="text-[0.8125rem]" style={{ color: "#5a6053" }}>
        2D 전환으로 25MB — 3D 안 120MB 대비 1/5
      </span>
    </div>
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
