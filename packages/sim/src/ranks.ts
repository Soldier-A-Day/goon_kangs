import rankTable from "../data/ranks.json";
import {
  RANK_ORDER,
  type Effect,
  type Grade,
  type Member,
  type Rank,
  type RunState,
} from "./types.js";

const GAINS = rankTable.gains;
const PENALTIES = rankTable.penalties;
const MULTIPLIER = rankTable.penaltyMultiplier as Record<Rank, number>;
const GRANTS = rankTable.grants as Record<Rank, readonly string[]>;

export interface ReviewDef {
  readonly day: number;
  readonly rank: Rank;
  readonly require: number;
}

export const REVIEWS = rankTable.reviews as readonly ReviewDef[];
export const RETRIES = rankTable.retries as readonly { day: number; rank: Rank }[];
export const RETRY_DISCOUNT = rankTable.retryDiscount;

export function grantsFor(rank: Rank): readonly string[] {
  return GRANTS[rank] ?? [];
}

/**
 * 감점에만 곱해지는 계급 배율.
 *
 * 초안은 "계급이 오르면 필수 퀘스트 +1"이었으나 철회됐다 — 4인 분대에서 전원 +1이면
 * 하루 판정 대상이 17 → 21건으로 늘어 연대 책임 구조에서 완주율이 급락하고,
 * 성장이 처벌로 체감된다. 대신 같은 실수의 대가만 커진다.
 */
export function penaltyMultiplier(rank: Rank): number {
  return MULTIPLIER[rank] ?? 1;
}

export function award(member: Member, amount: number): void {
  member.serviceScore += amount;
}

/**
 * 선택 퀘스트 한 건의 몫.
 *
 * 등급이 없으면 B로 친다 — 판이 붙지 않은 일과(합동 · 훈련 체크포인트)와 NPC
 * 대리가 끝낸 일과가 여기 해당한다. 0으로 두면 판을 아직 안 만든 원형이 승급을
 * 막게 되고, A로 두면 대리에게 맡기는 편이 이득이 된다.
 */
export function gradeGain(grade: Grade | null): number {
  return GRADE_GAINS[grade ?? "B"];
}

const GRADE_GAINS = rankTable.gains.optionalRoleQuestByGrade as Record<Grade, number>;

/**
 * 감점은 계급 배율을 태워서 물린다.
 * 배율이 소수라 그대로 더하면 부동소수 오차가 쌓이므로 정수로 맞춘다.
 */
export function penalize(member: Member, amount: number): void {
  member.serviceScore += Math.round(amount * penaltyMultiplier(member.rank));
}

/**
 * 표 13-1 하루치 복무 점수 적립. 점호 판정 **직전**에 돈다.
 *
 * 원래는 "안 해도 되는데 한 것"만 셌다 — 필수 완수는 살아있는 플레이어가 예외 없이
 * 100%라 변별력이 0이었기 때문이다. **등급이 그 문제를 푼다**: A로 한 필수와 C로 한
 * 필수는 같지 않다. 그래서 필수도 A일 때만 조금 얹는다.
 *
 * 다만 **새 점수원을 만들지는 않았다.** 퀘스트마다 등급 점수를 얹으면 1인당 하루
 * 6~8건이라 획득이 기존 경제의 2~3배가 되고, 심사 요구치(18 · 70 · 150)가 통째로
 * 무의미해진다. 선택 퀘스트의 2점을 A3·B2·C1로 갈아끼운 것이 전부다.
 */
export function applyDailyServiceScore(state: RunState): void {
  const jointFlawless = state.quests.some(
    (q) => q.kind === "joint" && q.status === "done",
  );
  // 훈련은 70%로도 생존하므로(9.0) 100% 완주는 순수 초과 성과다
  const trainingByMember = new Map<string, { total: number; done: number }>();
  for (const quest of state.quests) {
    if (quest.training === null || quest.ownerId === null) continue;
    const entry = trainingByMember.get(quest.ownerId) ?? { total: 0, done: 0 };
    entry.total += 1;
    if (quest.status === "done") entry.done += 1;
    trainingByMember.set(quest.ownerId, entry);
  }

  for (const member of state.members) {
    if (member.presence !== "player") continue;

    const mine = state.quests.filter(
      (q) =>
        q.ownerId === member.id &&
        // 밥을 먹었다고 승급하지는 않는다
        q.kind !== "care" &&
        q.status === "done" &&
        q.delegatedFrom === null,
    );

    for (const quest of mine) {
      // 선택 보직 퀘스트 — 가장 기본적인 "안 해도 되는 일"
      if (!quest.required) {
        award(member, gradeGain(quest.grade));
        continue;
      }
      // 필수는 해도 본전이다. 다만 깨끗하게 했으면 그만큼만 얹는다
      if (quest.grade === "A") award(member, GAINS.requiredQuestGradeA);
    }

    if (jointFlawless) award(member, GAINS.jointFlawless);

    const training = trainingByMember.get(member.id);
    if (training && training.total > 0 && training.done === training.total) {
      award(member, GAINS.trainingPerfect);
    }

    // 회복량 50% 손실을 감수한 대가 (COND-02)
    if (state.nightGuardIds.includes(member.id)) {
      award(member, GAINS.nightGuard);
    }
  }

  // 돌발은 선착 개인이지만 소유자가 없으므로 분대 공유로 준다
  const surprises = state.quests.filter(
    (q) => q.kind === "surprise" && q.status === "done",
  ).length;
  if (surprises > 0) {
    for (const member of state.members) {
      if (member.presence === "player") {
        award(member, surprises * GAINS.surpriseSuccess);
      }
    }
  }

  // 대리 상태 유지는 그 자리의 점수 성장을 멈추는 것으로 끝나지 않고 깎는다
  for (const member of state.members) {
    if (member.presence === "npcEvac" || member.presence === "npcLeave") {
      penalize(member, PENALTIES.npcProxyPerDay);
    }
  }
}

/** 후송의 개인 비용 (표 13-1) */
export function penalizeEvacuation(member: Member): void {
  penalize(member, PENALTIES.evacuated);
}

export interface ReviewOutcome {
  readonly memberId: string;
  readonly promoted: boolean;
  readonly from: Rank;
  readonly to: Rank;
  readonly score: number;
  readonly require: number;
  readonly trustBonus: number;
}

/**
 * RANK-01 승급 심사.
 *
 * 심사 시점은 복무일수가 열고(D-03/09/15), 통과 여부는 누적 복무 점수가 결정한다.
 * 미달은 게임오버가 아니라 보류이며, 다음 정비일에 요구치 −10%로 재심사를 받는다.
 * 점수 내역은 전원에게 공개된다 — 무임승차가 드러나는 것이 이 시스템의 사회적 압력 장치다.
 */
export function runReview(state: RunState, effects: Effect[]): void {
  const review = REVIEWS.find((entry) => entry.day === state.day);
  const retry = RETRIES.find((entry) => entry.day === state.day);
  if (!review && !retry) return;

  const targetRank = review?.rank ?? retry?.rank;
  if (!targetRank) return;

  const base = REVIEWS.find((entry) => entry.rank === targetRank)?.require ?? 0;
  const require = retry ? Math.round(base * (1 - RETRY_DISCOUNT)) : base;

  const targetIndex = RANK_ORDER.indexOf(targetRank);
  const outcomes: ReviewOutcome[] = [];

  for (const member of state.members) {
    if (member.presence !== "player") continue;
    // 이미 그 계급 이상이면 심사 대상이 아니다
    if (RANK_ORDER.indexOf(member.rank) >= targetIndex) continue;

    // 저녁 개인정비 시간을 대화에 쓴 선택을 심사 시점에 정산한다
    const trustBonus = Math.min(
      GAINS.trustMax,
      Math.round(
        (state.trust.platoonLeader + state.trust.assistant + state.trust.sergeantMajor) /
          GAINS.trustDivisor,
      ),
    );
    const score = member.serviceScore + trustBonus;
    const promoted = score >= require;
    const from = member.rank;

    if (promoted) member.rank = targetRank;

    outcomes.push({
      memberId: member.id,
      promoted,
      from,
      to: promoted ? targetRank : from,
      score,
      require,
      trustBonus,
    });
  }

  if (outcomes.length === 0) return;

  effects.push({
    type: "rankReviewed",
    day: state.day,
    isRetry: Boolean(retry) && !review,
    require,
    outcomes,
  });
}

/** 심사일인가 — UI가 미리 알려주는 용도 */
export function reviewDayFor(day: number): { rank: Rank; isRetry: boolean } | null {
  const review = REVIEWS.find((entry) => entry.day === day);
  if (review) return { rank: review.rank, isRetry: false };
  const retry = RETRIES.find((entry) => entry.day === day);
  if (retry) return { rank: retry.rank, isRetry: true };
  return null;
}
