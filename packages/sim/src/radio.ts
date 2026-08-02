import type { Quest, RadioState, RunState } from "./types.js";

/**
 * 8.0 무전 상태 (COMM-01).
 *
 * 표 3-1이 통신병의 실패 파급을 이렇게 적었다.
 *
 * > 무전 단절 → 합동 퀘스트 지시 UI 소멸. 근접 채팅 반경 안으로 **모여야만**
 * > 정보 전달 가능
 *
 * 이 상태가 **서버에 있어야 하는 이유는 분대가 같은 것을 봐야 하기 때문**이다.
 * 4인 협동에서 각자 클라이언트가 무전 상태를 따로 계산하면, 한 사람은 미니맵에
 * 아군 마커가 보이고 다른 사람은 안 보이는 일이 생긴다. 그러면 "무전이 끊겨서
 * 모여야 한다"는 합의 자체가 불가능해지고, 통신병이라는 보직이 무의미해진다.
 *
 * 2D 전환에서는 이게 더 중요해졌다(SAD-ART-001 §1.3-1). 3D는 벽이 시야를 가려
 * 무전 두절이 저절로 압박이 됐지만, 탑다운은 화면 안이 다 보이므로 **무전 상태가
 * 유일한 정보 차단 장치**다.
 */

/** 통신병의 무전 유지 일과 — 표 3-1의 "전용 퀘스트 예시"가 그대로 조건이 된다 */
const UPKEEP = ["안테나", "무전", "배터리", "암호", "교신"] as const;

export function isRadioUpkeep(quest: Quest): boolean {
  return UPKEEP.some((word) => quest.label.includes(word));
}

/**
 * 지금 무전 상태.
 *
 * 판정 순서가 곧 심각도 순이다 — 사람이 없는 것이 일과를 놓친 것보다 무겁다.
 */
export function evaluateRadio(state: RunState): RadioState {
  const comms = state.members.find((m) => m.role === "comms");

  // 통신병 자리가 아예 없는 편성은 무전이 없다
  if (!comms) return "down";

  switch (comms.presence) {
    case "player":
      break;
    // 후송 대리는 **필수만** 수행한다(JDG-03). 무전이 유지는 되되 열화한다
    case "npcEvac":
      return "weak";
    // 이탈 대리 · 처음부터 공석 · 후송되어 조작 불가 — 유지할 사람이 없다
    default:
      return "down";
  }

  // 통신병이 자리에 있어도 유지 일과를 놓치면 끊긴다.
  // 실패(failed)는 두절, 시간대 종료로 잠긴 것(locked)은 열화로 본다 —
  // 잠긴 것은 "아직 안 했다"이고 실패는 "못 했다"라 무게가 다르다
  let weak = false;
  for (const quest of state.quests) {
    if (quest.ownerId !== comms.id) continue;
    if (!isRadioUpkeep(quest)) continue;

    if (quest.status === "failed") return "down";
    if (quest.status === "locked") weak = true;
  }

  return weak ? "weak" : "ok";
}

/**
 * 상태를 갱신하고 바뀌었으면 알린다.
 *
 * 매 틱 계산해도 값은 같으므로, **바뀔 때만** 이벤트를 낸다 —
 * 10Hz로 같은 이벤트를 흘리면 클라이언트 알림 스택이 그것만으로 찬다.
 */
export function refreshRadio(state: RunState): RadioState | null {
  const next = evaluateRadio(state);
  if (next === state.radio) return null;
  state.radio = next;
  return next;
}
