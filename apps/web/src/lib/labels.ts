import type { QuickCommand, Snapshot } from "@sad/protocol";

type Zone = Snapshot["members"][number]["zone"];
type TempBand = Snapshot["weather"]["band"];
type Rank = Snapshot["members"][number]["rank"];
type QuestKind = Snapshot["quests"][number]["kind"];

/**
 * 구역 이름 — **방 하나가 곧 구역이다.**
 *
 * 정의는 `packages/sim/data/zones.json`이 소유한다. 여기 있는 것은 표시용
 * 이름뿐이고, 빠뜨리면 타입 검사가 잡는다.
 */
export const ZONE_LABELS: Record<Zone, string> = {
  Z01: "생활관 (1분대)",
  Z01b: "생활관 (2분대)",
  Z01c: "생활관 (3분대)",
  Z02: "생활관동 복도",
  Z03: "공용 세면장 · 샤워장",
  Z13: "세탁 · 건조실",
  Z16: "사지방",
  Z17: "체력단련장",
  Z09: "무기고",
  Z20: "급양동 복도",
  Z07: "병사식당",
  Z07b: "취사장",
  Z21: "지원동 복도",
  Z05: "의무실",
  Z06: "통신실",
  Z04: "행정반 · CP",
  Z22: "보급 · 정비동 복도",
  Z08: "보급 창고",
  Z10: "정비고 · 차량",
  Z11: "연병장",
  Z12: "초소 1 (북서)",
  Z12b: "초소 2 (남서)",
  Z18: "정문 위병소",
  Z19: "탄약고",
  Z14: "보일러실",
  Z50: "훈련장",
  TR01: "사격장",
  TR02: "화생방 제독소",
  TR03: "행군로",
  TR04: "숙영지",
  TR05: "혹한기 훈련장",
  TR06: "혹서기 급수 라인",
  TR07: "유격장 (기초)",
  TR08: "유격장 (종합)",
  TR09: "대민지원 (마을)",
  TR10: "합동 전술훈련장",
};

/** 익히는 순서 — 동 단위로 묶고 야외를 뒤에 둔다 */
export const ZONE_ORDER: Zone[] = [
  "Z01",
  "Z01b",
  "Z01c",
  "Z02",
  "Z03",
  "Z13",
  "Z16",
  "Z17",
  "Z09",
  "Z20",
  "Z07",
  "Z07b",
  "Z21",
  "Z05",
  "Z06",
  "Z04",
  "Z22",
  "Z08",
  "Z10",
  "Z11",
  "Z12",
  "Z12b",
  "Z18",
  "Z19",
  "Z14",
  "Z50",
  "TR01",
  "TR02",
  "TR03",
  "TR04",
  "TR05",
  "TR06",
  "TR07",
  "TR08",
  "TR09",
  "TR10",
];

/**
 * 동 단위 묶음 — 방이 26개라 낱개로 늘어놓으면 어느 것이 한 건물인지 읽히지 않는다.
 *
 * 순서는 `ZONE_ORDER`와 같고, 나누는 기준은 `zones.json`의 `buildingName`이다.
 */
export const ZONE_GROUPS: readonly { name: string; zones: Zone[] }[] = [
  { name: "생활관동", zones: ["Z01", "Z01b", "Z01c", "Z02", "Z03", "Z13", "Z16", "Z17", "Z09"] },
  { name: "급양동", zones: ["Z20", "Z07", "Z07b"] },
  { name: "지원동", zones: ["Z21", "Z05", "Z06", "Z04"] },
  { name: "보급 · 정비동", zones: ["Z22", "Z08", "Z10"] },
  { name: "야외", zones: ["Z11", "Z12", "Z12b", "Z18", "Z19", "Z14", "Z50"] },
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
  /** 7.0 회복 행동 — 밥·물·세면. 일과가 아니라서 판정에도 복무 점수에도 안 들어간다 */
  care: "회복",
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
