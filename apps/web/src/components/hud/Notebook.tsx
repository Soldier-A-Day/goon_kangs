"use client";

import type { Snapshot } from "@sad/protocol";
import { KIND_LABELS, ZONE_LABELS } from "@/lib/labels";

type Quest = Snapshot["quests"][number];

/**
 * 15.0 우측 수첩 — 내 필수 / 내 선택 / 하달받은 것 / 분대 합동 4분할.
 * 하달받은 것에는 누가 넘겼는지 이름이 붙는다. 남은 개수를 최상단에 크게 둔다.
 */
export function Notebook({
  snapshot,
  memberId,
  onFocus,
}: {
  snapshot: Snapshot;
  memberId: string;
  onFocus: (quest: Quest) => void;
}) {
  const mine = snapshot.quests.filter((quest) => quest.ownerId === memberId);
  const required = mine.filter((q) => q.required && q.delegatedFrom === null);
  const optional = mine.filter((q) => !q.required && q.delegatedFrom === null);
  const delegated = mine.filter((q) => q.delegatedFrom !== null);
  const joint = snapshot.quests.filter((q) => q.kind === "joint" || q.kind === "surprise");

  const remaining = required.filter((q) => q.status !== "done").length;
  const nameOf = (id: string | null) =>
    snapshot.members.find((m) => m.id === id)?.name ?? "?";

  return (
    <section className="flex min-h-0 flex-col gap-3 border border-rule bg-paper-3 p-3">
      <div className="flex items-baseline justify-between">
        <span className="label">수첩</span>
        <span className="flex items-baseline gap-2">
          <span className="label">남은 필수</span>
          <b
            className="font-mono text-2xl font-extrabold tabular-nums"
            style={{ color: remaining === 0 ? "var(--accent)" : "var(--alert)" }}
          >
            {remaining}
          </b>
        </span>
      </div>

      <div className="flex min-h-0 flex-col gap-3 overflow-y-auto">
        <QuestGroup title="내 필수" quests={required} onFocus={onFocus} />
        <QuestGroup title="내 선택" quests={optional} onFocus={onFocus} />
        <QuestGroup
          title="하달받은 것"
          quests={delegated}
          onFocus={onFocus}
          note={(quest) => `← ${nameOf(quest.delegatedFrom)}`}
        />
        <QuestGroup title="분대 합동 · 돌발" quests={joint} onFocus={onFocus} />
      </div>
    </section>
  );
}

function QuestGroup({
  title,
  quests,
  onFocus,
  note,
}: {
  title: string;
  quests: Quest[];
  onFocus: (quest: Quest) => void;
  note?: (quest: Quest) => string;
}) {
  return (
    <div className="flex flex-col gap-1">
      <span className="label border-b border-rule-2 pb-1">
        {title} <span className="text-ink-2">({quests.length})</span>
      </span>
      {quests.length === 0 ? (
        <span className="py-1 text-xs text-ink-2">없음</span>
      ) : (
        <ul className="flex flex-col">
          {quests.map((quest) => (
            <li key={quest.id}>
              <button
                type="button"
                onClick={() => onFocus(quest)}
                className="flex w-full items-center justify-between gap-2 py-1 text-left text-sm hover:bg-paper-2"
              >
                <span
                  className="flex min-w-0 items-baseline gap-2"
                  style={{
                    textDecoration: quest.status === "done" ? "line-through" : undefined,
                    color:
                      quest.status === "done"
                        ? "var(--ink-2)"
                        : quest.status === "locked"
                          ? "var(--alert)"
                          : undefined,
                  }}
                >
                  <span className="label shrink-0">{KIND_LABELS[quest.kind]}</span>
                  <span className="truncate">{quest.label}</span>
                  {note && <span className="label shrink-0">{note(quest)}</span>}
                </span>
                <span className="label shrink-0">
                  {quest.status === "locked"
                    ? "잠김"
                    : quest.minActors > 1
                      ? `${ZONE_LABELS[quest.zone]} ${quest.minActors}인`
                      : ZONE_LABELS[quest.zone]}
                </span>
              </button>
              {quest.progress > 0 && quest.status !== "done" && (
                <div className="h-[2px] w-full bg-rule-2">
                  <div
                    className="h-full bg-accent"
                    style={{ width: `${quest.progress * 100}%` }}
                  />
                </div>
              )}
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}
