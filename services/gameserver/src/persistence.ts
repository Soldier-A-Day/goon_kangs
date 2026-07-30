import { deserializeRun, serializeRun, type RunRecord, type RunState } from "@sad/sim";

/**
 * 저장소 경계 — 기획서 표 18-1이 나눈 선을 그대로 따른다.
 *
 * > 상태 저장 | 런 스냅샷 / 영속 기록 | Redis / Postgres |
 * > **휘발성 런 상태와 영속 메타 진행 분리**
 *
 * 둘은 성격이 정반대다. 하나로 묶으면 기록에 만료가 붙거나 스냅샷에 스키마가 붙는다.
 *
 * | | 런 스냅샷 | 영속 기록 |
 * |---|---|---|
 * | 수명 | 24시간 (17.0) | 영원 |
 * | 접근 | 코드 하나로 읽고 쓰기 | 정렬·집계·조회 |
 * | 쓰기 빈도 | 10초마다 | 런당 1회 |
 * | 대상 | Redis | Postgres |
 */

/* --------------------------------------------------- 휘발성 — 런 스냅샷 */

/** 17.0 — 전원 이탈 시 24시간 보관 */
export const RUN_TTL_MS = 24 * 60 * 60 * 1000;

/**
 * 이어하기용 스냅샷 저장소. 키-값 하나가 전부이며 TTL이 만료를 대신한다.
 * Redis 어댑터가 이 자리에 그대로 들어간다.
 */
export interface RunSnapshotStore {
  save(code: string, state: RunState): Promise<void>;
  load(code: string): Promise<RunState | null>;
  drop(code: string): Promise<void>;
}

/* ----------------------------------------------------- 영속 — 기록 */

export interface RecordQuery {
  readonly limit?: number;
  /** 특정 결과만 — 리더보드가 전역한 런만 보고 싶을 때 */
  readonly status?: RunRecord["status"];
}

/**
 * 기록·리더보드·하달 장부. 만료가 없고 조회 조건이 붙는다 — 관계형 저장소의 자리다.
 * 13.0의 런 외부 성장(기록·해금)도 결국 여기에 쌓인다.
 */
export interface RecordStore {
  append(record: RunRecord): Promise<void>;
  list(query?: RecordQuery): Promise<RunRecord[]>;
  get(runId: string): Promise<RunRecord | null>;
}

/* ------------------------------------------------------------ 인메모리 */

interface StoredSnapshot {
  readonly raw: string;
  readonly expiresAt: number;
}

/**
 * 개발·테스트용 스냅샷 저장소.
 *
 * 직렬화를 실제로 태우는 것이 중요하다 — 그래야 RunState가 JSON 왕복을 견디는지가
 * 여기서도 계속 확인되고, Redis로 바꿔도 같은 경로를 지난다.
 */
export class MemoryRunSnapshotStore implements RunSnapshotStore {
  private readonly runs = new Map<string, StoredSnapshot>();

  /** 시계를 주입받는다 — 테스트가 24시간을 실제로 기다리지 않아도 되게 */
  constructor(private readonly now: () => number = () => Date.now()) {}

  async save(code: string, state: RunState): Promise<void> {
    this.runs.set(code, {
      raw: serializeRun(state),
      expiresAt: this.now() + RUN_TTL_MS,
    });
  }

  async load(code: string): Promise<RunState | null> {
    const stored = this.runs.get(code);
    if (!stored) return null;
    if (stored.expiresAt <= this.now()) {
      this.runs.delete(code);
      return null;
    }
    return deserializeRun(stored.raw);
  }

  async drop(code: string): Promise<void> {
    this.runs.delete(code);
  }

  sweep(): void {
    const now = this.now();
    for (const [code, stored] of this.runs) {
      if (stored.expiresAt <= now) this.runs.delete(code);
    }
  }

  get size(): number {
    return this.runs.size;
  }
}

/** 개발·테스트용 기록 저장소. 프로세스가 죽으면 사라지므로 배포에는 쓰지 않는다. */
export class MemoryRecordStore implements RecordStore {
  private readonly records: RunRecord[] = [];

  async append(record: RunRecord): Promise<void> {
    this.records.unshift(record);
  }

  async list(query: RecordQuery = {}): Promise<RunRecord[]> {
    const filtered = query.status
      ? this.records.filter((r) => r.status === query.status)
      : this.records;
    return filtered.slice(0, query.limit ?? 50);
  }

  async get(runId: string): Promise<RunRecord | null> {
    return this.records.find((r) => r.runId === runId) ?? null;
  }
}

/** 서버가 들고 다니는 저장소 묶음 */
export interface Storage {
  readonly snapshots: RunSnapshotStore;
  readonly records: RecordStore;
}

export function memoryStorage(now?: () => number): Storage {
  return {
    snapshots: new MemoryRunSnapshotStore(now),
    records: new MemoryRecordStore(),
  };
}

/**
 * 환경변수가 있으면 실제 저장소, 없으면 인메모리.
 *
 * 로컬 개발과 테스트는 DB 없이 돌아가야 한다 — 저장소를 붙였다고 `docker compose`가
 * 필요해지면 진입 장벽만 올라간다. 어느 쪽이 붙었는지는 로그로 남긴다.
 */
export async function storageFromEnv(): Promise<Storage> {
  const { upstashFromEnv, UpstashRunSnapshotStore } = await import("./stores/upstash.js");
  const { supabaseFromEnv, SupabaseRecordStore } = await import("./stores/supabase.js");

  const redis = upstashFromEnv();
  const supabase = supabaseFromEnv();

  const snapshots = redis
    ? new UpstashRunSnapshotStore(redis)
    : new MemoryRunSnapshotStore();
  const records = supabase ? new SupabaseRecordStore(supabase) : new MemoryRecordStore();

  console.log(
    `[storage] 스냅샷=${redis ? "upstash" : "메모리"} · 기록=${supabase ? "supabase" : "메모리"}`,
  );

  return { snapshots, records };
}
