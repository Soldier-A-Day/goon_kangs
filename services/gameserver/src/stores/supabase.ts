import { createClient, type SupabaseClient } from "@supabase/supabase-js";
import type { RunRecord } from "@sad/sim";
import type { RecordQuery, RecordStore } from "../persistence.js";

/** sql/001_records.sql 의 runs 테이블 */
interface RunRow {
  run_id: string;
  finished_at_day: number;
  status: RunRecord["status"];
  season: RunRecord["season"];
  difficulty: string;
  ending_id: string | null;
  ending_label: string | null;
  discipline: number;
  hidden: string[];
  failed_at: string | null;
}

interface MemberRow {
  run_id: string;
  name: string;
  role: string;
  rank: string;
  service_score: number;
  evacuations: number;
  delegations_given: number;
  delegations_received: number;
}

/**
 * 영속 기록 — Supabase(Postgres) 어댑터.
 *
 * 게임서버는 service_role 키로 쓰므로 RLS를 우회한다. 웹 셸은 anon 키로 **읽기만** 한다 —
 * SELECT 정책만 만들어 뒀기 때문에 그쪽에서는 쓸 수 없다(sql/001_records.sql).
 */
export class SupabaseRecordStore implements RecordStore {
  constructor(private readonly client: SupabaseClient) {}

  async append(record: RunRecord): Promise<void> {
    const { error } = await this.client.from("runs").insert({
      run_id: record.runId,
      finished_at_day: record.finishedAtDay,
      status: record.status,
      season: record.season,
      difficulty: record.difficulty,
      ending_id: record.ending?.id ?? null,
      ending_label: record.ending?.label ?? null,
      discipline: record.discipline,
      hidden: [...record.hidden],
      failed_at: record.failedAt,
    } satisfies RunRow);

    // 기록을 못 남기는 것이 런을 망치면 안 된다. 로그만 남기고 넘어간다 —
    // 이 시점의 런은 이미 끝났고 플레이어가 할 수 있는 일도 없다.
    if (error) {
      console.error("[records] 런 기록 저장 실패", record.runId, error.message);
      return;
    }

    const members: MemberRow[] = record.members.map((member) => ({
      run_id: record.runId,
      name: member.name,
      role: member.role,
      rank: member.rank,
      service_score: member.serviceScore,
      evacuations: member.evacuations,
      delegations_given: member.delegationsGiven,
      delegations_received: member.delegationsReceived,
    }));

    const { error: memberError } = await this.client.from("run_members").insert(members);
    if (memberError) {
      console.error("[records] 분대원 저장 실패", record.runId, memberError.message);
    }
  }

  async list(query: RecordQuery = {}): Promise<RunRecord[]> {
    let builder = this.client
      .from("runs")
      .select("*, run_members(*)")
      .order("created_at", { ascending: false })
      .limit(query.limit ?? 50);

    if (query.status) builder = builder.eq("status", query.status);

    const { data, error } = await builder;
    if (error || !data) {
      console.error("[records] 조회 실패", error?.message);
      return [];
    }
    return data.map(toRecord);
  }

  async get(runId: string): Promise<RunRecord | null> {
    const { data, error } = await this.client
      .from("runs")
      .select("*, run_members(*)")
      .eq("run_id", runId)
      .maybeSingle();

    if (error || !data) return null;
    return toRecord(data);
  }
}

function toRecord(row: RunRow & { run_members?: MemberRow[] }): RunRecord {
  return {
    runId: row.run_id,
    finishedAtDay: row.finished_at_day,
    status: row.status,
    season: row.season,
    difficulty: row.difficulty,
    ending: row.ending_id
      ? {
          id: row.ending_id as never,
          label: row.ending_label ?? row.ending_id,
          // 아래는 기록에 굳히지 않은 파생값이라 표시용 기본값을 채운다
          hiddenCount: row.hidden.length,
          finalRank: "private",
          discipline: row.discipline,
          trustTotal: 0,
          evacuations: 0,
        }
      : null,
    discipline: row.discipline,
    hidden: row.hidden,
    failedAt: row.failed_at,
    members: (row.run_members ?? []).map((member) => ({
      name: member.name,
      role: member.role as never,
      rank: member.rank as never,
      serviceScore: member.service_score,
      evacuations: member.evacuations,
      delegationsGiven: member.delegations_given,
      delegationsReceived: member.delegations_received,
    })),
  };
}

/**
 * 서버 전용 클라이언트. service_role 키는 절대 브라우저로 나가면 안 되므로
 * `NEXT_PUBLIC_` 접두사가 붙은 값은 여기서 쓰지 않는다.
 */
export function supabaseFromEnv(): SupabaseClient | null {
  const url = process.env.SUPABASE_URL;
  const key = process.env.SUPABASE_SERVICE_ROLE_KEY ?? process.env.SUPABASE_SECRET_KEY;
  if (!url || !key) return null;
  return createClient(url, key, { auth: { persistSession: false } });
}
