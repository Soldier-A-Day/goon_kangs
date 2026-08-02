import { delegationLedgerSummary } from "./delegation.js";
import { resolveEnding, type EndingResult } from "./hidden.js";
import type { RunState } from "./types.js";

/**
 * 저장 포맷 버전. 구조가 바뀌면 올리고, 맞지 않는 스냅샷은 복구하지 않는다.
 *
 * 2 — F-1이 `RunState.elapsedRealMs`를 새 필수 필드로 추가했다. 옛 스냅샷에는
 * 이 필드가 없어 복구하면 `undefined`로 들어오고, `applyTick`의 `+=`가 그걸
 * 곧장 `NaN`으로 굳혀 해금이 영영 안 열리는 조용한 손상이 된다 — "반쯤 맞는
 * 상태로 되살리는 것이 가장 나쁘다"는 원칙 그대로, 버전을 올려 옛 스냅샷은
 * 복구하지 않고 새로 시작하게 한다.
 */
export const SAVE_VERSION = 2;

export interface RunSave {
  readonly version: number;
  readonly savedAtDay: number;
  readonly state: RunState;
}

/**
 * 17.0 — 런 상태를 스냅샷으로 굳힌다. 전원 이탈 시 24시간 보관 → 이어하기.
 *
 * RunState는 순수 데이터(함수·클래스·Date 없음)라 JSON 왕복이 손실 없이 성립한다.
 * 그 성질이 깨지면 이어하기가 조용히 망가지므로 테스트로 고정한다.
 */
export function serializeRun(state: RunState): string {
  const save: RunSave = {
    version: SAVE_VERSION,
    savedAtDay: state.day,
    state,
  };
  return JSON.stringify(save);
}

export function deserializeRun(raw: string): RunState | null {
  let parsed: unknown;
  try {
    parsed = JSON.parse(raw);
  } catch {
    return null;
  }

  if (typeof parsed !== "object" || parsed === null) return null;
  const save = parsed as Partial<RunSave>;
  // 포맷이 다르면 복구하지 않는다 — 반쯤 맞는 상태로 되살리는 것이 가장 나쁘다
  if (save.version !== SAVE_VERSION || typeof save.state !== "object") return null;

  return save.state as RunState;
}

/* ------------------------------------------------------------------ 기록 */

export interface RunRecord {
  readonly runId: string;
  readonly finishedAtDay: number;
  readonly status: RunState["status"];
  readonly season: RunState["season"];
  readonly difficulty: string;
  readonly ending: EndingResult | null;
  readonly discipline: number;
  readonly hidden: readonly string[];
  readonly failedAt: string | null;
  readonly members: readonly {
    readonly name: string;
    readonly role: string;
    readonly rank: string;
    readonly serviceScore: number;
    readonly evacuations: number;
    /** 하달 장부 — 누가 몇 건을 넘겼고 받았는지 (QST-05) */
    readonly delegationsGiven: number;
    readonly delegationsReceived: number;
  }[];
}

/**
 * 런이 끝나면 남는 기록. 리더보드와 하달 장부가 이 한 덩어리에서 나온다.
 *
 * 하달 장부를 기록에 함께 굳히는 이유는 6.2에 있다 — 런 종료 시 공개되는 장부가
 * "누가 누구에게 몇 건을 넘겼는지"를 뒤바뀐 계급과 나란히 보여주는 장치이기 때문이다.
 */
export function summarizeRun(state: RunState): RunRecord {
  const ledger = delegationLedgerSummary(state);
  const last = state.judgements[state.judgements.length - 1];

  return {
    runId: state.runId,
    finishedAtDay: state.day,
    status: state.status,
    season: state.season,
    difficulty: state.config.difficulty,
    ending: state.status === "cleared" ? resolveEnding(state) : null,
    discipline: Math.round(state.discipline),
    hidden: [...state.hiddenUnlocked],
    failedAt: last && !last.passed ? last.failedAt : null,
    members: state.members
      .filter((m) => m.presence !== "npcVacant")
      .map((member) => {
        const entry = ledger.find((l) => l.memberId === member.id);
        return {
          name: member.name,
          role: member.role,
          rank: member.rank,
          serviceScore: member.serviceScore,
          evacuations: member.evacuations,
          delegationsGiven: entry?.given ?? 0,
          delegationsReceived: entry?.received ?? 0,
        };
      }),
  };
}
