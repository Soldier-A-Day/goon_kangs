import { bandRule } from "./weather.js";
import { hasActiveMedic } from "./condition.js";
import { isHeatSource } from "./zones.js";
import type { Effect, Member, RunState } from "./types.js";

/**
 * 5.0 혹한 특수 규칙 — 보온 게이지.
 *
 * > 극혹한 밴드에서는 체감온도 게이지가 별도로 표시되고, **90초마다 열원(난로·차량
 * > 히터·실내)에 접촉**해야 게이지가 리셋된다. 미접촉 시 동상 디버프(이동 속도 −30%,
 * > 상호작용 실패율 +20%) → **의무병만 해제 가능.**
 *
 * 이 게이지가 서버에 있어야 하는 이유는 **판정이기 때문**이다. 게이지가 0이 되면
 * 이동과 작업이 실제로 느려지고, 그건 그날의 필수 퀘스트를 끝낼 수 있는지를 바꾼다.
 * 클라이언트가 스스로 세면 화면과 판정이 갈라지고, 4인이 각자 다른 숫자를 본다.
 *
 * 열원 판정은 **구역 단위**다. sim에는 좌표가 없고(`zones.ts`: "좌표는 논리 좌표이며
 * 렌더링과 무관하다") 앞으로도 두지 않을 것이므로, "실내이거나 난로가 있는 구역"이
 * 곧 열원 접촉이다. 아트 쪽(§9.3)이 난로 라이트 반경을 판정 반경과 일치시키는 것은
 * 같은 규칙을 눈에 보이게 만드는 일이지 별도 판정이 아니다.
 */

/** 극혹한이 아닌 밴드에서 게이지가 머무는 값 — 가득 찬 상태 */
export const WARMTH_FULL_MS = Number.POSITIVE_INFINITY;

export interface WarmthRule {
  readonly seconds: number;
  readonly moveSlowdown: number;
  readonly workPenalty: number;
}

/** 지금 밴드에 보온 게이지가 있는가. 없으면 이 시스템 전체가 잠든다 */
export function warmthRule(state: RunState): WarmthRule | null {
  const gauge = bandRule(state.weather.band).warmthGauge;
  return gauge ?? null;
}

/**
 * 게이지를 흘린다. `applyTick`에서 실시간 ms로 돈다 —
 * 90초는 시간대(칸)가 아니라 **실시간**이라 시간대 정산에 얹을 수 없다.
 */
export function tickWarmth(state: RunState, elapsedMs: number, effects: Effect[]): void {
  const rule = warmthRule(state);

  // **가장 뜨거운 경로다.** `applyTick`이 매 틱 부르고, 헤드리스 배치 시뮬은
  // 이걸 수백만 번 돈다. 극혹한이 아니면 할 일이 없으므로 멤버 순회 전에 빠진다 —
  // 게이지를 0으로 되돌리는 것은 밴드가 바뀔 때 한 번이면 충분하다(`clearWarmth`).
  if (!rule) return;

  const full = rule.seconds * 1000;

  for (const member of state.members) {
    if (member.presence === "evacuated") continue;

    // 이동 중에는 밖을 지나는 것이다 — 도착 전까지 열원에 닿을 수 없다
    const sheltered = member.travelRemainingMs <= 0 && isHeatSource(member.zone);

    if (sheltered) {
      member.warmthRemainingMs = full;
      continue;
    }

    // 처음 극혹한에 들어선 사람은 가득 찬 상태에서 시작한다
    if (member.warmthRemainingMs <= 0 && !member.frostbitten) {
      member.warmthRemainingMs = full;
    }

    member.warmthRemainingMs = Math.max(0, member.warmthRemainingMs - elapsedMs);

    if (member.warmthRemainingMs === 0 && !member.frostbitten) {
      member.frostbitten = true;
      effects.push({ type: "frostbitten", memberId: member.id });
    }
  }

  if (frostbittenAny(state)) relieveFrostbite(state, effects);
}

/** 아무도 동상이 아니면 해제 판정을 돌 이유가 없다 */
function frostbittenAny(state: RunState): boolean {
  for (const member of state.members) {
    if (member.frostbitten) return true;
  }
  return false;
}

/**
 * 밴드가 극혹한을 벗어날 때 한 번 부른다.
 *
 * 디버프가 남아 있으면 왜 느린지 알 방법이 없다. 매 틱 되돌리는 대신
 * 밴드가 바뀌는 순간에만 치운다.
 */
export function clearWarmth(state: RunState): void {
  if (warmthRule(state)) return;
  for (const member of state.members) {
    member.warmthRemainingMs = 0;
    member.frostbitten = false;
  }
}

/**
 * 5.0 — 동상은 **의무병만 해제할 수 있다.**
 *
 * 열원에 다시 가는 것만으로는 안 풀린다. 그게 이 디버프를 의무병에게 묶는 장치이고,
 * 의무병이 없으면(후송·이탈) 분대가 그대로 안고 가야 한다 — 표 3-1이 말한
 * "온도 피해 회복 불가"가 여기서 실제로 성립한다.
 *
 * 조건이 셋이다.
 *
 *   1. **의무병이 같은 구역에 있을 것** — 좌표가 없으므로 그 이상은 셀 수 없고,
 *      거기까지 걸어와야 한다는 비용은 그대로 남는다
 *   2. **그 구역이 열원일 것** — 눈밭 한복판에서 응급처치를 할 수는 없다.
 *      이 조건이 "동상 걸린 사람을 데리고 실내로 들어간다"를 만들고,
 *      그 왕복이 §6.1이 말한 시간 비용으로 돌아온다
 *   3. **의무병 자신이 멀쩡할 것** — 같이 얼어 있으면 아무도 못 푼다
 *
 * 2번이 없으면 의무병 옆에 붙어 다니는 것만으로 동상이 무의미해진다. 실제로
 * 처음엔 조건 1만 뒀다가, 동상이 붙은 바로 그 틱에 풀려버리는 것을 테스트가 잡았다.
 */
function relieveFrostbite(state: RunState, effects: Effect[]): void {
  if (!hasActiveMedic(state)) return;

  const medic = state.members.find(
    (m) =>
      m.role === "medic" &&
      m.presence !== "evacuated" &&
      m.travelRemainingMs <= 0 &&
      !m.frostbitten &&
      isHeatSource(m.zone),
  );
  if (!medic) return;

  for (const member of state.members) {
    if (!member.frostbitten) continue;
    if (member.zone !== medic.zone) continue;
    if (member.travelRemainingMs > 0) continue;

    member.frostbitten = false;
    member.warmthRemainingMs = (warmthRule(state)?.seconds ?? 0) * 1000;
    effects.push({ type: "frostbiteRelieved", memberId: member.id, byId: medic.id });
  }
}

/** 이동 속도 −30% — 같은 거리를 더 오래 걷는다 */
export function travelMultiplier(state: RunState, member: Member): number {
  if (!member.frostbitten) return 1;
  const rule = warmthRule(state);
  return rule ? 1 / (1 - rule.moveSlowdown) : 1;
}

/**
 * 상호작용 실패율 +20% — 진척이 그만큼 덜 쌓인다.
 *
 * "실패율"을 확률로 굴리지 않고 진척 감소로 옮기는 이유는 판정이 결정적이어야
 * 하기 때문이다. 같은 조작에 어떤 날은 되고 어떤 날은 안 되면, 플레이어는 자기
 * 상태가 나빠진 것을 배우는 대신 게임이 불공정하다고 배운다.
 */
export function workMultiplier(state: RunState, member: Member): number {
  if (!member.frostbitten) return 1;
  const rule = warmthRule(state);
  return rule ? 1 - rule.workPenalty : 1;
}
