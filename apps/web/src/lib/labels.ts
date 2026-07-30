import type { QuickCommand, Snapshot } from "@sad/protocol";

type Zone = Snapshot["members"][number]["zone"];
type TempBand = Snapshot["weather"]["band"];
type Rank = Snapshot["members"][number]["rank"];
type QuestKind = Snapshot["quests"][number]["kind"];

export const ZONE_LABELS: Record<Zone, string> = {
  barracks: "생활관",
  drillGround: "연병장",
  storage: "창고",
  messHall: "식당",
  guardPost: "초소",
  trainingField: "훈련장",
  infirmary: "의무실",
  boilerRoom: "보일러실",
};

export const ZONE_ORDER: Zone[] = [
  "barracks",
  "messHall",
  "infirmary",
  "drillGround",
  "storage",
  "boilerRoom",
  "guardPost",
  "trainingField",
];

export const PHASE_LABELS: Record<Snapshot["phase"]["id"], string> = {
  reveille: "기상 · 점검",
  morning: "오전 일과",
  lunch: "중식 · 휴식",
  afternoon: "오후 일과",
  personal: "석식 · 개인정비",
  rollcall: "점호 · 판정",
};

export const RANK_LABELS: Record<Rank, string> = {
  private: "이병",
  pfc: "일병",
  corporal: "상병",
  sergeant: "병장",
};

export const KIND_LABELS: Record<QuestKind, string> = {
  role: "보직",
  chore: "공통",
  joint: "합동",
  surprise: "돌발",
  hidden: "히든",
};

/** 온도 밴드는 색 외에 아이콘·텍스트로도 구분한다 — 색각 이상 대응 (15.0 접근성) */
export const BAND_MARKS: Record<TempBand, { mark: string; tone: string }> = {
  extremeCold: { mark: "❄❄", tone: "var(--cold)" },
  cold: { mark: "❄", tone: "var(--cold)" },
  normal: { mark: "◦", tone: "var(--ink-2)" },
  warm: { mark: "☀", tone: "var(--heat)" },
  hot: { mark: "☀☀", tone: "var(--heat)" },
  extremeHot: { mark: "☀☀☀", tone: "var(--heat)" },
};

/** 8.0 퀵 커맨드 8슬롯 — 타이핑 없이 18일을 완주할 수 있어야 한다 */
export const QUICK_COMMANDS: { id: QuickCommand; label: string; key: string }[] = [
  { id: "assemble", label: "집합", key: "1" },
  { id: "wait", label: "대기", key: "2" },
  { id: "allClear", label: "이상 없음", key: "3" },
  { id: "needHelp", label: "지원 요청", key: "4" },
  { id: "done", label: "완료", key: "5" },
  { id: "cannot", label: "못 함", key: "6" },
  { id: "overHere", label: "여기", key: "7" },
  { id: "hurry", label: "서둘러", key: "8" },
];

/** 11.0 보급 품목. sim의 data/supply.json과 같은 값이다 (web은 sim을 참조할 수 없다) */
export const ITEM_LABELS: Record<string, string> = {
  combatUniform: "전투복",
  combatBoots: "전투화",
  thermalLiner: "방한 내피",
  gloves: "장갑",
  canteen: "수통",
  parka: "방상외피",
  winterBoots: "방한화",
  insulatedCanteen: "보온 수통",
  canteen2: "수통 추가",
  coolingTowel: "냉각 타월",
  icePack: "얼음팩",
  medkit: "의약품",
  rations: "전투식량",
};

export const STAT_LABELS = [
  { key: "stamina", label: "체력", inverted: false },
  { key: "hydration", label: "수분", inverted: false },
  { key: "fatigue", label: "피로", inverted: true },
  { key: "mental", label: "정신력", inverted: false },
  { key: "hygiene", label: "청결", inverted: false },
  { key: "satiety", label: "포만감", inverted: false },
] as const;

export function formatSeconds(ms: number): string {
  const total = Math.max(0, Math.round(ms / 1000));
  return `${Math.floor(total / 60)}:${String(total % 60).padStart(2, "0")}`;
}
