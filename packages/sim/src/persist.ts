import { delegationLedgerSummary } from "./delegation.js";
import { resolveEnding, type EndingResult } from "./hidden.js";
import { SUPPLY_START } from "./supply.js";
import type { ConditionBreach, RunState, TempBand } from "./types.js";
import { weatherFor } from "./weather.js";

/**
 * 저장 포맷 버전. 구조가 바뀌면 올리고, 맞지 않는 스냅샷은 복구하지 않는다.
 *
 * 2 — F-1이 `RunState.elapsedRealMs`를 새 필수 필드로 추가했다. 옛 스냅샷에는
 * 이 필드가 없어 복구하면 `undefined`로 들어오고, `applyTick`의 `+=`가 그걸
 * 곧장 `NaN`으로 굳혀 해금이 영영 안 열리는 조용한 손상이 된다 — "반쯤 맞는
 * 상태로 되살리는 것이 가장 나쁘다"는 원칙 그대로, 버전을 올려 옛 스냅샷은
 * 복구하지 않고 새로 시작하게 한다.
 *
 * 3 — C-1이 `RunState.firstConditionBreach`를 새 필수 필드로 추가했다. 옛
 * 스냅샷에는 이 필드가 없어 복구하면 `undefined`로 들어오고, `applyJudgement`의
 * 속성 접근이 그 자리에서 터진다. 같은 이유로 버전을 올린다.
 */
export const SAVE_VERSION = 3;

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
  /** 결정타 조건의 수치·시점 — "다음 판 전략"을 말하는 데 쓴다 (C-1) */
  readonly firstFailure: ConditionBreach | null;
  /** "한파 속 물자 부족 D-5 완주" 형식 한 줄. 성공·실패 어느 쪽에도 붙는다 (C-1) */
  readonly headline: string;
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

/** 헤드라인에 얹는 날씨 밴드 표현 — 평시·온난은 정상 범주라 굳이 외치지 않는다 */
const WEATHER_HEADLINE: Partial<Record<TempBand, string>> = {
  extremeCold: "혹한",
  cold: "한파",
  hot: "무더위",
  extremeHot: "폭염",
};

/** 시작 보급(표 11.0, 12)의 1/4 이하로 남으면 "물자 부족"이라 부른다 */
const SUPPLY_LOW_THRESHOLD = Math.floor(SUPPLY_START / 4);

const RESULT_HEADLINE: Record<RunState["status"], string> = {
  cleared: "완주",
  discharged: "퇴소",
  disbanded: "해산",
  running: "진행 중",
};

/**
 * 런 요약 헤드라인 — "한파 속 물자 부족 D-5 완주" 형식 (C-1).
 *
 * 재료는 날씨 밴드 우세 · 보급 상태 · 도달 일차 · 결과. 성공·실패 어느 쪽에도 같은
 * 함수를 쓴다 — 실패 전용 장치가 아니다.
 *
 * 날씨는 따로 기록해 두지 않아도 다시 구할 수 있다 — `weatherFor`가 (seed, day,
 * season)만으로 그날 날씨를 되짚는 순수 함수이기 때문이다(`weather.ts`의 결정론
 * 주석 참고). 그래서 RunState에 일별 로그를 새로 얹지 않는다.
 */
export function buildHeadline(state: RunState): string {
  const bandCounts = new Map<TempBand, number>();
  for (let day = 1; day <= state.day; day += 1) {
    const band = weatherFor(state.seed, day, state.season).band;
    bandCounts.set(band, (bandCounts.get(band) ?? 0) + 1);
  }

  let dominantBand: TempBand | null = null;
  let dominantCount = 0;
  for (const [band, count] of bandCounts) {
    if (WEATHER_HEADLINE[band] && count > dominantCount) {
      dominantBand = band;
      dominantCount = count;
    }
  }

  const bits: string[] = [];
  if (dominantBand) bits.push(`${WEATHER_HEADLINE[dominantBand]} 속`);
  if (state.supplyPoints <= SUPPLY_LOW_THRESHOLD) bits.push("물자 부족");
  bits.push(`D-${state.day}`);
  bits.push(RESULT_HEADLINE[state.status]);

  return bits.join(" ");
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
  const failedAt = last && !last.passed ? last.failedAt : null;

  return {
    runId: state.runId,
    finishedAtDay: state.day,
    status: state.status,
    season: state.season,
    difficulty: state.config.difficulty,
    ending: state.status === "cleared" ? resolveEnding(state) : null,
    discipline: Math.round(state.discipline),
    hidden: [...state.hiddenUnlocked],
    failedAt,
    firstFailure: failedAt ? (state.firstConditionBreach[failedAt] ?? null) : null,
    headline: buildHeadline(state),
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
