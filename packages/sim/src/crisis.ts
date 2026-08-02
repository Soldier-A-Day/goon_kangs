import { evacuate } from "./evacuation.js";
import type { CrisisStat, Effect, Member, RunState } from "./types.js";

/**
 * B-2 위기와 구조.
 *
 * 컨디션 붕괴는 예전엔 즉시 후송이었다(JDG-03 — `evacuation.ts` `checkCollapses`).
 * 쓰러지는 순간 혼자 조용히 실려 나갔다. 그 자리에 "위기"를 끼워 넣는다: 쓰러진
 * 사람은 그대로 두면 후송되지만, 시간 안에 동료가 곁에서 응급처치를 하면 그
 * 자리에서 살아난다. 실패하면 `evacuate()`가 그대로 돈다 — 여기서 긴장을
 * 물타기하지 않는다는 것이 발주의 요구다.
 *
 * 새 미니게임을 만들지 않는다. 판정은 붙잡은 시간(ChoreBoard식)만으로 하고,
 * 원형을 하나도 늘리지 않는다 — 위기는 조작을 겨루는 게임이 아니라 "곁에
 * 있어 주는 시간"이어야 긴장이 산다.
 */

/** 위기 지속 시간 — 이 안에 구조하지 못하면 `evacuate()`가 그대로 돈다 */
export const CRISIS_MS = 45_000;

/** 구조에 필요한 누적 홀드 시간(기본) */
export const RESCUE_REQUIRED_MS = 8_000;

/**
 * 의무병 구조 가속 배율.
 *
 * condition.ts의 `noMedicMultiplier`가 "의무병이 없으면 전원이 손해를 본다"는
 * 페널티 쪽 대칭이라면, 이건 성공 쪽 대칭이다 — 의무병이 있으면 구조가 빠르다.
 */
export const MEDIC_RESCUE_MULTIPLIER = 2;

/**
 * 구조 성공 시 회복량. 완치가 아니라 "다시 움직일 만큼"만 되돌린다 — 위기의
 * 원인이 된 스탯 하나만 회복하고, 나머지 소모는 그대로 남아 다음 위기를 예고한다.
 */
export const RESCUE_RECOVERY: Record<CrisisStat, number> = {
  stamina: 30,
  hydration: 25,
};

/**
 * 위기 진입. 이미 위기 중이면 다시 걸지 않는다 — 체력 0·탈수 2단계는 다음
 * 시간대 정산에서도 여전히 조건을 만족하므로, 가드가 없으면 매 정산마다
 * 타이머가 45초로 리셋되어 사실상 영원히 안 쓰러진다.
 *
 * NPC 대리(`presence !== "player"`)는 호출부(`checkCollapses`)에서 아예 걸러진다 —
 * 대리는 원래도 컨디션으로 후송되지 않았으므로(JDG-03), 위기 역시 겪지 않는다.
 * 봇 완주율에 영향이 없는 이유가 이것이다 — 새 장치를 얹지 않아도 이미 중립이다.
 */
export function enterCrisis(
  state: RunState,
  member: Member,
  stat: CrisisStat,
  effects: Effect[],
): void {
  if (member.crisisStat !== null) return;
  member.crisisStat = stat;
  member.crisisMsLeft = CRISIS_MS;
  member.rescueMs = 0;
  effects.push({ type: "crisisStarted", memberId: member.id, stat, crisisMs: CRISIS_MS });
}

/**
 * 위기 시계. `applyTick`(step.ts)에서 실시간 ms로 흐른다 — 시간대 경계와
 * 무관하게 45초는 45초다(하달 창처럼 멈추지 않는다. 쓰러진 사람에게 UI 사정은
 * 상관없다).
 *
 * 다 흐르면 `evacuate()`가 그대로 돈다 — "실패하면 기존 후송 그대로, 긴장을
 * 물타기하지 않는다"의 실행부다.
 */
export function tickCrisis(state: RunState, elapsedMs: number, effects: Effect[]): void {
  if (elapsedMs <= 0) return;

  for (const member of state.members) {
    if (member.crisisStat === null) continue;

    member.crisisMsLeft -= elapsedMs;
    if (member.crisisMsLeft > 0) continue;

    member.crisisStat = null;
    member.crisisMsLeft = 0;
    member.rescueMs = 0;
    evacuate(state, member.id, effects);
  }
}

/**
 * 구조 상호작용. 기존 `applyWork`(step.ts)와 같은 모양이지만 대상이 사람이다.
 *
 * 자기 자신은 구조할 수 없고, 위기에 빠진 사람은 구조자가 될 수 없다(둘 다
 * 쓰러져 있을 수는 없다), 곁(같은 구역)에 있어야 한다 — 원격 구조는 없다.
 * 이 관문들은 전부 서버가 본다. 클라는 "지금 이 사람을 붙잡고 있다"는 신고만
 * 보낸다(ARCH-02).
 */
export function applyRescueWork(
  state: RunState,
  rescuerId: string,
  targetId: string,
  deltaMs: number,
  effects: Effect[],
): void {
  if (deltaMs <= 0) return;
  const rescuer = state.members.find((m) => m.id === rescuerId);
  const target = state.members.find((m) => m.id === targetId);
  if (!rescuer || !target) return;
  if (rescuer.id === target.id) return;
  if (target.crisisStat === null) return;
  if (rescuer.presence !== "player") return;
  if (rescuer.crisisStat !== null) return;
  if (rescuer.travelRemainingMs > 0) return;
  if (rescuer.zone !== target.zone) return;

  const rate = rescuer.role === "medic" ? MEDIC_RESCUE_MULTIPLIER : 1;
  target.rescueMs = Math.min(RESCUE_REQUIRED_MS, target.rescueMs + deltaMs * rate);
  if (target.rescueMs < RESCUE_REQUIRED_MS) return;

  const stat = target.crisisStat;
  target.crisisStat = null;
  target.crisisMsLeft = 0;
  target.rescueMs = 0;
  target.stats[stat] = Math.min(100, target.stats[stat] + RESCUE_RECOVERY[stat]);

  effects.push({ type: "crisisRescued", memberId: target.id, rescuerId: rescuer.id, stat });
}
