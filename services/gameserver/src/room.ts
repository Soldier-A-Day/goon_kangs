import {
  ROLES,
  createRun,
  step,
  summarizeRun,
  type Effect,
  type Role,
  type RunState,
  type SimEvent,
} from "@sad/sim";
import type { Intent, LobbyState, ServerEvent, ServerMessage } from "@sad/protocol";
import { projectEffect, projectSnapshot } from "./snapshot.js";

/** 17.0 NET-01 — 시뮬레이션 20Hz, 스냅샷 10Hz */
export const TICK_HZ = 20;
export const SNAPSHOT_HZ = 10;
export const TICK_MS = 1000 / TICK_HZ;

/** 스킵 투표 성립 정족수 — 생존 인원의 3/4 (TIME-01) */
export const SKIP_QUORUM_RATIO = 3 / 4;

/**
 * 재접속 유예.
 *
 * 이탈 대리는 한도 없이 잔여 필수를 인수한다(2.0) — "게임을 못 하게 되는 것 자체가 최대 비용"이라
 * 판정 회피에 악용될 수 없다는 전제 위에 선 규칙이다. 그런데 끊기자마자 대리로 넘기면
 * 그 전제가 깨진다: 새로고침 한 번이면 몇 초 만에 돌아오면서 그날 필수를 전부 완수한 상태가 된다.
 * 비용이 0이고 이득만 남으므로 판정을 통째로 우회할 수 있다.
 *
 * 그래서 유예를 둔다. 안에 돌아오면 아무 일도 없었던 것이고, 넘기면 그때 진짜 이탈로 본다.
 */
export const DISCONNECT_GRACE_MS = 30_000;

export interface Seat {
  readonly role: Role;
  memberId: string | null;
  name: string | null;
  ready: boolean;
  connected: boolean;
}

export type Send = (memberId: string, message: ServerMessage) => void;

/**
 * 1방 = 1분대(4인 고정).
 *
 * 방은 상태를 소유하고 sim을 돌린다. 클라이언트가 보내는 것은 의도뿐이며,
 * 진척·판정·기온은 전부 여기서 결정된다 — 판정이 곧 승패이므로 클라를 신뢰할 수 없다.
 */
export class Room {
  readonly code: string;
  readonly seats: Seat[];
  hostId: string | null = null;
  started = false;
  run: RunState | null = null;

  private seq = 0;
  private sinceSnapshotMs = 0;
  private pendingEvents: ServerEvent[] = [];
  /** 지금 붙잡고 있는 퀘스트 — 진척은 서버가 센다 */
  private readonly working = new Map<string, string>();
  /**
   * 표시용 좌표. **검증하지 않고 되비추기만 한다.**
   *
   * 규칙이 보는 것은 `zone`뿐이라(`canWork`) 이 값이 틀려도 판정은 안 흔들린다.
   * sim에 두지 않은 이유가 그것이다 — 규칙 엔진에 화면 데이터를 넣으면
   * 헤드리스 시뮬이 좌표를 만들어내야 한다.
   */
  private readonly positions = new Map<string, { x: number; y: number }>();
  private readonly skipVotes = new Set<string>();
  /** 끊겼지만 아직 유예 중인 사람 → 남은 유예 ms */
  private readonly graceLeft = new Map<string, number>();
  private readonly leaderVotes = new Map<string, string>();

  /** 런이 끝났을 때 기록을 남길 곳. 서버가 주입한다 */
  onFinished: ((room: Room) => void) | null = null;
  /** 주기 스냅샷 — 전원 이탈해도 24시간 이어하기가 되도록 (17.0) */
  onPersist: ((room: Room) => void) | null = null;
  private sincePersistMs = 0;
  private finishedReported = false;

  constructor(
    code: string,
    private readonly config: Parameters<typeof createRun>[0]["config"],
    private readonly seed: number,
    private readonly send: Send,
  ) {
    this.code = code;
    this.seats = ROLES.map((role) => ({
      role,
      memberId: null,
      name: null,
      ready: false,
      connected: false,
    }));
  }

  /* ------------------------------------------------------------- 로비 */

  join(name: string, role: Role): { ok: true; memberId: string } | { ok: false; reason: string } {
    if (this.started) return { ok: false, reason: "roomStarted" };
    const seat = this.seats.find((s) => s.role === role);
    if (!seat) return { ok: false, reason: "unknownRole" };
    // 보직당 정확히 1명 — 중복이 없다 (3.0)
    if (seat.memberId) return { ok: false, reason: "roleTaken" };

    const memberId = `${role}-${Math.random().toString(36).slice(2, 8)}`;
    seat.memberId = memberId;
    seat.name = name;
    seat.ready = false;
    if (!this.hostId) this.hostId = memberId;
    return { ok: true, memberId };
  }

  leaveLobby(memberId: string): void {
    const seat = this.seats.find((s) => s.memberId === memberId);
    if (!seat || this.started) return;
    seat.memberId = null;
    seat.name = null;
    seat.ready = false;
    if (this.hostId === memberId) {
      this.hostId = this.seats.find((s) => s.memberId)?.memberId ?? null;
    }
  }

  setReady(memberId: string, value: boolean): void {
    const seat = this.seats.find((s) => s.memberId === memberId);
    if (seat) seat.ready = value;
  }

  setConnected(memberId: string, value: boolean): void {
    const seat = this.seats.find((s) => s.memberId === memberId);
    if (seat) seat.connected = value;
  }

  get occupants(): Seat[] {
    return this.seats.filter((s) => s.memberId !== null);
  }

  lobbyState(): LobbyState {
    return {
      type: "lobby",
      code: this.code,
      started: this.started,
      hostId: this.hostId ?? "",
      seats: this.seats.map((seat) => ({
        role: seat.role,
        memberId: seat.memberId,
        name: seat.name,
        ready: seat.ready,
      })),
    };
  }

  /**
   * 런 시작. 빈 보직은 NPC가 채우며, 처음부터 비어 있던 자리에는
   * 군기 −3/일이 붙지 않는다 (ROLE-03).
   */
  start(): boolean {
    if (this.started) return false;
    const members = this.occupants.map((seat) => ({
      id: seat.memberId as string,
      name: seat.name ?? "무명",
      role: seat.role,
    }));
    if (members.length === 0) return false;

    this.run = createRun({
      runId: `run-${this.code}`,
      seed: this.seed,
      members,
      config: this.config,
    });
    this.apply({ type: "beginDay" });
    this.started = true;
    this.broadcastSnapshot(true);
    return true;
  }

  /**
   * 같은 방으로 다시 시작한다.
   *
   * **방을 새로 만들지 않는다.** 코드도 토큰도 자리도 그대로 살아 있고,
   * 바뀌는 것은 런뿐이다 — 퇴소한 분대가 로비로 나가 방을 다시 만들고
   * 초대 코드를 다시 뿌리는 것은 같은 일을 두 번 하는 것이다.
   *
   * 끝난 런에서만 부른다. 진행 중인 런을 지울 수 있으면 그건 재시작이
   * 아니라 사고다.
   *
   * 시드를 바꾼다 — 같은 시드면 기온 롤과 일과 배정이 통째로 같아서,
   * 진 판을 외워서 다시 하는 것이 최적해가 된다.
   */
  restart(): boolean {
    if (!this.run || this.run.status === "running") return false;

    const members = this.run.members
      .filter((m) => m.presence !== "npcVacant")
      .map((m) => ({ id: m.id, name: m.name, role: m.role }));
    if (members.length === 0) return false;

    this.run = createRun({
      runId: `run-${this.code}`,
      seed: Math.floor(Math.random() * (2 ** 31 - 1)) + 1,
      members,
      config: this.config,
    });

    this.working.clear();
    this.positions.clear();
    this.skipVotes.clear();
    this.leaderVotes.clear();
    this.graceLeft.clear();
    this.finishedReported = false;
    this.sinceSnapshotMs = 0;

    this.apply({ type: "beginDay" });
    this.broadcastSnapshot(true);
    this.flushEvents();
    return true;
  }

  /* ------------------------------------------------------------- 진행 */

  /** 서버 시계가 주입하는 시간. sim은 시계를 갖지 않는다. */
  tick(elapsedMs: number): void {
    if (!this.run) return;
    if (this.run.status !== "running") {
      this.reportFinished();
      return;
    }

    // 붙잡고 있는 퀘스트에 먼저 진척을 넣는다 — 상호작용은 시간에 비례한다
    for (const [memberId, questId] of this.working) {
      this.apply({ type: "work", memberId, questId, deltaMs: elapsedMs });
    }

    this.apply({ type: "tick", elapsedMs });
    this.expireGraces(elapsedMs);

    this.sinceSnapshotMs += elapsedMs;
    if (this.sinceSnapshotMs >= 1000 / SNAPSHOT_HZ) {
      this.sinceSnapshotMs = 0;
      this.broadcastSnapshot();
    }

    // 저장은 매 틱이 아니라 일차가 넘어갈 만한 주기로 충분하다
    this.sincePersistMs += elapsedMs;
    if (this.sincePersistMs >= 10_000) {
      this.sincePersistMs = 0;
      this.onPersist?.(this);
    }

    this.flushEvents();
    if (this.run.status !== "running") this.reportFinished();
  }

  /**
   * 클라이언트 의도 처리. 여기서 서버가 거리·상태·권한을 검증한다.
   * sim은 규칙을, 이 함수는 "그 의도를 낼 자격이 있는가"를 본다.
   */
  handleIntent(memberId: string, intent: Intent): void {
    if (!this.run) {
      if (intent.type === "ready") {
        this.setReady(memberId, intent.value);
        this.broadcastLobby();
      }
      return;
    }
    if (this.run.status !== "running") return;

    const member = this.run.members.find((m) => m.id === memberId);
    if (!member || member.presence !== "player") return;

    switch (intent.type) {
      case "move":
        this.working.delete(memberId);
        this.apply({ type: "move", memberId, to: intent.to, onFoot: intent.onFoot });
        break;

      case "interact":
        if (intent.active) this.working.set(memberId, intent.questId);
        else this.working.delete(memberId);
        break;

      case "jointStep":
        // 요구 인원 검증은 sim이 한다 — 같은 규칙을 두 곳에 두지 않는다
        this.apply({ type: "jointStep", memberId, questId: intent.questId });
        break;

      case "questCleared":
        // 자격 검증은 sim이 한다 — 구역·시간대·소유자·최소 진척이 전부 거기 있고,
        // 여기서 또 보면 같은 규칙이 두 곳에 살게 된다 (ARCH-02)
        this.working.delete(memberId);
        this.apply({
          type: "questCleared",
          memberId,
          questId: intent.questId,
          grade: intent.grade,
        });
        // 완료는 다음 스냅샷을 기다리지 않는다 — 판을 통과한 순간 화면이 닫혀야 한다
        this.broadcastSnapshot(true);
        break;

      case "delegateChore":
        this.apply({
          type: "delegateChore",
          fromId: memberId,
          toId: intent.toId,
          questId: intent.questId,
        });
        break;

      case "vetoChore":
        this.apply({ type: "vetoChore", memberId, questId: intent.questId });
        break;

      case "fileClaim":
        // 청구서는 행정병만 쓴다 — 자격 검증은 sim이 한다 (11.0)
        this.apply({ type: "fileClaim", memberId, items: intent.items });
        this.broadcastSnapshot(true);
        break;

      case "leaderReassign":
        this.apply({
          type: "leaderReassign",
          leaderId: memberId,
          questId: intent.questId,
          toId: intent.toId,
        });
        break;

      case "voteSkip":
        this.voteSkip(memberId, intent.value);
        break;

      case "voteLeader":
        this.voteLeader(memberId, intent.candidateId);
        break;

      case "position":
        this.positions.set(memberId, { x: intent.x, y: intent.y });
        break;

      case "quickCommand":
        this.pendingEvents.push({
          type: "quickCommand",
          memberId,
          command: intent.command,
          zone: member.zone,
        });
        break;

      case "chat":
        this.relayChat(memberId, intent.text);
        break;

      case "ready":
        break;
    }

    this.flushEvents();
  }

  /**
   * 접속이 끊겼다. 곧바로 이탈로 보지 않고 유예를 준다 — 새로고침과 진짜 이탈은 다른 사건이다.
   */
  disconnect(memberId: string): void {
    this.setConnected(memberId, false);
    this.working.delete(memberId);

    if (!this.run || !this.started) {
      this.leaveLobby(memberId);
      this.broadcastLobby();
      return;
    }

    this.graceLeft.set(memberId, DISCONNECT_GRACE_MS);
  }

  reconnect(memberId: string): void {
    this.setConnected(memberId, true);
    // 유예 안에 돌아왔다면 아무 일도 없었던 것이다
    this.graceLeft.delete(memberId);

    if (this.run) {
      this.apply({ type: "rejoinRun", memberId });
      this.sendTo(memberId, projectSnapshot(this.run, ++this.seq, this.positions));
    } else {
      this.broadcastLobby();
    }
  }

  /** 런 기록은 한 번만 남긴다 */
  private reportFinished(): void {
    if (this.finishedReported || !this.run) return;
    if (this.run.status === "running") return;
    this.finishedReported = true;
    this.onFinished?.(this);
  }

  /** 저장된 스냅샷에서 방을 되살린다 (17.0 이어하기) */
  restore(state: RunState): void {
    this.run = state;
    this.started = true;
    for (const member of state.members) {
      const seat = this.seats.find((s) => s.role === member.role);
      if (seat && member.presence !== "npcVacant") {
        seat.memberId = member.id;
        seat.name = member.name;
      }
    }
    this.hostId ??= this.occupants[0]?.memberId ?? null;
  }

  summarize() {
    return this.run ? summarizeRun(this.run) : null;
  }

  /** 유예를 넘긴 사람만 진짜 이탈로 처리한다 */
  private expireGraces(elapsedMs: number): void {
    if (this.graceLeft.size === 0) return;

    for (const [memberId, left] of [...this.graceLeft]) {
      const remaining = left - elapsedMs;
      if (remaining > 0) {
        this.graceLeft.set(memberId, remaining);
        continue;
      }
      this.graceLeft.delete(memberId);
      this.apply({ type: "leaveRun", memberId });
      this.broadcastSnapshot(true);
    }
  }

  /* ------------------------------------------------------------- 내부 */

  private voteSkip(memberId: string, value: boolean): void {
    if (!this.run) return;
    if (value) this.skipVotes.add(memberId);
    else this.skipVotes.delete(memberId);

    const alive = this.run.members.filter((m) => m.presence === "player").length;
    const needed = Math.ceil(alive * SKIP_QUORUM_RATIO);
    if (this.skipVotes.size >= needed && needed > 0) {
      this.skipVotes.clear();
      this.apply({ type: "skipPhase" });
      this.broadcastSnapshot(true);
    }
  }

  private voteLeader(memberId: string, candidateId: string): void {
    if (!this.run) return;
    this.leaderVotes.set(memberId, candidateId);

    const tally = new Map<string, number>();
    for (const candidate of this.leaderVotes.values()) {
      tally.set(candidate, (tally.get(candidate) ?? 0) + 1);
    }
    const voters = this.run.members.filter((m) => m.presence === "player").length;
    for (const [candidate, count] of tally) {
      // 2:2 동수면 현직 유지 — 과반이어야 바뀐다 (ROLE-02)
      if (count > voters / 2) {
        this.run.leaderId = candidate;
        this.leaderVotes.clear();
        this.broadcastSnapshot(true);
        return;
      }
    }
  }

  /**
   * 8.0 채널. 근접 채팅은 같은 구역에만, 무전은 통신병이 열어둔 채널로 전원에게.
   * 무전이 끊기면 물리적으로 모여야 정보가 전달된다.
   */
  private relayChat(memberId: string, text: string): void {
    if (!this.run) return;
    const sender = this.run.members.find((m) => m.id === memberId);
    if (!sender) return;

    // 8.0 — 통신병이라고 무조건 무전이 되는 게 아니다. **무전이 살아 있어야** 한다.
    // 두절 상태에서는 통신병도 근접 반경 안에서만 말이 닿는다
    const radio = sender.role === "comms" && this.run.radio !== "down";
    const event: ServerEvent = { type: "chat", memberId, text, viaRadio: radio };

    for (const member of this.run.members) {
      if (member.presence !== "player") continue;
      if (!radio && member.zone !== sender.zone) continue;
      this.sendTo(member.id, { type: "events", items: [event] });
    }
  }

  private apply(event: SimEvent): void {
    if (!this.run) return;
    const result = step(this.run, event);
    this.run = result.state;
    this.collect(result.effects);
  }

  private collect(effects: readonly Effect[]): void {
    for (const effect of effects) {
      const projected = projectEffect(effect);
      if (projected) this.pendingEvents.push(projected);
    }
  }

  private flushEvents(): void {
    if (this.pendingEvents.length === 0) return;
    const items = this.pendingEvents;
    this.pendingEvents = [];
    this.broadcast({ type: "events", items });
  }

  broadcastSnapshot(force = false): void {
    if (!this.run) return;
    if (!force && !this.started) return;
    this.broadcast(projectSnapshot(this.run, ++this.seq, this.positions));
  }

  broadcastLobby(): void {
    this.broadcast(this.lobbyState());
  }

  private broadcast(message: ServerMessage): void {
    for (const seat of this.occupants) {
      if (seat.memberId) this.sendTo(seat.memberId, message);
    }
  }

  private sendTo(memberId: string, message: ServerMessage): void {
    this.send(memberId, message);
  }
}
