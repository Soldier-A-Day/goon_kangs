"use client";

import { ROLE_LABELS, type Role } from "@/lib/api";

/**
 * 보직 카드 (목업 §11 상단).
 *
 * 카드 하나가 **보직 하나의 존재 이유**를 다 담는다 — 이름, 하는 일,
 * 그리고 비었을 때 분대가 잃는 것. 마지막 줄이 붉은 이유는 §3.0에서
 * 보직이 취향이 아니라 **역할 분담**이기 때문이다. 아무나 골라도 되는
 * 목록으로 보이면 4인이 전부 소총수를 고르려 든다.
 *
 * 도트 초상은 SVG로 직접 찍는다. 게임 스프라이트(32×48)를 그대로 쓰면
 * 웹 셸이 Unity 번들을 기다려야 하고, 로비는 번들이 오기 **전에** 보여야 한다.
 */
export function RoleCard({
  role,
  state,
  holder,
  onPick,
}: {
  role: Role;
  /** `mine` 내가 고른 것 · `taken` 남이 가진 것 · `open` 빈 자리 · `npc` NPC가 채울 자리 */
  state: "mine" | "taken" | "open" | "npc";
  holder?: string;
  onPick?: () => void;
}) {
  const meta = ROLE_LABELS[role];
  const mine = state === "mine";
  const npc = state === "npc";

  return (
    <button
      type="button"
      onClick={onPick}
      disabled={state === "taken" || !onPick}
      aria-pressed={mine}
      className={`group flex w-full flex-col items-stretch text-left transition-colors ${
        mine ? "bg-sunk" : "bg-paper-3 hover:bg-paper-2"
      } ${state === "taken" ? "cursor-not-allowed" : ""}`}
      style={{
        border: mine
          ? "3px solid var(--accent)"
          : npc
            ? "2px dashed var(--alert)"
            : "1px solid var(--rule)",
      }}
    >
      {/* 머리띠 — 색이 곧 보직이다 (§3.2 "색은 정보다") */}
      <span
        className="flex h-[30px] items-center justify-center text-[0.6875rem] font-extrabold tracking-widest"
        style={{ background: meta.tint, color: "var(--paper)" }}
      >
        {meta.code}
      </span>

      <span className="flex justify-center py-5">
        <Portrait role={role} dim={npc} />
      </span>

      {/* 설명이 두 줄로 넘치는 보직이 있다(행정병). 최소 높이를 주지 않으면
          카드 넷의 아래 끝이 어긋나 목업의 정렬이 깨진다 */}
      <span className="flex min-h-[104px] flex-col gap-1 px-5 pb-4">
        <b className="text-xl font-bold">{meta.name}</b>
        <span className="text-sm text-ink-2">{meta.duty}</span>
        <span className="text-[0.8125rem]" style={{ color: "var(--alert)" }}>
          {meta.fail}
        </span>
      </span>

      <span
        className="mx-5 mt-auto mb-5 flex h-[30px] shrink-0 items-center justify-center text-sm"
        style={
          mine
            ? { background: "var(--accent)", color: "var(--paper)", fontWeight: 800 }
            : npc
              ? { background: "var(--alert-bg)", color: "var(--alert)", fontWeight: 700 }
              : { background: "var(--sunk)", color: "var(--accent)" }
        }
      >
        {mine
          ? "나 · 선택 중"
          : npc
            ? "NPC 대리로 진행됨"
            : holder
              ? `${holder} · 준비 완료`
              : "비어 있음"}
      </span>
    </button>
  );
}

/**
 * 도트 초상 (목업 §11).
 *
 * 보직을 가르는 것은 **머리와 손**이다(§5.2 표 "head — 보직 식별 1순위").
 * 통신병은 안테나, 의무병은 흰 완장, 행정병은 서류판. 몸통은 전부 같다 —
 * 같은 병사이고 임무만 다르다는 것이 그 자체로 정보다.
 */
function Portrait({ role, dim }: { role: Role; dim?: boolean }) {
  const skin = "#e0b48c";
  const cloth = "#6e7a50";
  const dark = "#55603c";
  const cap = "#3d4629";
  const boot = "#33352e";

  return (
    <svg
      width="128"
      height="132"
      viewBox="0 0 32 33"
      shapeRendering="crispEdges"
      opacity={dim ? 0.35 : 1}
      aria-hidden
    >
      <g transform="translate(6,0)">
        {/* 전투모 */}
        <rect x="12" y="0" width="8" height="1" fill={cap} />
        <rect x="11" y="1" width="10" height="2" fill={dark} />
        <rect
          x="11"
          y="3"
          width="10"
          height="1"
          fill={role === "medic" ? "#eef1e8" : cap}
        />
        {/* 얼굴 */}
        <rect x="12" y="4" width="8" height="5" fill={skin} />
        {role === "comms" && (
          <>
            <rect x="10" y="5" width="2" height="3" fill="#2a2e24" />
            <rect x="20" y="5" width="2" height="3" fill="#2a2e24" />
          </>
        )}
        {/* 어깨 · 몸통 */}
        <rect x="9" y="10" width="14" height="2" fill={cloth} />
        <rect x="11" y="12" width="10" height="9" fill={cloth} />
        {/* 팔 — 완장이 여기 붙는다 */}
        <rect
          x="9"
          y="12"
          width="2"
          height="9"
          fill={
            role === "medic" ? "#eef1e8" : role === "admin" ? "#b08a2c" : dark
          }
        />
        <rect x="21" y="12" width="2" height="9" fill={dark} />
        {/* 다리 · 전투화 */}
        <rect x="12" y="22" width="4" height="7" fill={dark} />
        <rect x="17" y="22" width="4" height="7" fill={dark} />
        <rect x="12" y="29" width="4" height="4" fill={boot} />
        <rect x="17" y="29" width="4" height="4" fill={boot} />
        {/* 지급 장비 */}
        {role === "rifle" && (
          <rect
            x="6"
            y="12"
            width="16"
            height="2"
            fill={boot}
            transform="rotate(38 14 13)"
          />
        )}
        {role === "comms" && <rect x="22" y="2" width="1" height="12" fill="#8a9080" />}
        {role === "admin" && <rect x="21" y="12" width="4" height="6" fill="#d8dad0" />}
      </g>
    </svg>
  );
}
