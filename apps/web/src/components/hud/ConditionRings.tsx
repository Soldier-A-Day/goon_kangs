"use client";

import type { Snapshot } from "@sad/protocol";
import { STAT_LABELS } from "@/lib/labels";

type Member = Snapshot["members"][number];

/**
 * 15.0 좌하단 컨디션 — 6스탯 컴팩트 링. 위험 구간만 강조한다.
 * 피로만 방향이 반대다(0에서 시작해 100으로 오른다).
 */
export function ConditionRings({ member }: { member: Member }) {
  return (
    <section className="flex flex-col gap-2 border border-rule bg-paper-3 p-3">
      <span className="label">컨디션</span>
      <ul className="grid grid-cols-3 gap-2">
        {STAT_LABELS.map(({ key, label, inverted }) => {
          const value = member.stats[key];
          const danger = inverted ? value >= 80 : value <= 20;
          const caution = inverted ? value >= 60 : value <= 40;
          const tone = danger ? "var(--alert)" : caution ? "var(--heat)" : "var(--accent)";

          return (
            <li key={key} className="flex flex-col items-center gap-1">
              <span
                className="relative grid h-12 w-12 place-items-center rounded-full"
                style={{
                  background: `conic-gradient(${tone} ${value * 3.6}deg, var(--rule-2) 0deg)`,
                }}
              >
                <span className="grid h-9 w-9 place-items-center rounded-full bg-paper-3 font-mono text-xs font-bold tabular-nums">
                  {Math.round(value)}
                </span>
              </span>
              <span
                className="text-[0.6875rem]"
                style={{ color: danger ? "var(--alert)" : "var(--ink-2)" }}
              >
                {label}
              </span>
            </li>
          );
        })}
      </ul>
    </section>
  );
}
