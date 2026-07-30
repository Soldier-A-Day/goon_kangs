import { createClient, type SupabaseClient } from "@supabase/supabase-js";

/**
 * 기록 조회 — 웹 셸이 Supabase를 직접 읽는다.
 *
 * 게임서버를 거치지 않는 이유는 두 가지다.
 * 1. `/records`와 `/ledger`가 **게임서버가 죽어 있어도 뜬다**. 기록은 이미 끝난 런의
 *    것이라 규칙 엔진이 관여할 일이 없다.
 * 2. 웹 셸은 규칙을 몰라야 한다(ARCH-02). 데이터만 읽는 화면이 게임서버에 의존할 이유가 없다.
 *
 * 쓰기는 게임서버만 한다 — 이 키로는 SELECT 정책밖에 통과하지 못한다
 * (services/gameserver/sql/001_records.sql).
 */

export interface RunRecord {
  readonly runId: string;
  readonly finishedAtDay: number;
  readonly status: "cleared" | "discharged" | "disbanded";
  readonly season: "cold" | "hot";
  readonly difficulty: string;
  readonly endingId: string | null;
  readonly endingLabel: string | null;
  readonly discipline: number;
  readonly hidden: readonly string[];
  readonly failedAt: string | null;
  readonly members: readonly {
    readonly name: string;
    readonly role: string;
    readonly rank: string;
    readonly serviceScore: number;
    readonly evacuations: number;
    readonly delegationsGiven: number;
    readonly delegationsReceived: number;
  }[];
}

interface RunRow {
  run_id: string;
  finished_at_day: number;
  status: RunRecord["status"];
  season: RunRecord["season"];
  difficulty: string;
  ending_id: string | null;
  ending_label: string | null;
  discipline: number;
  hidden: string[] | null;
  failed_at: string | null;
  run_members?: MemberRow[];
}

interface MemberRow {
  name: string;
  role: string;
  rank: string;
  service_score: number;
  evacuations: number;
  delegations_given: number;
  delegations_received: number;
}

/**
 * 지연 초기화 — 모듈 최상위에서 만들면 환경변수가 없는 빌드 시점에 터진다.
 * 공개 키만 쓴다. service_role 키는 절대 이 앱에 들어오지 않는다.
 */
function client(): SupabaseClient | null {
  const url = process.env.NEXT_PUBLIC_SUPABASE_URL;
  const key =
    process.env.NEXT_PUBLIC_SUPABASE_PUBLISHABLE_KEY ??
    process.env.NEXT_PUBLIC_SUPABASE_ANON_KEY;
  if (!url || !key) return null;
  return createClient(url, key, { auth: { persistSession: false } });
}

/** 저장소에 붙지 못한 것과 기록이 0건인 것은 화면에서 다르게 말해야 한다 */
export type RecordsResult =
  | { readonly ok: true; readonly records: RunRecord[] }
  | { readonly ok: false; readonly reason: "notConfigured" | "queryFailed" };

export async function fetchRecords(limit = 50): Promise<RecordsResult> {
  const supabase = client();
  if (!supabase) return { ok: false, reason: "notConfigured" };

  const { data, error } = await supabase
    .from("runs")
    .select("*, run_members(*)")
    .order("created_at", { ascending: false })
    .limit(limit);

  if (error || !data) return { ok: false, reason: "queryFailed" };
  return { ok: true, records: data.map(toRecord) };
}

export async function fetchRecord(runId: string): Promise<RunRecord | null> {
  const supabase = client();
  if (!supabase) return null;

  const { data, error } = await supabase
    .from("runs")
    .select("*, run_members(*)")
    .eq("run_id", runId)
    .maybeSingle();

  if (error || !data) return null;
  return toRecord(data);
}

function toRecord(row: RunRow): RunRecord {
  return {
    runId: row.run_id,
    finishedAtDay: row.finished_at_day,
    status: row.status,
    season: row.season,
    difficulty: row.difficulty,
    endingId: row.ending_id,
    endingLabel: row.ending_label,
    discipline: row.discipline,
    hidden: row.hidden ?? [],
    failedAt: row.failed_at,
    members: (row.run_members ?? []).map((member) => ({
      name: member.name,
      role: member.role,
      rank: member.rank,
      serviceScore: member.service_score,
      evacuations: member.evacuations,
      delegationsGiven: member.delegations_given,
      delegationsReceived: member.delegations_received,
    })),
  };
}

/* ------------------------------------------------------------------ 라벨 */

export const RANK_LABELS: Record<string, string> = {
  private: "이병",
  pfc: "일병",
  corporal: "상병",
  sergeant: "병장",
};

export const ENDING_LABELS: Record<string, string> = {
  exemplary: "모범 전역",
  record: "분대 기록 엔딩",
  barely: "간신히 전역",
  normal: "정상 전역",
};

export const HIDDEN_LABELS: Record<string, string> = {
  flawlessDay: "완전한 하루",
  coldSnap: "혹한을 넘다",
  hydrated: "쓰러지지 않는다",
  ledgerClean: "제 몫은 제가",
  proxyKing: "대행의 달인",
  steadfast: "우수분대",
};

/** 퇴소·해체는 엔딩이 없다 — 결과 한 줄을 만드는 곳을 한 군데로 모은다 */
export function outcomeLabel(record: RunRecord): string {
  if (record.endingId) return ENDING_LABELS[record.endingId] ?? record.endingLabel ?? "전역";
  return record.status === "disbanded" ? "분대 해체" : "퇴소";
}
