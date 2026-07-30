"use client";

import Link from "next/link";
import { useParams, useRouter } from "next/navigation";
import { useEffect, useState } from "react";
import type { Session } from "@sad/protocol";
import { ConditionRings } from "@/components/hud/ConditionRings";
import { DelegationWindow } from "@/components/hud/DelegationWindow";
import { EventLog } from "@/components/hud/EventLog";
import { Notebook } from "@/components/hud/Notebook";
import { PhaseBar } from "@/components/hud/PhaseBar";
import { QuickBar } from "@/components/hud/QuickBar";
import { SupplyPanel } from "@/components/hud/SupplyPanel";
import { ZoneMap } from "@/components/hud/ZoneMap";
import { RANK_LABELS, ZONE_LABELS } from "@/lib/labels";
import { loadSession } from "@/lib/session";
import { useGameSocket, useSmoothClock } from "@/lib/useGameSocket";

/**
 * DOM 디버그 클라이언트.
 *
 * Unity 자리에 들어가는 임시 클라이언트지만 버릴 코드가 아니다 — 15.0 HUD가 여기서 만들어지고,
 * Unity는 3D 뷰포트만 가져간다. M0 질문에 답하는 것이 목적이다:
 * 이동 + 붙잡기 + 판정만 있는 상태에서 하루 루프가 긴장을 만드는가?
 */
export default function PlayPage() {
  const params = useParams<{ code: string }>();
  const router = useRouter();
  const code = (params.code ?? "").toUpperCase();

  const [session, setSession] = useState<Session | null>(null);
  const [working, setWorking] = useState<string | null>(null);
  const [skipVoted, setSkipVoted] = useState(false);

  useEffect(() => {
    const stored = loadSession();
    if (!stored || stored.code !== code) {
      router.replace("/lobby?mode=join");
      return;
    }
    setSession(stored);
  }, [code, router]);

  const { status, snapshot, events, send, memberId } = useGameSocket(session?.token ?? null);
  const elapsed = useSmoothClock(snapshot);

  const myId = memberId ?? session?.memberId ?? null;

  // 시간대가 바뀌면 붙잡고 있던 것과 투표를 놓는다 — 서버도 칸 경계에서 잠근다
  useEffect(() => {
    setWorking(null);
    setSkipVoted(false);
  }, [snapshot?.phase.index, snapshot?.day]);

  function toggleWork(questId: string | null) {
    if (working && working !== questId) {
      send({ type: "interact", questId: working, active: false });
    }
    setWorking(questId);
    if (questId) send({ type: "interact", questId, active: true });
  }

  if (!session || !snapshot || !myId) {
    return (
      <main className="flex flex-1 items-center justify-center p-14 text-ink-2">
        {status === "closed" || status === "error"
          ? "서버와 연결이 끊겼다."
          : "부대로 이동 중…"}
      </main>
    );
  }

  const me = snapshot.members.find((m) => m.id === myId);
  if (!me) {
    return <main className="p-14 text-ink-2">분대에서 자리를 찾지 못했다.</main>;
  }

  if (snapshot.status !== "running") {
    return <RunEnded snapshot={snapshot} code={code} />;
  }

  const delegating = snapshot.phase.delegationWindowMsLeft > 0;

  return (
    <div className="flex min-h-screen flex-col">
      <PhaseBar snapshot={snapshot} elapsedMs={elapsed} />

      <main className="mx-auto grid w-full max-w-6xl flex-1 gap-4 px-4 py-4 lg:grid-cols-[1fr_22rem]">
        <div className="flex min-w-0 flex-col gap-4">
          {delegating && (
            <DelegationWindow snapshot={snapshot} memberId={myId} onSend={send} />
          )}

          <ZoneMap
            snapshot={snapshot}
            memberId={myId}
            workingQuestId={working}
            onSend={send}
            onToggleWork={toggleWork}
          />

          <QuickBar
            snapshot={snapshot}
            onSend={send}
            skipVoted={skipVoted}
            onToggleSkip={() => {
              const next = !skipVoted;
              setSkipVoted(next);
              send({ type: "voteSkip", value: next });
            }}
          />

          <SupplyPanel snapshot={snapshot} memberId={myId} onSend={send} />

          <Squad snapshot={snapshot} myId={myId} />
        </div>

        <aside className="flex min-h-0 flex-col gap-4">
          <ConditionRings member={me} />
          <Notebook
            snapshot={snapshot}
            memberId={myId}
            onFocus={(quest) => {
              if (quest.zone !== me.zone) send({ type: "move", to: quest.zone });
            }}
          />
          <EventLog events={events} snapshot={snapshot} />
        </aside>
      </main>
    </div>
  );
}

function Squad({
  snapshot,
  myId,
}: {
  snapshot: ReturnType<typeof useGameSocket>["snapshot"] & object;
  myId: string;
}) {
  return (
    <section className="flex flex-col gap-2 border border-rule bg-paper-3 p-3">
      <span className="label">분대</span>
      <ul className="grid gap-px bg-rule sm:grid-cols-2">
        {snapshot.members.map((member) => {
          const proxy = member.presence !== "player";
          const remaining = snapshot.quests.filter(
            (q) => q.ownerId === member.id && q.required && q.status !== "done",
          ).length;

          return (
            <li
              key={member.id}
              className="flex items-center justify-between gap-2 bg-paper px-3 py-2"
            >
              <span className="flex min-w-0 flex-col">
                <span className="flex items-baseline gap-2">
                  <b className="truncate text-sm font-bold">{member.name}</b>
                  <span className="label">{RANK_LABELS[member.rank]}</span>
                  {member.id === myId && <span className="label text-accent">나</span>}
                </span>
                <span className="text-[0.6875rem] text-ink-2">
                  {proxy ? "NPC 대리" : ZONE_LABELS[member.zone]}
                  {member.onGuardTonight && " · 오늘 경계"}
                </span>
              </span>
              <span
                className="font-mono text-sm font-bold tabular-nums"
                style={{ color: remaining === 0 ? "var(--accent)" : "var(--alert)" }}
              >
                {remaining}
              </span>
            </li>
          );
        })}
      </ul>
    </section>
  );
}

function RunEnded({
  snapshot,
  code,
}: {
  snapshot: NonNullable<ReturnType<typeof useGameSocket>["snapshot"]>;
  code: string;
}) {
  const cleared = snapshot.status === "cleared";
  const judgement = snapshot.lastJudgement;

  return (
    <main className="mx-auto flex w-full max-w-2xl flex-1 flex-col justify-center gap-6 px-6 py-14">
      <span className="label">{code}</span>
      <h1
        className="text-4xl font-extrabold tracking-tight"
        style={{ color: cleared ? "var(--accent)" : "var(--alert)" }}
      >
        {cleared ? "전역" : snapshot.status === "disbanded" ? "분대 해체" : "퇴소"}
      </h1>

      <p className="text-ink-2">
        {snapshot.day}일차까지 버텼다.
        {judgement && !judgement.passed && (
          <>
            {" "}
            마지막 점호는 <b className="text-ink">조건 {judgement.failedAt}</b>에서
            무너졌다 — 필수 {judgement.requiredDone}/{judgement.requiredTotal}.
          </>
        )}
      </p>

      <div className="flex flex-wrap gap-4">
        <Link
          href={`/ledger/run-${code}`}
          className="text-accent underline underline-offset-4"
        >
          하달 장부 보기
        </Link>
        <Link href="/records" className="text-accent underline underline-offset-4">
          분대 기록
        </Link>
        <Link href="/" className="text-ink-2 underline underline-offset-4">
          처음으로
        </Link>
      </div>
    </main>
  );
}
