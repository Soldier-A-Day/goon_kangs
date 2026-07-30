"use client";

import { useEffect } from "react";
import type { Intent, Snapshot } from "@sad/protocol";
import { QUICK_COMMANDS } from "@/lib/labels";

/**
 * 8.0 퀵 커맨드 — 타임 프레셔 구간의 유일한 채널.
 *
 * 타이핑은 손을 쓴다. 20초 하달 창이나 9초 방독면 착용 구간에서 채팅창을 여는 것은 불가능하므로,
 * 키 하나로 나가는 이 8슬롯이 주 소통 수단이다. 자유 채팅은 여유 구간 전용으로 격하한다 —
 * 타이핑을 전혀 하지 않고도 18일 완주가 가능해야 한다(15.0 접근성).
 */
export function QuickBar({
  snapshot,
  onSend,
  skipVoted,
  onToggleSkip,
}: {
  snapshot: Snapshot;
  onSend: (intent: Intent) => void;
  skipVoted: boolean;
  onToggleSkip: () => void;
}) {
  useEffect(() => {
    function onKey(event: KeyboardEvent) {
      if (event.target instanceof HTMLInputElement) return;
      const command = QUICK_COMMANDS.find((entry) => entry.key === event.key);
      if (!command) return;
      event.preventDefault();
      onSend({ type: "quickCommand", command: command.id });
    }
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [onSend]);

  const alive = snapshot.members.filter((m) => m.presence === "player").length;
  const needed = Math.ceil(alive * 0.75);

  return (
    <section className="flex flex-col gap-2 border border-rule bg-paper-3 p-3">
      <div className="flex items-baseline justify-between">
        <span className="label">퀵 커맨드 — 숫자키</span>
        <button
          type="button"
          onClick={onToggleSkip}
          className={`border px-3 py-1 text-xs font-bold ${
            skipVoted ? "border-accent bg-ink text-paper" : "border-rule hover:bg-paper-2"
          }`}
        >
          시간대 스킵 투표 {skipVoted ? "취소" : `(${needed}명 필요)`}
        </button>
      </div>

      <ul className="grid grid-cols-4 gap-px bg-rule">
        {QUICK_COMMANDS.map((command) => (
          <li key={command.id}>
            <button
              type="button"
              onClick={() => onSend({ type: "quickCommand", command: command.id })}
              className="flex w-full items-baseline justify-center gap-2 bg-paper px-2 py-2 text-sm hover:bg-paper-2"
            >
              <span className="label">{command.key}</span>
              <b className="font-bold">{command.label}</b>
            </button>
          </li>
        ))}
      </ul>
    </section>
  );
}
