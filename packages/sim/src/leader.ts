import type { Effect, Member, RunState } from "./types.js";

/**
 * G1 — 분대장 자동 지정 · 재지정 · 투표.
 *
 * **예전엔 `state.leaderId`가 런 내내 `null`이었다.** `run.ts`가 `leaderId: null`로
 * 시작한 뒤 아무도 채우지 않았고, `voteLeader` 인텐트는 프로토콜에 정의만 돼
 * 있을 뿐 `step.ts`가 받지 않았다(예전 room.ts가 sim을 거치지 않고 자기
 * 로컬 상태를 직접 바꿔쓰는 우회로만 있었다 — ARCH-02 위반이자, `step`을 안
 * 타므로 재현성도 없었다). 그 결과 `relief.ts` `canUseRelief`의 첫 줄
 * (`state.leaderId !== leaderId` → `notLeader`)을 절대 통과할 수 없었다 —
 * 화면엔 "구제권 3장"이 보이는데 쓸 방법이 없는 상태였다.
 *
 * 이 파일이 그 공백을 채운다: 런 시작 시 결정적으로 한 명을 뽑고(`pickLeader`),
 * 그 사람이 더 이상 `player`가 아니게 되면 다시 뽑고(`reassignLeaderIfVacant`),
 * 분대가 직접 투표로 바꿀 수도 있게 한다(`voteLeader`).
 */

/**
 * 사람 참석자 중 한 명을 결정적으로 고른다.
 *
 * `members`는 `createRun`에서 이미 `ROLES` 순서(rifle→comms→medic→admin)로
 * 정렬돼 있다(run.ts). 그 순서에서 처음 만나는 `presence === "player"`를
 * 뽑는다 — 같은 편성이면 언제나 같은 사람이 뽑히므로 "결정적"이라는 요구를
 * 만족한다. 사람이 아무도 없으면(전원 NPC 대리/공석) null.
 */
export function pickLeader(members: readonly Member[]): string | null {
  const first = members.find((m) => m.presence === "player");
  return first?.id ?? null;
}

/**
 * 분대장 자리가 비었으면(애초에 없었거나, 있던 사람이 나가거나 후송됐으면)
 * 다시 뽑는다.
 *
 * 후송(`evacuate`)·중도 이탈(`leaveRun`)·복귀(`rejoinRun`·`returnEvacuees`) —
 * `member.presence`가 바뀌는 모든 자리에서 이 함수를 부른다. 지금 분대장이
 * 여전히 `player`면 아무 일도 하지 않는다(멀쩡한 분대장을 매번 다시 뽑을
 * 이유가 없다).
 */
export function reassignLeaderIfVacant(state: RunState, effects: Effect[]): void {
  const current = state.members.find((m) => m.id === state.leaderId);
  if (current && current.presence === "player") return;

  const next = pickLeader(state.members);
  if (next === state.leaderId) return;

  state.leaderId = next;
  // 공백을 다시 채우는 것은 새 선출이다 — 지난 투표는 무효로 비운다.
  state.leaderVotes = {};
  effects.push({ type: "leaderChanged", leaderId: next });
}

/**
 * 분대장 선출 투표 — 과반 원칙.
 *
 * ARCH-02(규칙은 sim에만 있다) — 이 과반 계산은 원래 `services/gameserver/src/room.ts`
 * `voteLeader`(사설 메서드)에 있었다. `step`을 거치지 않고 `this.run.leaderId`를
 * 직접 바꿔써서 두 가지 문제가 있었다: (1) 규칙이 서버 계층에 새 있어 ARCH-02를
 * 어겼고, (2) sim의 순수 `step` 함수를 안 타므로 헤드리스 시뮬·리플레이가 이
 * 경로를 재현할 수 없었다. 여기로 옮기고 room.ts는 이벤트만 전달한다.
 *
 * 규칙(근거):
 *  - 지금 참석 중인(`presence === "player"`) 사람만 투표할 수 있고, 지금
 *    참석 중인 사람에게만 투표할 수 있다 — 나간 사람을 분대장으로 뽑아봐야
 *    바로 다음 순간 `reassignLeaderIfVacant`가 도로 갈아치운다.
 *  - **과반(지금 참석 중인 전체 인원의 절반 초과)을 얻은 후보가 나오는 즉시
 *    확정한다** — 전원이 투표할 때까지 기다리지 않는다. 이게 "즉시"다.
 *    분모는 "지금까지 투표한 사람 수"가 아니라 "지금 참석 중인 전체 인원"
 *    이다 — 안 그러면 딱 한 명만 투표해도 1표가 그 한 표의 과반(1 > 0.5)이
 *    되어 즉시 확정돼 버린다.
 *  - 동수(예: 2:2)는 과반이 아니므로 아무도 확정되지 않는다 — 현직 유지.
 *  - 나간 사람이 예전에 던진 표는 득표 집계에서 뺀다 — 안 그러면 나간
 *    사람의 표가 영영 유령표로 남아 특정 후보를 유리하게 만든다.
 *  - 확정되는 순간 표를 비운다 — 다음 공백 때 새로 걷는다.
 */
export function voteLeader(
  state: RunState,
  memberId: string,
  candidateId: string,
  effects: Effect[],
): void {
  const voter = state.members.find((m) => m.id === memberId);
  if (!voter || voter.presence !== "player") return;
  const candidate = state.members.find((m) => m.id === candidateId);
  if (!candidate || candidate.presence !== "player") return;

  state.leaderVotes[memberId] = candidateId;

  const tally = new Map<string, number>();
  for (const [voterId, pick] of Object.entries(state.leaderVotes)) {
    const stillHere = state.members.find((m) => m.id === voterId);
    if (!stillHere || stillHere.presence !== "player") continue;
    tally.set(pick, (tally.get(pick) ?? 0) + 1);
  }

  const totalPlayers = state.members.filter((m) => m.presence === "player").length;
  for (const [pick, count] of tally) {
    if (count > totalPlayers / 2) {
      if (state.leaderId !== pick) {
        state.leaderId = pick;
        effects.push({ type: "leaderChanged", leaderId: pick });
      }
      state.leaderVotes = {};
      return;
    }
  }
}
