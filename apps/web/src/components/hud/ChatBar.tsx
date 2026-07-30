"use client";

import { useState } from "react";
import type { Intent, Snapshot } from "@sad/protocol";
import { ZONE_LABELS } from "@/lib/labels";

/**
 * 8.0 자유 채팅.
 *
 * 협동 4패턴 중 **정보 전달**(암구호·좌표·주파수)만이 자유 텍스트를 요구한다 —
 * 화면에 보이는 사람과 입력하는 사람이 분리되는 패턴이라 퀵 커맨드로는 대체할 수 없다.
 * 나머지 세 패턴은 퀵 커맨드로 해결되므로 타이핑 없이도 18일 완주가 가능해야 한다(15.0).
 *
 * 통신병이 치면 무전이라 거리 제한이 없고, 나머지는 같은 구역에만 닿는다.
 * 무전이 끊기면 물리적으로 모여야 정보가 전달된다 — 통신병 실패의 대가가 이동 시간이 된다.
 */
export function ChatBar({
  snapshot,
  memberId,
  onSend,
}: {
  snapshot: Snapshot;
  memberId: string;
  onSend: (intent: Intent) => void;
}) {
  const [text, setText] = useState("");
  const me = snapshot.members.find((m) => m.id === memberId);
  if (!me) return null;

  const radio = me.role === "comms";
  const nearby = snapshot.members.filter(
    (m) =>
      m.id !== memberId &&
      m.presence === "player" &&
      m.zone === me.zone &&
      m.travelRemainingMs === 0,
  ).length;

  function submit(event: React.FormEvent) {
    event.preventDefault();
    const message = text.trim();
    if (!message) return;
    onSend({ type: "chat", text: message });
    setText("");
  }

  return (
    <form onSubmit={submit} className="flex flex-col gap-2 border border-rule bg-paper-3 p-3">
      <div className="flex flex-wrap items-baseline justify-between gap-2">
        <span className="label">{radio ? "무전 — 거리 제한 없음" : "근접 채팅"}</span>
        <span className="label">
          {radio
            ? "전 분대 수신"
            : nearby > 0
              ? `${ZONE_LABELS[me.zone]}에 ${nearby}명`
              : `${ZONE_LABELS[me.zone]}에 혼자 — 아무도 듣지 못한다`}
        </span>
      </div>

      <div className="flex gap-2">
        <input
          value={text}
          onChange={(event) => setText(event.target.value)}
          maxLength={200}
          placeholder={radio ? "전 분대에 보낸다" : "같은 구역에만 들린다"}
          className="min-w-0 flex-1 border border-rule bg-paper px-3 py-2 text-sm outline-none focus:border-accent"
        />
        <button
          type="submit"
          disabled={text.trim().length === 0}
          className="border-2 border-ink bg-ink px-4 py-2 text-sm font-bold text-paper disabled:opacity-40"
        >
          전송
        </button>
      </div>
    </form>
  );
}
