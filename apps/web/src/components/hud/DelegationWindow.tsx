"use client";

import { useState } from "react";
import type { Intent, Snapshot } from "@sad/protocol";
import { RANK_LABELS, ZONE_LABELS, formatSeconds } from "@/lib/labels";

type Member = Snapshot["members"][number];
type Quest = Snapshot["quests"][number];

const RANK_ORDER = ["private", "pfc", "corporal", "sergeant"] as const;
const RECEIVE_LIMIT = 2;
const DISCIPLINE_COST = -2;

function rankIndex(member: Member): number {
  return RANK_ORDER.indexOf(member.rank);
}

/**
 * 15.0 하달 창.
 *
 * 좌: 내 공통 일과. 우: 나보다 계급이 낮은 분대원과 수락 여력 n/2.
 * 드래그로 배정하고, 하단에 예상 군기 변동이 실시간으로 뜬다.
 * 타이머는 이 동안 정지하므로 눈치싸움에 일과 시간을 쓰지 않는다.
 */
export function DelegationWindow({
  snapshot,
  memberId,
  onSend,
}: {
  snapshot: Snapshot;
  memberId: string;
  onSend: (intent: Intent) => void;
}) {
  const [dragging, setDragging] = useState<string | null>(null);
  const [hovered, setHovered] = useState<string | null>(null);
  const [assigned, setAssigned] = useState(0);

  const me = snapshot.members.find((m) => m.id === memberId);
  if (!me) return null;

  const isLeader = snapshot.leaderId === memberId;

  const chores = snapshot.quests.filter(
    (quest) =>
      quest.kind === "chore" && quest.ownerId === memberId && quest.delegatedFrom === null,
  );

  // 분대장이 되돌릴 수 있는 것 — 이미 누군가에게 넘어간 공통 일과
  const reassignable = snapshot.quests.filter(
    (quest) => quest.kind === "chore" && quest.delegatedFrom !== null,
  );

  // 계급이 1단계 이상 높은 사람만 하달할 수 있다. 이병은 최하위라 대상이 없다.
  const targets = snapshot.members.filter(
    (member) =>
      member.id !== memberId &&
      member.presence === "player" &&
      rankIndex(me) - rankIndex(member) >= 1,
  );

  const allowance = targets.length === 0
    ? 0
    : Math.max(...targets.map((t) => (rankIndex(me) - rankIndex(t) >= 2 ? 2 : 1)));
  const left = Math.max(0, allowance - assigned);

  function assign(quest: Quest, target: Member) {
    if (target.choresReceived >= RECEIVE_LIMIT) return;
    if (left <= 0) return;
    onSend({ type: "delegateChore", toId: target.id, questId: quest.id });
    setAssigned((count) => count + 1);
  }

  return (
    <section className="border-2 border-ink bg-paper-3">
      <div className="flex flex-wrap items-baseline justify-between gap-3 border-b border-rule bg-ink px-4 py-2 text-paper">
        <b className="text-sm font-bold">하달 — 공통 일과를 넘긴다</b>
        <span className="font-mono text-sm tabular-nums">
          {formatSeconds(snapshot.phase.delegationWindowMsLeft)}
        </span>
      </div>

      <div className="grid gap-px bg-rule sm:grid-cols-2">
        <div className="flex flex-col gap-2 bg-paper-3 p-3">
          <span className="label">내 공통 일과</span>
          {chores.length === 0 ? (
            <p className="text-sm text-ink-2">넘길 공통 일과가 없다.</p>
          ) : (
            <ul className="flex flex-col gap-2">
              {chores.map((quest) => (
                <li
                  key={quest.id}
                  draggable
                  onDragStart={() => setDragging(quest.id)}
                  onDragEnd={() => setDragging(null)}
                  className={`cursor-grab border px-3 py-2 ${
                    dragging === quest.id
                      ? "border-accent opacity-60"
                      : "border-rule-2 bg-paper"
                  }`}
                >
                  <span className="flex items-baseline justify-between gap-2">
                    <b className="text-sm font-bold">{quest.label}</b>
                    <span className="label">
                      {quest.required ? "필수" : "선택"} · {ZONE_LABELS[quest.zone]}
                    </span>
                  </span>
                </li>
              ))}
            </ul>
          )}
        </div>

        <div className="flex flex-col gap-2 bg-paper-3 p-3">
          <span className="label">하급자</span>
          {targets.length === 0 ? (
            <p className="text-sm text-ink-2">
              하달할 수 있는 하급자가 없다. 이병은 최하위라 넘길 수 없다.
            </p>
          ) : (
            <ul className="flex flex-col gap-2">
              {targets.map((target) => {
                const full = target.choresReceived >= RECEIVE_LIMIT;
                return (
                  <li
                    key={target.id}
                    onDragOver={(event) => {
                      event.preventDefault();
                      setHovered(target.id);
                    }}
                    onDragLeave={() => setHovered(null)}
                    onDrop={() => {
                      const quest = chores.find((q) => q.id === dragging);
                      if (quest) assign(quest, target);
                      setDragging(null);
                      setHovered(null);
                    }}
                    className={`border px-3 py-2 ${
                      hovered === target.id && !full
                        ? "border-accent bg-paper"
                        : "border-rule-2 bg-paper"
                    } ${full ? "opacity-50" : ""}`}
                  >
                    <span className="flex items-baseline justify-between gap-2">
                      <b className="text-sm font-bold">
                        {target.name}
                        <span className="label ml-2">{RANK_LABELS[target.rank]}</span>
                      </b>
                      <span className="label">
                        수락 여력 {RECEIVE_LIMIT - target.choresReceived}/{RECEIVE_LIMIT}
                      </span>
                    </span>
                  </li>
                );
              })}
            </ul>
          )}
        </div>
      </div>

      {isLeader && reassignable.length > 0 && (
        <div className="flex flex-col gap-2 border-t border-rule px-4 py-3">
          {/*
            RANK-02 — 분대장 개입은 계급과 무관하다. 이병 분대장이 병장의 하달을 되돌리는
            장면이 여기서 성립한다. 시간대당 1회뿐이라 언제 쓸지가 판단이 된다.
          */}
          <span className="label">분대장 개입 — 시간대당 1회</span>
          {reassignable.map((quest) => (
            <div key={quest.id} className="flex flex-wrap items-center gap-2 text-sm">
              <span className="flex-1">
                <b className="font-bold">{quest.label}</b>
                <span className="label ml-2">
                  {snapshot.members.find((m) => m.id === quest.delegatedFrom)?.name} →{" "}
                  {snapshot.members.find((m) => m.id === quest.ownerId)?.name}
                </span>
              </span>
              {snapshot.members
                .filter(
                  (target) =>
                    target.presence === "player" &&
                    target.id !== quest.ownerId &&
                    target.choresReceived < RECEIVE_LIMIT,
                )
                .map((target) => (
                  <button
                    key={target.id}
                    type="button"
                    onClick={() =>
                      onSend({
                        type: "leaderReassign",
                        questId: quest.id,
                        toId: target.id,
                      })
                    }
                    className="border border-rule px-2 py-1 text-xs hover:bg-paper-2"
                  >
                    {target.name}에게
                  </button>
                ))}
            </div>
          ))}
        </div>
      )}

      <div className="flex flex-wrap items-center justify-between gap-3 border-t border-rule px-4 py-2">
        <span className="label">남은 하달 {left}건</span>
        <span className="text-sm">
          예상 군기 변동{" "}
          <b
            className="font-mono font-bold tabular-nums"
            style={{ color: assigned > 0 ? "var(--alert)" : undefined }}
          >
            {assigned === 0 ? "0" : assigned * DISCIPLINE_COST}
          </b>
          <span className="label ml-3">복무 점수는 수행자에게 간다</span>
        </span>
      </div>
    </section>
  );
}
