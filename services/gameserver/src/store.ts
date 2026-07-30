import { randomBytes, randomInt } from "node:crypto";
import type { ServerMessage } from "@sad/protocol";
import type { RunConfig } from "@sad/sim";
import { Room, type Send } from "./room.js";
import { MemoryPersistence, type Persistence } from "./persistence.js";

/** 초대 코드는 6자리. 헷갈리는 글자(0/O, 1/I)는 뺀다 */
const CODE_ALPHABET = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

export interface Session {
  readonly code: string;
  readonly memberId: string;
}

/**
 * 런 상태 저장소. 지금은 인메모리이며, 이 인터페이스 뒤에 Redis 어댑터가 들어간다 —
 * 전원 이탈 시 24시간 보관 → 이어하기 (17.0 NET-01).
 */
export class RoomStore {
  private readonly rooms = new Map<string, Room>();
  private readonly sessions = new Map<string, Session>();

  constructor(
    private readonly send: Send,
    readonly persistence: Persistence = new MemoryPersistence(),
  ) {}

  createRoom(config: Partial<RunConfig>): Room {
    let code = generateCode();
    while (this.rooms.has(code)) code = generateCode();

    const room = new Room(code, config, randomInt(1, 2 ** 31 - 1), this.send);
    this.wire(room);
    this.rooms.set(code, room);
    return room;
  }

  /** 방이 스스로 저장·기록하도록 연결한다 — 방은 저장소가 무엇인지 알 필요가 없다 */
  private wire(room: Room): void {
    room.onPersist = (target) => {
      if (target.run) void this.persistence.saveRun(target.code, target.run);
    };
    room.onFinished = (target) => {
      const record = target.summarize();
      if (record) void this.persistence.appendRecord(record);
      // 끝난 런은 이어하기 대상이 아니다
      void this.persistence.dropRun(target.code);
    };
  }

  /**
   * 17.0 이어하기 — 서버가 재시작했거나 방이 청소된 뒤에도
   * 저장된 스냅샷이 있으면 같은 코드로 되살린다.
   */
  async resume(code: string): Promise<Room | null> {
    const upper = code.toUpperCase();
    const existing = this.rooms.get(upper);
    if (existing) return existing;

    const state = await this.persistence.loadRun(upper);
    if (!state) return null;

    const room = new Room(upper, state.config, state.seed, this.send);
    this.wire(room);
    room.restore(state);
    this.rooms.set(upper, room);
    return room;
  }

  get(code: string): Room | undefined {
    return this.rooms.get(code.toUpperCase());
  }

  issueToken(code: string, memberId: string): string {
    const token = randomBytes(24).toString("base64url");
    this.sessions.set(token, { code, memberId });
    return token;
  }

  /** 로비에서 발급한 단기 토큰으로만 WS에 붙을 수 있다 (ARCH-02 핸드오프) */
  resolve(token: string): { room: Room; session: Session } | null {
    const session = this.sessions.get(token);
    if (!session) return null;
    const room = this.rooms.get(session.code);
    if (!room) return null;
    return { room, session };
  }

  revoke(token: string): void {
    this.sessions.delete(token);
  }

  /**
   * 빈 방 청소.
   *
   * 시작 전 빈 방은 그냥 지운다. 시작한 뒤 끝난 방도 지우는데, 그 시점에는 이미
   * 기록이 남고 스냅샷이 정리된 상태다(wire 참고) — 진행 중인 방은 건드리지 않는다.
   */
  sweep(): void {
    for (const [code, room] of this.rooms) {
      if (!room.started && room.occupants.length === 0) {
        this.rooms.delete(code);
        continue;
      }
      if (room.run && room.run.status !== "running") {
        this.rooms.delete(code);
      }
    }
  }

  get size(): number {
    return this.rooms.size;
  }

  /** 진행 중인 방들 — 서버 루프가 매 틱 돌린다 */
  active(): Room[] {
    return [...this.rooms.values()].filter((room) => room.started);
  }
}

export function generateCode(): string {
  let code = "";
  for (let i = 0; i < 6; i += 1) {
    code += CODE_ALPHABET[randomInt(0, CODE_ALPHABET.length)];
  }
  return code;
}

export type Broadcaster = (memberId: string, message: ServerMessage) => void;
