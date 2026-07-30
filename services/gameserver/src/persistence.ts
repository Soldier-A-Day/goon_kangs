import { deserializeRun, serializeRun, type RunRecord, type RunState } from "@sad/sim";

/**
 * 저장소 경계.
 *
 * 17.0은 "런 상태를 Redis에 스냅샷, 전원 이탈 시 24시간 보관"이라고 정한다.
 * 지금은 인메모리 구현이지만 **이 인터페이스 뒤에** Redis 어댑터가 그대로 들어간다 —
 * 서버 코드는 어느 쪽인지 알 필요가 없다.
 */
export interface Persistence {
  saveRun(code: string, state: RunState): Promise<void>;
  loadRun(code: string): Promise<RunState | null>;
  dropRun(code: string): Promise<void>;
  appendRecord(record: RunRecord): Promise<void>;
  listRecords(limit?: number): Promise<RunRecord[]>;
  getRecord(runId: string): Promise<RunRecord | null>;
}

/** 17.0 — 전원 이탈 시 24시간 보관 */
export const RUN_TTL_MS = 24 * 60 * 60 * 1000;

interface StoredRun {
  readonly raw: string;
  readonly expiresAt: number;
}

export class MemoryPersistence implements Persistence {
  private readonly runs = new Map<string, StoredRun>();
  private readonly records: RunRecord[] = [];

  /** 시계를 주입받는다 — 테스트가 만료를 실제로 기다리지 않아도 되게 */
  constructor(private readonly now: () => number = () => Date.now()) {}

  async saveRun(code: string, state: RunState): Promise<void> {
    this.runs.set(code, {
      raw: serializeRun(state),
      expiresAt: this.now() + RUN_TTL_MS,
    });
  }

  async loadRun(code: string): Promise<RunState | null> {
    const stored = this.runs.get(code);
    if (!stored) return null;
    if (stored.expiresAt <= this.now()) {
      this.runs.delete(code);
      return null;
    }
    return deserializeRun(stored.raw);
  }

  async dropRun(code: string): Promise<void> {
    this.runs.delete(code);
  }

  async appendRecord(record: RunRecord): Promise<void> {
    this.records.unshift(record);
  }

  async listRecords(limit = 50): Promise<RunRecord[]> {
    return this.records.slice(0, limit);
  }

  async getRecord(runId: string): Promise<RunRecord | null> {
    return this.records.find((r) => r.runId === runId) ?? null;
  }

  /** 만료된 스냅샷 청소 */
  sweep(): void {
    const now = this.now();
    for (const [code, stored] of this.runs) {
      if (stored.expiresAt <= now) this.runs.delete(code);
    }
  }

  get runCount(): number {
    return this.runs.size;
  }
}
