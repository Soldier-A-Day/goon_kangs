import supplyTable from "../data/supply.json";
import { disciplineBand } from "./discipline.js";
import type { Effect, Member, RunState, TempBand } from "./types.js";
import { bandRule, weatherFor } from "./weather.js";

const POINTS = supplyTable.points;
const WARMTH = supplyTable.clothingWarmth as unknown as Record<string, number>;

export interface SupplyItem {
  readonly id: string;
  readonly label: string;
  readonly cost: number;
  readonly slot: string;
  readonly warmth?: number;
  readonly staminaRecovery?: number;
  readonly satietyRecovery?: number;
}

export const CATALOG = supplyTable.catalog as readonly SupplyItem[];
export const SUPPLY_DAYS = supplyTable.supplyDays as readonly number[];
export const STARTING_KIT = supplyTable.startingKit.items as readonly string[];
export const SUPPLY_START = POINTS.start;

export function itemById(id: string): SupplyItem | undefined {
  return CATALOG.find((item) => item.id === id);
}

export function isSupplyDay(day: number): boolean {
  return SUPPLY_DAYS.includes(day);
}

/**
 * 5.0 피복 보온치 — 체감온도 공식의 한 항.
 *
 * 지금까지는 전투복 +2로 고정돼 있었다. 실제 착용 상태가 그 자리를 대신하면서
 * "방상외피를 청구했는가"가 곧바로 체감온도로 돌아온다 — 보급이 온도 시스템과 맞물리는 지점이다.
 */
export function clothingWarmth(member: Member): number {
  let total = 0;
  for (const id of member.inventory) {
    total += WARMTH[id] ?? 0;
  }
  return total;
}

/** 표 5-1 필수 장비를 갖췄는가 — 조건 D의 절반이다 */
export function missingGear(member: Member, band: TempBand): string[] {
  return bandRule(band).requiredGear.filter((id) => !member.inventory.includes(id));
}

export function hasRequiredGear(member: Member, band: TempBand): boolean {
  return missingGear(member, band).length === 0;
}

/**
 * SUP-01 포인트 획득. 하루 마감에 적립된다.
 * 일과 완수 정시성·돌발 성공·군기 보너스에서 나온다(11.0).
 */
export function accrueSupplyPoints(state: RunState): void {
  let gained = POINTS.perDayBase;

  const required = state.quests.filter((q) => q.required);
  if (required.length > 0 && required.every((q) => q.status === "done")) {
    gained += POINTS.onTimeBonus;
  }

  gained +=
    state.quests.filter((q) => q.kind === "surprise" && q.status === "done").length *
    POINTS.surpriseBonus;

  // 12.0 우수분대(80+)는 보급 포인트 +20%
  const bonus = disciplineBand(state.discipline).supplyBonus ?? 0;
  state.supplyPoints += Math.round(gained * (1 + bonus));
}

/**
 * 보급일 청구. 청구서는 행정병이 작성한다(11.0).
 *
 * 플레이어가 청구서를 내지 않았으면 표준 청구로 채운다 — 부대는 서류를 내면 필요한 것을 준다.
 * 다만 포인트는 항상 부족하므로 우선순위가 곧 판단이 된다: 내일 밴드를 아는 행정병만
 * 무엇을 먼저 사야 하는지 안다.
 */
export function runSupplyDay(state: RunState, effects: Effect[]): void {
  if (!isSupplyDay(state.day)) return;

  const requested = state.pendingClaim.length > 0 ? state.pendingClaim : standardClaim(state);
  const bought: string[] = [];

  for (const id of requested) {
    const item = itemById(id);
    if (!item) continue;
    if (state.supplyPoints < item.cost) continue;

    // 분대 공용이 아니라 개인 지급이므로 전원에게 돌아간다
    const needed = state.members.filter(
      (m) => m.presence !== "npcVacant" && !m.inventory.includes(id),
    );
    if (needed.length === 0) continue;

    state.supplyPoints -= item.cost;
    for (const member of needed) member.inventory.push(id);
    bought.push(id);
  }

  state.pendingClaim = [];
  effects.push({
    type: "supplyClaimed",
    day: state.day,
    items: bought,
    pointsLeft: state.supplyPoints,
  });
}

/**
 * 표준 청구 — 내일 밴드의 필수 장비를 먼저, 남으면 소모품을 채운다.
 *
 * 행정병이 예보를 보고 짜는 우선순위를 코드로 옮긴 것이며, 플레이어가 직접 청구서를 내면
 * 이 목록을 덮어쓴다. 포인트가 모자라면 앞에서부터 잘린다.
 */
export function standardClaim(state: RunState): string[] {
  const needed = new Set<string>();
  const bands = [state.weather.band];

  // 다음 보급일까지 버텨야 하므로 그 사이의 밴드를 미리 본다.
  // 예보를 아는 행정병이 실제로 하는 계산을 코드로 옮긴 것이다(5.0 · 11.0).
  const nextSupply = SUPPLY_DAYS.find((day) => day > state.day) ?? state.config.totalDays;
  for (let day = state.day + 1; day <= Math.min(nextSupply, state.config.totalDays); day += 1) {
    bands.push(weatherFor(state.seed, day, state.season).band);
  }

  for (const band of bands) {
    for (const id of bandRule(band).requiredGear) {
      const anyoneMissing = state.members.some(
        (m) => m.presence !== "npcVacant" && !m.inventory.includes(id),
      );
      if (anyoneMissing) needed.add(id);
    }
  }

  return [...needed, "medkit", "rations"];
}

/** 청구서 작성 — 행정병만 */
export function fileClaim(state: RunState, memberId: string, items: readonly string[]): boolean {
  const member = state.members.find((m) => m.id === memberId);
  if (!member || member.role !== "admin" || member.presence !== "player") return false;
  state.pendingClaim = items.filter((id) => itemById(id) !== undefined);
  return true;
}

/** 청구 목록의 총 비용 — UI가 예산을 보여주는 데 쓴다 */
export function claimCost(items: readonly string[]): number {
  return items.reduce((sum, id) => sum + (itemById(id)?.cost ?? 0), 0);
}
