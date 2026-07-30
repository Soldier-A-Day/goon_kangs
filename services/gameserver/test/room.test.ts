import { describe, expect, it } from "vitest";
import type { ServerMessage, Snapshot } from "@sad/protocol";
import { Room } from "../src/room.js";
import { RoomStore, generateCode } from "../src/store.js";

interface Harness {
  room: Room;
  sent: { memberId: string; message: ServerMessage }[];
  lastSnapshot(): Snapshot | undefined;
}

function harness(): Harness {
  const sent: { memberId: string; message: ServerMessage }[] = [];
  const room = new Room("TEST01", { totalDays: 18 }, 1234, (memberId, message) => {
    sent.push({ memberId, message });
  });
  return {
    room,
    sent,
    lastSnapshot() {
      const snapshots = sent
        .map((s) => s.message)
        .filter((m): m is Snapshot => m.type === "snapshot");
      return snapshots[snapshots.length - 1];
    },
  };
}

function fourPlayers(room: Room): string[] {
  return [
    room.join("소총", "rifle"),
    room.join("통신", "comms"),
    room.join("의무", "medic"),
    room.join("행정", "admin"),
  ].map((result) => (result.ok ? result.memberId : ""));
}

describe("로비", () => {
  it("보직당 한 명만 앉을 수 있다", () => {
    const { room } = harness();
    expect(room.join("김", "rifle").ok).toBe(true);
    expect(room.join("이", "rifle")).toEqual({ ok: false, reason: "roleTaken" });
    expect(room.join("이", "comms").ok).toBe(true);
  });

  it("먼저 들어온 사람이 방장이 되고, 나가면 넘어간다", () => {
    const { room } = harness();
    const first = room.join("김", "rifle");
    const second = room.join("이", "comms");
    if (!first.ok || !second.ok) throw new Error("입장 실패");

    expect(room.hostId).toBe(first.memberId);
    room.leaveLobby(first.memberId);
    expect(room.hostId).toBe(second.memberId);
  });

  it("시작한 방에는 들어갈 수 없다", () => {
    const { room } = harness();
    fourPlayers(room);
    room.start();
    expect(room.join("늦은", "rifle")).toEqual({ ok: false, reason: "roomStarted" });
  });

  it("인원이 모자라도 시작할 수 있고 빈 자리는 NPC가 채운다", () => {
    const { room } = harness();
    room.join("김", "rifle");
    expect(room.start()).toBe(true);
    expect(room.run?.members).toHaveLength(4);
    expect(room.run?.members.filter((m) => m.presence === "npcVacant")).toHaveLength(3);
  });
});

describe("스냅샷", () => {
  it("시드와 RNG 상태가 새어나가지 않는다", () => {
    const h = harness();
    fourPlayers(h.room);
    h.room.start();

    const snapshot = h.lastSnapshot();
    expect(snapshot).toBeDefined();
    const serialized = JSON.stringify(snapshot);
    expect(serialized).not.toContain("rngState");
    expect(serialized).not.toContain("\"seed\"");
  });

  it("퀘스트는 진척 비율만 담고 원본 소요는 담지 않는다", () => {
    const h = harness();
    fourPlayers(h.room);
    h.room.start();

    const quest = h.lastSnapshot()?.quests[0];
    expect(quest).toBeDefined();
    expect(quest?.progress).toBe(0);
    expect(quest).not.toHaveProperty("workMs");
  });

  it("시작 전에는 스냅샷을 보내지 않는다", () => {
    const h = harness();
    fourPlayers(h.room);
    expect(h.lastSnapshot()).toBeUndefined();
  });
});

describe("진행", () => {
  it("붙잡고 있는 퀘스트가 시간에 비례해 진행된다", () => {
    const h = harness();
    const [rifle] = fourPlayers(h.room);
    h.room.start();
    if (!h.room.run || !rifle) throw new Error("런 없음");

    const quest = h.room.run.quests.find(
      (q) => q.ownerId === rifle && q.zone === h.room.run?.members[0]?.zone,
    );
    if (!quest) throw new Error("같은 구역 퀘스트 없음");

    h.room.handleIntent(rifle, { type: "interact", questId: quest.id, active: true });
    h.room.tick(5000);

    const after = h.room.run.quests.find((q) => q.id === quest.id);
    expect(after?.workedMs).toBeGreaterThan(0);
  });

  it("이동을 시작하면 붙잡고 있던 퀘스트를 놓는다", () => {
    const h = harness();
    const [rifle] = fourPlayers(h.room);
    h.room.start();
    if (!rifle || !h.room.run) throw new Error("런 없음");

    const quest = h.room.run.quests.find((q) => q.ownerId === rifle);
    if (!quest) throw new Error("퀘스트 없음");

    h.room.handleIntent(rifle, { type: "interact", questId: quest.id, active: true });
    h.room.handleIntent(rifle, { type: "move", to: "storage" });
    h.room.tick(5000);

    expect(h.room.run.quests.find((q) => q.id === quest.id)?.workedMs).toBe(0);
  });

  it("스킵 투표는 3/4가 모여야 성립한다", () => {
    const h = harness();
    const ids = fourPlayers(h.room);
    h.room.start();
    const phaseBefore = h.room.run?.phaseIndex;

    h.room.handleIntent(ids[0] as string, { type: "voteSkip", value: true });
    h.room.handleIntent(ids[1] as string, { type: "voteSkip", value: true });
    expect(h.room.run?.phaseIndex).toBe(phaseBefore);

    h.room.handleIntent(ids[2] as string, { type: "voteSkip", value: true });
    expect(h.room.run?.phaseIndex).toBe((phaseBefore ?? 0) + 1);
  });

  it("분대장은 과반으로만 바뀐다 — 2:2 동수면 현직 유지", () => {
    const h = harness();
    const ids = fourPlayers(h.room);
    h.room.start();

    h.room.handleIntent(ids[0] as string, { type: "voteLeader", candidateId: ids[0] as string });
    h.room.handleIntent(ids[1] as string, { type: "voteLeader", candidateId: ids[0] as string });
    expect(h.room.run?.leaderId).toBeNull();

    h.room.handleIntent(ids[2] as string, { type: "voteLeader", candidateId: ids[0] as string });
    expect(h.room.run?.leaderId).toBe(ids[0]);
  });

  it("접속이 끊기면 이탈 대리로 전환된다", () => {
    const h = harness();
    const ids = fourPlayers(h.room);
    h.room.start();

    h.room.disconnect(ids[0] as string);
    expect(h.room.run?.members.find((m) => m.id === ids[0])?.presence).toBe("npcLeave");

    h.room.reconnect(ids[0] as string);
    expect(h.room.run?.members.find((m) => m.id === ids[0])?.presence).toBe("player");
  });
});

describe("채널", () => {
  it("근접 채팅은 같은 구역에만 간다", () => {
    const h = harness();
    const ids = fourPlayers(h.room);
    h.room.start();
    const [rifle, , medic] = ids;
    if (!rifle || !medic) throw new Error("분대원 없음");

    h.room.handleIntent(medic, { type: "move", to: "infirmary" });
    h.room.tick(60_000);
    h.sent.length = 0;

    h.room.handleIntent(rifle, { type: "chat", text: "여기 좀" });

    const receivers = h.sent
      .filter((s) => s.message.type === "events")
      .map((s) => s.memberId);
    expect(receivers).toContain(rifle);
    expect(receivers).not.toContain(medic);
  });

  it("통신병의 채팅은 무전이라 거리 제한이 없다", () => {
    const h = harness();
    const ids = fourPlayers(h.room);
    h.room.start();
    const [rifle, comms, medic] = ids;
    if (!rifle || !comms || !medic) throw new Error("분대원 없음");

    h.room.handleIntent(medic, { type: "move", to: "infirmary" });
    h.room.tick(60_000);
    h.sent.length = 0;

    h.room.handleIntent(comms, { type: "chat", text: "전 인원 집합" });

    const receivers = h.sent
      .filter((s) => s.message.type === "events")
      .map((s) => s.memberId);
    expect(receivers).toContain(medic);
  });
});

describe("토큰과 방 코드", () => {
  it("초대 코드는 헷갈리는 글자를 쓰지 않는다", () => {
    for (let i = 0; i < 200; i += 1) {
      const code = generateCode();
      expect(code).toHaveLength(6);
      expect(code).not.toMatch(/[01IO]/);
    }
  });

  it("발급한 토큰으로만 방을 찾을 수 있다", () => {
    const store = new RoomStore(() => {});
    const room = store.createRoom({});
    const joined = room.join("김", "rifle");
    if (!joined.ok) throw new Error("입장 실패");

    const token = store.issueToken(room.code, joined.memberId);
    expect(store.resolve(token)?.session.memberId).toBe(joined.memberId);
    expect(store.resolve("아무거나")).toBeNull();

    store.revoke(token);
    expect(store.resolve(token)).toBeNull();
  });

  it("시작하지 않은 빈 방은 청소된다", () => {
    const store = new RoomStore(() => {});
    const room = store.createRoom({});
    const joined = room.join("김", "rifle");
    if (!joined.ok) throw new Error("입장 실패");

    store.sweep();
    expect(store.size).toBe(1);

    room.leaveLobby(joined.memberId);
    store.sweep();
    expect(store.size).toBe(0);
  });
});
