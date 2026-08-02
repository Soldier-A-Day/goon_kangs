import hiddenTable from "../data/hidden.json";
import { LEADER_RELIEF_LIMIT } from "./relief.js";
import { RANK_ORDER, type Effect, type Rank, type RunState } from "./types.js";

export interface HiddenQuestDef {
  readonly id: string;
  readonly label: string;
  readonly hint: string;
  /** 특정 보직이 있어야 열리는 것 — 그 보직이 NPC면 달성할 수 없다 */
  readonly role: string | null;
}

export const HIDDEN_QUESTS = hiddenTable.quests as readonly HiddenQuestDef[];
export const HIDDEN_FOR_RECORD_ENDING = hiddenTable.requiredForRecordEnding;

export type EndingId = "exemplary" | "record" | "barely" | "normal";

/**
 * 표 6-1 히든 퀘스트.
 *
 * 페널티가 없고 달성해도 그날의 판정을 바꾸지 않는다 — 순수하게 "해냈다"는 기록이며,
 * 4개를 모으면 분대 기록 엔딩이 열린다(META-02).
 *
 * 매일 밤 점호 직후에 검사한다. 조건은 전부 분대 단위다 — 엔딩이 분대 단위이기 때문이다.
 */
export function checkHiddenQuests(state: RunState, effects: Effect[]): void {
  for (const quest of HIDDEN_QUESTS) {
    if (state.hiddenUnlocked.includes(quest.id)) continue;

    // 보직 한정 히든은 그 보직에 사람이 있어야 한다
    if (quest.role !== null) {
      const staffed = state.members.some(
        (m) => m.role === quest.role && m.presence === "player",
      );
      if (!staffed) continue;
    }

    if (!isAchieved(quest.id, state)) continue;

    state.hiddenUnlocked.push(quest.id);
    effects.push({ type: "hiddenUnlocked", id: quest.id, label: quest.label });
  }
}

function isAchieved(id: string, state: RunState): boolean {
  switch (id) {
    case "flawlessDay":
      // 선택까지 하나도 남기지 않은 하루
      return (
        state.quests.length > 0 &&
        state.quests.every((q) => q.status === "done" || q.kind === "surprise")
      );

    case "coldSnap":
      return (
        state.weather.band === "extremeCold" &&
        state.quests.filter((q) => q.required).every((q) => q.status === "done")
      );

    case "hydrated": {
      const hotBands = ["hot", "extremeHot"];
      if (!hotBands.includes(state.weather.band)) return false;
      return state.members
        .filter((m) => m.presence === "player")
        .every((m) => m.stats.hydration >= 50);
    }

    case "ledgerClean":
      return state.day >= 10 && state.ledger.length === 0;

    case "proxyKing":
      return (
        state.ledger.filter((record) => record.outcome === "accepted").length >= 5 &&
        state.members.reduce((sum, m) => sum + m.serviceScore, 0) > 0
      );

    case "steadfast":
      return state.discipline >= 90;

    default:
      return false;
  }
}

export interface EndingResult {
  readonly id: EndingId;
  readonly label: string;
  readonly hiddenCount: number;
  readonly finalRank: Rank;
  readonly discipline: number;
  readonly trustTotal: number;
  readonly evacuations: number;
}

/**
 * META-02 엔딩 분기.
 *
 * 기준은 18일 무실패 여부 · 최종 계급 · 최종 군기 · 간부 신뢰도 합계 · 히든 달성 수다.
 * 모범 전역은 후송 0회와 구제 0회를 함께 요구한다 — 후송 1회로 '무실패' 배지가 깨지는 것이
 * 팀이 후송을 막을 실질 동기다(JDG-03).
 */
export function resolveEnding(state: RunState): EndingResult {
  const hiddenCount = state.hiddenUnlocked.length;
  const evacuations = state.members.reduce((sum, m) => sum + m.evacuations, 0);
  // B-4 — `judgement.reliefsUsed`는 이제 간부 몫만 센다. 분대장 몫(우선순위 지정)은
  // 판정 전에 이미 소모되므로 판정 기록에 안 남는다 — "구제 0회"를 참칭하지 않으려면
  // leaderReliefsRemaining이 시작값 그대로인지도 같이 봐야 한다.
  const officerReliefsUsed = state.judgements.reduce((sum, j) => sum + j.reliefsUsed, 0);
  const leaderReliefsUsed = LEADER_RELIEF_LIMIT - state.leaderReliefsRemaining;
  const reliefsUsed = officerReliefsUsed + leaderReliefsUsed;
  const trustTotal =
    state.trust.platoonLeader + state.trust.assistant + state.trust.sergeantMajor;

  const topRank = state.members
    .filter((m) => m.presence === "player")
    .reduce<Rank>(
      (best, m) =>
        RANK_ORDER.indexOf(m.rank) > RANK_ORDER.indexOf(best) ? m.rank : best,
      "private",
    );

  const base = {
    hiddenCount,
    finalRank: topRank,
    discipline: state.discipline,
    trustTotal,
    evacuations,
  };

  if (hiddenCount >= HIDDEN_FOR_RECORD_ENDING) {
    return { id: "record", label: "분대 기록 엔딩", ...base };
  }

  if (
    evacuations === 0 &&
    reliefsUsed === 0 &&
    state.discipline >= 80 &&
    topRank === "sergeant"
  ) {
    return { id: "exemplary", label: "모범 전역", ...base };
  }

  if (state.warnings > 0) {
    return { id: "barely", label: "간신히 전역", ...base };
  }

  return { id: "normal", label: "정상 전역", ...base };
}

/** 13.0 해금 — 보직을 완주하면 그 보직의 대체 특기가 열린다. 능력 총량은 같고 방식만 다르다. */
export function unlockedSpecialties(state: RunState): string[] {
  if (state.status !== "cleared") return [];
  return state.members
    .filter((m) => m.presence === "player")
    .map((m) => `${m.role}:alt`);
}
