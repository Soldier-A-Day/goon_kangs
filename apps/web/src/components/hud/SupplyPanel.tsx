"use client";

import { useState } from "react";
import type { Intent, Snapshot } from "@sad/protocol";
import { ITEM_LABELS } from "@/lib/labels";

/** 카탈로그는 sim의 data/supply.json과 같은 값이다. web은 sim을 참조할 수 없으므로(ARCH-02) 복제한다. */
const CATALOG: { id: string; cost: number }[] = [
  { id: "parka", cost: 6 },
  { id: "winterBoots", cost: 4 },
  { id: "insulatedCanteen", cost: 3 },
  { id: "canteen2", cost: 2 },
  { id: "coolingTowel", cost: 3 },
  { id: "icePack", cost: 4 },
  { id: "medkit", cost: 5 },
  { id: "rations", cost: 3 },
];

/**
 * 11.0 보급 청구.
 *
 * 청구서는 행정병이 쓴다. 포인트는 항상 부족하므로 무엇을 먼저 살지가 판단이 되고,
 * 다음 날 예보를 아는 행정병만 정답을 안다 — 정보를 가진 사람과 결정권을 가진
 * 분대장이 대화해야 하는 구조가 여기서 나온다.
 */
export function SupplyPanel({
  snapshot,
  memberId,
  onSend,
}: {
  snapshot: Snapshot;
  memberId: string;
  onSend: (intent: Intent) => void;
}) {
  const me = snapshot.members.find((m) => m.id === memberId);
  const [picked, setPicked] = useState<string[]>([]);

  if (!me) return null;

  const isAdmin = me.role === "admin";
  const budget = snapshot.supply.points;
  const cost = picked.reduce(
    (sum, id) => sum + (CATALOG.find((item) => item.id === id)?.cost ?? 0),
    0,
  );
  const over = cost > budget;

  // 분대에 하나라도 모자란 장비 — 오늘 밤 조건 D가 깨질 신호다
  const shortages = [...new Set(snapshot.members.flatMap((m) => m.missingGear))];

  return (
    <section className="flex flex-col gap-3 border border-rule bg-paper-3 p-3">
      <div className="flex flex-wrap items-baseline justify-between gap-2">
        <span className="label">
          보급 {snapshot.supply.isSupplyDay && "· 오늘 보급일"}
        </span>
        <span className="flex items-baseline gap-2">
          <span className="label">포인트</span>
          <b className="font-mono text-lg font-bold tabular-nums">{budget}</b>
        </span>
      </div>

      {shortages.length > 0 && (
        <p className="border-l-[3px] border-alert bg-paper px-3 py-2 text-sm text-alert">
          필수 장비 부족: {shortages.map((id) => ITEM_LABELS[id] ?? id).join(" · ")} —
          점호 전에 갖추지 못하면 조건 D가 깨진다.
        </p>
      )}

      {!isAdmin ? (
        <p className="text-sm text-ink-2">
          청구서는 행정병이 작성한다.
          {snapshot.supply.pendingClaim.length > 0 && (
            <>
              {" "}
              제출됨:{" "}
              {snapshot.supply.pendingClaim
                .map((id) => ITEM_LABELS[id] ?? id)
                .join(" · ")}
            </>
          )}
        </p>
      ) : (
        <>
          <ul className="grid grid-cols-2 gap-px bg-rule">
            {CATALOG.map((item) => {
              const selected = picked.includes(item.id);
              const owned = me.inventory.includes(item.id);
              const urgent = shortages.includes(item.id);

              return (
                <li key={item.id}>
                  <button
                    type="button"
                    disabled={owned}
                    onClick={() =>
                      setPicked((current) =>
                        selected
                          ? current.filter((id) => id !== item.id)
                          : [...current, item.id],
                      )
                    }
                    className={`flex w-full items-baseline justify-between gap-2 px-3 py-2 text-left text-sm ${
                      selected ? "bg-ink text-paper" : "bg-paper hover:bg-paper-2"
                    } ${owned ? "opacity-40" : ""}`}
                  >
                    <span className="flex items-baseline gap-2">
                      {ITEM_LABELS[item.id] ?? item.id}
                      {urgent && !owned && (
                        <span className="label" style={{ color: "var(--alert)" }}>
                          부족
                        </span>
                      )}
                    </span>
                    <span className="label">{owned ? "보유" : item.cost}</span>
                  </button>
                </li>
              );
            })}
          </ul>

          <div className="flex flex-wrap items-center justify-between gap-2">
            <span className="text-sm">
              청구 합계{" "}
              <b
                className="font-mono font-bold tabular-nums"
                style={{ color: over ? "var(--alert)" : undefined }}
              >
                {cost}
              </b>
              <span className="label ml-2">예산 {budget}</span>
            </span>
            <button
              type="button"
              disabled={picked.length === 0 || over}
              onClick={() => onSend({ type: "fileClaim", items: picked })}
              className="border-2 border-ink bg-ink px-4 py-2 text-sm font-bold text-paper disabled:opacity-40"
            >
              청구서 제출
            </button>
          </div>

          {snapshot.supply.pendingClaim.length > 0 && (
            <p className="text-xs text-ink-2">
              제출됨:{" "}
              {snapshot.supply.pendingClaim.map((id) => ITEM_LABELS[id] ?? id).join(" · ")}{" "}
              — 다음 보급일 아침에 들어온다
            </p>
          )}
        </>
      )}
    </section>
  );
}
