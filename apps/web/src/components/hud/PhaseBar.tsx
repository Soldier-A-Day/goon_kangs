"use client";

import type { Snapshot } from "@sad/protocol";
import { BAND_MARKS, formatSeconds } from "@/lib/labels";

/**
 * 15.0 상단 시간대 바 — 현재 시간대명 + 잔여 시간 + 오늘 기온 밴드.
 * 잔여 20% 이하에서 색으로 경고한다.
 */
export function PhaseBar({
  snapshot,
  elapsedMs,
}: {
  snapshot: Snapshot;
  elapsedMs: number;
}) {
  const { phase, weather, discipline } = snapshot;
  const capped = Math.min(elapsedMs, phase.durationMs);
  const remaining = Math.max(0, phase.durationMs - capped);
  const ratio = phase.durationMs === 0 ? 0 : remaining / phase.durationMs;
  const warning = ratio <= 0.2;
  const band = BAND_MARKS[weather.band];

  return (
    <header className="border-b-2 border-ink bg-paper-3">
      <div className="mx-auto flex w-full max-w-6xl flex-wrap items-center gap-x-6 gap-y-2 px-4 py-2">
        <span className="label">
          D-{String(snapshot.day).padStart(2, "0")} / {snapshot.totalDays}
        </span>

        <span className="flex items-baseline gap-2">
          <b className="text-sm font-bold">{phase.label}</b>
          <span className="label">{phase.clock}</span>
        </span>

        <span
          className="font-mono text-sm font-bold tabular-nums"
          style={{ color: warning ? "var(--alert)" : undefined }}
        >
          {formatSeconds(remaining)}
        </span>

        <span className="flex items-center gap-2 text-sm" style={{ color: band.tone }}>
          <span aria-hidden>{band.mark}</span>
          <b className="font-bold">{weather.label}</b>
          <span className="font-mono text-xs tabular-nums">{weather.feelsLike}°C</span>
        </span>

        <span className="ml-auto flex items-center gap-3 text-sm">
          <span className="label">군기</span>
          <b
            className="font-mono font-bold tabular-nums"
            style={{
              color:
                discipline.value < 40
                  ? "var(--alert)"
                  : discipline.value >= 80
                    ? "var(--accent)"
                    : undefined,
            }}
          >
            {discipline.value}
          </b>
          <span className="label">구제 {snapshot.reliefsRemaining}</span>
        </span>
      </div>

      <div className="h-1 w-full bg-rule-2">
        <div
          className="h-full transition-[width] duration-100 ease-linear"
          style={{
            width: `${ratio * 100}%`,
            background: warning ? "var(--alert)" : "var(--accent)",
          }}
        />
      </div>

      {phase.delegationWindowMsLeft > 0 && (
        <p className="bg-ink px-4 py-1 text-center text-xs font-bold text-paper">
          하달 창 — 시간대 타이머 정지 · {formatSeconds(phase.delegationWindowMsLeft)}
        </p>
      )}
    </header>
  );
}
