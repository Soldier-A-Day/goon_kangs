import { Redis } from "@upstash/redis";
import { deserializeRun, serializeRun, type RunState } from "@sad/sim";
import { RUN_TTL_MS, type RunSnapshotStore } from "../persistence.js";

/**
 * 17.0 이어하기 — Upstash Redis 어댑터.
 *
 * TTL을 Redis에 맡긴다. 인메모리 구현은 만료 시각을 직접 들고 비교했지만,
 * 여기서는 `EX`가 그 일을 하고 만료된 키는 애초에 조회되지 않는다 —
 * "24시간 보관"이 규칙이 아니라 저장소의 성질이 된다.
 */
export class UpstashRunSnapshotStore implements RunSnapshotStore {
  private readonly ttlSeconds = Math.floor(RUN_TTL_MS / 1000);

  constructor(private readonly redis: Redis) {}

  private key(code: string): string {
    return `sad:run:${code}`;
  }

  async save(code: string, state: RunState): Promise<void> {
    // 직렬화 문자열을 그대로 넣는다. Upstash가 객체를 자동 직렬화해주지만
    // sim의 serializeRun을 거쳐야 저장 포맷 버전이 함께 실린다.
    await this.redis.set(this.key(code), serializeRun(state), {
      ex: this.ttlSeconds,
    });
  }

  async load(code: string): Promise<RunState | null> {
    // @upstash/redis는 값을 자동 역직렬화한다. 우리가 넣은 것은 JSON 문자열이라
    // 읽을 때 객체로 파싱되어 돌아온다 — 문자열만 받으면 항상 null이 된다.
    // 클라이언트 설정에 기대지 않고 양쪽을 다 받아준다.
    const raw = await this.redis.get<unknown>(this.key(code));
    if (raw === null || raw === undefined) return null;
    const json = typeof raw === "string" ? raw : JSON.stringify(raw);
    return deserializeRun(json);
  }

  async drop(code: string): Promise<void> {
    await this.redis.del(this.key(code));
  }
}

/**
 * Vercel 마켓플레이스는 `KV_REST_API_*`로 주입하고, Upstash에 직접 가입하면
 * `UPSTASH_REDIS_REST_*`로 준다. 둘 다 받아준다 — 어디서 발급했는지는 코드가 알 필요 없다.
 */
export function upstashFromEnv(): Redis | null {
  const url = process.env.KV_REST_API_URL ?? process.env.UPSTASH_REDIS_REST_URL;
  const token = process.env.KV_REST_API_TOKEN ?? process.env.UPSTASH_REDIS_REST_TOKEN;
  if (!url || !token) return null;
  return new Redis({ url, token });
}
