"use client";

import { useEffect, useRef } from "react";
import type { Snapshot } from "@sad/protocol";
import type { TimedEvent } from "@/lib/useGameSocket";
import { ITEM_LABELS, PHASE_LABELS, QUICK_COMMANDS, RANK_LABELS } from "@/lib/labels";

/**
 * 방송·무전·판정이 한 줄씩 흐르는 로그.
 * JDG-02의 "실패 원인을 단 한 줄로 지목한다"가 여기서 실제로 읽히게 만든다.
 */
export function EventLog({
  events,
  snapshot,
}: {
  events: TimedEvent[];
  snapshot: Snapshot;
}) {
  const endRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    endRef.current?.scrollIntoView({ block: "end" });
  }, [events.length]);

  const nameOf = (id: string) =>
    snapshot.members.find((m) => m.id === id)?.name ?? "누군가";

  return (
    <section className="flex min-h-0 flex-col gap-2 border border-rule bg-paper-3 p-3">
      <span className="label">상황</span>
      <ul className="flex min-h-0 flex-col gap-1 overflow-y-auto text-sm">
        {events.map(({ key, event }) => {
          const rendered = describe(event, nameOf);
          if (!rendered) return null;
          return (
            <li key={key} style={{ color: rendered.tone }}>
              {rendered.text}
            </li>
          );
        })}
        <div ref={endRef} />
      </ul>
    </section>
  );
}

function describe(
  event: TimedEvent["event"],
  nameOf: (id: string) => string,
): { text: string; tone?: string } | null {
  switch (event.type) {
    case "phaseStarted":
      return {
        text: `▸ D-${event.day} ${PHASE_LABELS[event.phase]} 시작`,
        tone: "var(--accent)",
      };
    case "phaseEnded":
      return event.lockedCount > 0
        ? {
            text: `${PHASE_LABELS[event.phase]} 종료 — ${event.lockedCount}건 잠김`,
            tone: "var(--alert)",
          }
        : { text: `${PHASE_LABELS[event.phase]} 종료 — 잠긴 일과 없음` };
    case "weatherRolled":
      return { text: `오늘 기온: ${event.label}` };
    case "surpriseRaised":
      return { text: `돌발 — ${event.label}`, tone: "var(--heat)" };
    case "dayJudged":
      return event.passed
        ? { text: "점호 통과 — 취침", tone: "var(--accent)" }
        : {
            text: `점호 실패 — 조건 ${event.failedAt}에서 무너졌다`,
            tone: "var(--alert)",
          };
    case "disciplineChanged":
      return { text: `군기 ${event.to} (${event.band})` };
    case "memberEvacuated":
      return {
        text: `${nameOf(event.memberId)} 후송 — ${
          event.absorbed ? "대리가 잔여 필수를 인수했다" : "잔여 필수를 인수하지 못했다"
        }`,
        tone: "var(--alert)",
      };
    case "memberReturned":
      return {
        text: event.asRecruit
          ? `${nameOf(event.memberId)} 복귀 신병으로 재투입 — 계급과 점수를 잃었다`
          : `${nameOf(event.memberId)} 재접속`,
      };
    case "memberLeft":
      return { text: `${nameOf(event.memberId)} 이탈 — NPC 대리로 전환`, tone: "var(--alert)" };
    case "forcedSleep":
      return { text: `${nameOf(event.memberId)} 피로 한계 — 강제 수면`, tone: "var(--alert)" };
    case "sleepSettled":
      return {
        text: `야간 경계: ${event.guardIds.map(nameOf).join(" · ") || "없음"} (회복 절반)`,
      };
    case "choreDelegated":
      return { text: `${nameOf(event.fromId)} → ${nameOf(event.toId)} 하달` };
    case "choreVetoed":
      return { text: `${nameOf(event.memberId)} 하달 거부` };
    case "choreReassigned":
      return { text: `분대장이 ${nameOf(event.toId)}에게 재배정` };
    case "quickCommand": {
      const label = QUICK_COMMANDS.find((c) => c.id === event.command)?.label ?? "";
      return { text: `${nameOf(event.memberId)}: ${label}`, tone: "var(--accent)" };
    }
    case "chat":
      return {
        text: `${event.radio ? "[무전] " : ""}${nameOf(event.memberId)}: ${event.text}`,
      };
    case "runEnded":
      return {
        text:
          event.status === "cleared"
            ? "전역 — 18일을 버텼다"
            : event.status === "disbanded"
              ? "분대 해체"
              : "퇴소",
        tone: event.status === "cleared" ? "var(--accent)" : "var(--alert)",
      };
    case "rankReviewed": {
      // 점수 내역을 전원에게 보여준다 — 누가 필수만 하고 버텼는지가 드러나는 것이
      // 이 시스템의 사회적 압력 장치다 (13.1 공개 원칙)
      const lines = event.outcomes
        .map(
          (outcome) =>
            `${nameOf(outcome.memberId)} ${outcome.score}/${outcome.require} ` +
            (outcome.promoted ? `→ ${RANK_LABELS[outcome.to]}` : "보류"),
        )
        .join(" · ");
      const promoted = event.outcomes.some((outcome) => outcome.promoted);
      return {
        text: `${event.isRetry ? "재심사" : "승급 심사"} — ${lines}`,
        tone: promoted ? "var(--accent)" : "var(--ink-2)",
      };
    }
    case "supplyClaimed":
      return {
        text:
          event.items.length === 0
            ? `보급일 — 포인트가 모자라 아무것도 받지 못했다 (잔여 ${event.pointsLeft})`
            : `보급 도착 — ${event.items
                .map((id) => ITEM_LABELS[id] ?? id)
                .join(" · ")} (잔여 ${event.pointsLeft})`,
        tone: event.items.length === 0 ? "var(--alert)" : "var(--accent)",
      };
    case "log":
      return { text: event.message };
    default:
      return null;
  }
}
