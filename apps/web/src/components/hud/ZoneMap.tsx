"use client";

import type { Intent, Snapshot } from "@sad/protocol";
import { ZONE_GROUPS, ZONE_LABELS, formatSeconds } from "@/lib/labels";

type Quest = Snapshot["quests"][number];
type Zone = Snapshot["members"][number]["zone"];

/**
 * 15.0 부대 지도 + 중앙 하단 상호작용.
 *
 * 구역은 3D 없이 "동선이 멀다"(6.1)를 표현하는 논리 위치다. 이동에는 시간이 들고,
 * 이동 중에는 아무것도 붙잡을 수 없다. 협동이 필요한 퀘스트는 왜 진행이 안 되는지
 * ("2인 필요 — 현재 1명") 그 자리에서 밝힌다.
 */
export function ZoneMap({
  snapshot,
  memberId,
  workingQuestId,
  onSend,
  onToggleWork,
}: {
  snapshot: Snapshot;
  memberId: string;
  workingQuestId: string | null;
  onSend: (intent: Intent) => void;
  onToggleWork: (questId: string | null) => void;
}) {
  const me = snapshot.members.find((m) => m.id === memberId);
  if (!me) return null;

  const traveling = me.travelRemainingMs > 0;
  const here = snapshot.quests.filter(
    (quest) =>
      quest.zone === me.zone &&
      quest.status !== "done" &&
      quest.status !== "locked" &&
      (quest.ownerId === null || quest.ownerId === memberId),
  );

  const presentAt = (zone: Zone) =>
    snapshot.members.filter(
      (m) => m.zone === zone && m.presence !== "evacuated" && m.travelRemainingMs === 0,
    );

  return (
    <section className="flex flex-col gap-3">
      <div className="flex flex-col gap-2 border border-rule bg-paper-3 p-3">
        <div className="flex items-baseline justify-between">
          <span className="label">부대 지도</span>
          {traveling && (
            <span className="label" style={{ color: "var(--heat)" }}>
              이동 중 · {formatSeconds(me.travelRemainingMs)}
            </span>
          )}
        </div>

        {/*
          동 단위로 묶어 그린다. 구역이 8개일 때는 한 판에 늘어놓아도 읽혔지만,
          방이 곧 구역이 된 뒤로는 26칸이라 어느 것이 한 건물인지 알 수 없다.
        */}
        {ZONE_GROUPS.map((group) => (
          <div key={group.name} className="flex flex-col gap-1">
            <span className="label" style={{ color: "var(--ink-2)" }}>
              {group.name}
            </span>
            <ul className="grid grid-cols-2 gap-px bg-rule sm:grid-cols-4">
              {group.zones.map((zone) => {
                const occupants = presentAt(zone);
                const current = me.zone === zone;
                const pending = snapshot.quests.filter(
                  (q) =>
                    q.zone === zone &&
                    q.status !== "done" &&
                    q.status !== "locked" &&
                    (q.ownerId === memberId || q.ownerId === null),
                ).length;

                return (
                  <li key={zone}>
                    <button
                      type="button"
                      disabled={current}
                      onClick={() => {
                        onToggleWork(null);
                        onSend({ type: "move", to: zone });
                      }}
                      className={`flex h-full w-full flex-col gap-1 px-3 py-2 text-left ${
                        current ? "bg-ink text-paper" : "bg-paper hover:bg-paper-2"
                      }`}
                    >
                      <span className="flex items-baseline justify-between gap-2">
                        <b className="text-sm font-bold">{ZONE_LABELS[zone]}</b>
                        {pending > 0 && (
                          <span
                            className="font-mono text-xs font-bold tabular-nums"
                            style={{ color: current ? "var(--paper-2)" : "var(--accent)" }}
                          >
                            {pending}
                          </span>
                        )}
                      </span>
                      <span
                        className="truncate text-[0.6875rem]"
                        style={{ color: current ? "var(--paper-2)" : "var(--ink-2)" }}
                      >
                        {occupants.length > 0
                          ? occupants.map((m) => m.name).join(" · ")
                          : " "}
                      </span>
                    </button>
                  </li>
                );
              })}
            </ul>
          </div>
        ))}
      </div>

      <div className="flex flex-col gap-2 border border-rule bg-paper-3 p-3">
        <span className="label">{ZONE_LABELS[me.zone]}에서 할 수 있는 것</span>

        {traveling ? (
          <p className="py-2 text-sm text-ink-2">도착하면 상호작용할 수 있다.</p>
        ) : here.length === 0 ? (
          <p className="py-2 text-sm text-ink-2">여기서 할 일은 없다.</p>
        ) : (
          <ul className="flex flex-col gap-1">
            {here.map((quest) => (
              <li key={quest.id}>
                <InteractRow
                  quest={quest}
                  active={workingQuestId === quest.id}
                  actors={presentAt(quest.zone).length}
                  onToggle={() =>
                    onToggleWork(workingQuestId === quest.id ? null : quest.id)
                  }
                />
              </li>
            ))}
          </ul>
        )}
      </div>
    </section>
  );
}

function InteractRow({
  quest,
  active,
  actors,
  onToggle,
}: {
  quest: Quest;
  active: boolean;
  actors: number;
  onToggle: () => void;
}) {
  const blocked = quest.minActors > actors;

  return (
    <button
      type="button"
      onClick={onToggle}
      className={`flex w-full flex-col gap-1 border px-3 py-2 text-left ${
        active ? "border-accent bg-paper" : "border-rule-2 bg-paper hover:bg-paper-2"
      }`}
    >
      <span className="flex items-baseline justify-between gap-3">
        <b className="text-sm font-bold">{quest.label}</b>
        <span className="label">
          {blocked
            ? `${quest.minActors}인 필요 — 현재 ${actors}명`
            : active
              ? "수행 중"
              : "붙잡기"}
        </span>
      </span>
      <span className="h-[3px] w-full bg-rule-2">
        <span
          className="block h-full"
          style={{
            width: `${quest.progress * 100}%`,
            background: blocked ? "var(--alert)" : "var(--accent)",
          }}
        />
      </span>
    </button>
  );
}
