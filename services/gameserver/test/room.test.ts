import { describe, expect, it } from "vitest";
import { phaseAt } from "@sad/sim";
import type { ServerMessage, Snapshot } from "@sad/protocol";
import { DISCONNECT_GRACE_MS, Room } from "../src/room.js";
import { RoomStore, generateCode } from "../src/store.js";
import { projectEffect } from "../src/snapshot.js";
import { RUN_TTL_MS, memoryStorage } from "../src/persistence.js";

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

    // 아무 퀘스트나 잡고 **그 구역으로 옮긴 뒤** 붙잡는다.
    // 시작 구역에 있는 퀘스트를 찾던 예전 방식은 그날 배정 뽑기에 기대고 있어서,
    // 퀘스트 풀이 늘자 시드에 따라 못 찾고 깨졌다
    // **지금 시간대의** 일과여야 한다. 일과는 제 칸에서만 진척이 쌓이는데(4.0),
    // 예전에는 아무 일과나 집어서 기상·점검 칸에 오후 일과를 붙잡고 있었다
    const now = phaseAt(h.room.run.phaseIndex).id;
    const quest = h.room.run.quests.find(
      (q) => q.ownerId === rifle && q.phase === now,
    );
    if (!quest) throw new Error(`${now} 칸의 소총수 일과 없음`);

    // 걸어서 옮긴다. 인접 구역으로만 즉시 넘어가므로(`zones.ts`) 복도를
    // 경유한다 — 생활관동의 방은 전부 복도(Z02)에서만 이어지고, 연병장으로
    // 나가는 문도 복도에 있다. 연병장을 먼저 거치면 세면장으로 못 들어간다
    h.room.handleIntent(rifle, { type: "move", to: "Z02", onFoot: true });
    h.room.handleIntent(rifle, { type: "move", to: quest.zone, onFoot: true });
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
    h.room.handleIntent(rifle, { type: "move", to: "Z08" });
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

  it("유예를 넘긴 이탈만 대리로 전환되고, 재접속하면 복귀한다", () => {
    const h = harness();
    const ids = fourPlayers(h.room);
    h.room.start();

    h.room.disconnect(ids[0] as string);
    h.room.tick(DISCONNECT_GRACE_MS + 1000);
    expect(h.room.run?.members.find((m) => m.id === ids[0])?.presence).toBe("npcLeave");

    h.room.reconnect(ids[0] as string);
    expect(h.room.run?.members.find((m) => m.id === ids[0])?.presence).toBe("player");
  });
});

describe("하달 창 조기 종료", () => {
  /** 계급차가 있어야 sim이 창을 연다(6.2) — 테스트 편의상 직접 연다 */
  function openWindow(room: Room): void {
    if (!room.run) throw new Error("런 없음");
    const [first] = room.run.members;
    if (first) first.rank = "corporal";
    room.run.delegationWindowMsLeft = 20_000;
    for (const member of room.run.members) member.delegationDone = false;
  }

  it("delegationDone 의도가 sim까지 전달된다 — 전원 확정 시 즉시 닫힌다", () => {
    const h = harness();
    const ids = fourPlayers(h.room);
    h.room.start();
    openWindow(h.room);

    for (const id of ids.slice(0, 3)) {
      h.room.handleIntent(id as string, { type: "delegationDone" });
    }
    expect(h.room.run?.delegationWindowMsLeft).toBe(20_000);

    h.room.handleIntent(ids[3] as string, { type: "delegationDone" });
    expect(h.room.run?.delegationWindowMsLeft).toBe(0);
  });

  it("접속이 끊긴 사람은 조기 종료 집계에서 세지 않는다", () => {
    const h = harness();
    const ids = fourPlayers(h.room);
    h.room.start();
    openWindow(h.room);

    // 미접속 — 게임서버가 대신 신고를 흘려 나머지가 못 닫는 일이 없게 한다
    h.room.disconnect(ids[0] as string);
    for (const id of ids.slice(1)) {
      h.room.handleIntent(id as string, { type: "delegationDone" });
    }
    expect(h.room.run?.delegationWindowMsLeft).toBe(0);
  });
});

describe("채널", () => {
  it("근접 채팅은 같은 구역에만 간다", () => {
    const h = harness();
    const ids = fourPlayers(h.room);
    h.room.start();
    const [rifle, , medic] = ids;
    if (!rifle || !medic) throw new Error("분대원 없음");

    h.room.handleIntent(medic, { type: "move", to: "Z05" });
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

    h.room.handleIntent(medic, { type: "move", to: "Z05" });
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

  it("발급한 토큰으로만 방을 찾을 수 있다", async () => {
    const store = new RoomStore(() => {});
    const room = store.createRoom({});
    const joined = room.join("김", "rifle");
    if (!joined.ok) throw new Error("입장 실패");

    const token = store.issueToken(room.code, joined.memberId);
    // 저장소를 볼 수도 있어 비동기다 — 서버가 재시작해도 토큰이 살아 있어야 한다
    expect((await store.resolve(token))?.session.memberId).toBe(joined.memberId);
    expect(await store.resolve("아무거나")).toBeNull();

    store.revoke(token);
    expect(await store.resolve(token)).toBeNull();
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

describe("재접속 유예", () => {
  it("끊기자마자 이탈 대리로 넘기지 않는다", () => {
    const h = harness();
    const ids = fourPlayers(h.room);
    h.room.start();

    h.room.disconnect(ids[0] as string);
    expect(h.room.run?.members.find((m) => m.id === ids[0])?.presence).toBe("player");
  });

  it("유예 안에 돌아오면 아무 일도 없었던 것이다 — 새로고침으로 필수를 우회할 수 없다", () => {
    const h = harness();
    const ids = fourPlayers(h.room);
    h.room.start();

    const before = h.room.run?.quests.filter(
      (q) => q.required && q.ownerId === ids[0] && q.status !== "done",
    ).length;

    h.room.disconnect(ids[0] as string);
    h.room.tick(5000);
    h.room.reconnect(ids[0] as string);
    h.room.tick(DISCONNECT_GRACE_MS + 1000);

    const after = h.room.run?.quests.filter(
      (q) => q.required && q.ownerId === ids[0] && q.status !== "done",
    ).length;

    expect(h.room.run?.members.find((m) => m.id === ids[0])?.presence).toBe("player");
    expect(after).toBe(before);
  });

  it("유예를 넘기면 그때 진짜 이탈로 본다", () => {
    const h = harness();
    const ids = fourPlayers(h.room);
    h.room.start();

    h.room.disconnect(ids[0] as string);
    h.room.tick(DISCONNECT_GRACE_MS + 1000);

    expect(h.room.run?.members.find((m) => m.id === ids[0])?.presence).toBe("npcLeave");
  });
});

describe("승급 심사 투영", () => {
  it("심사 결과가 점수 내역과 함께 클라이언트로 나간다", () => {
    const event = projectEffect({
      type: "rankReviewed",
      day: 3,
      isRetry: false,
      require: 18,
      outcomes: [
        {
          memberId: "p1",
          promoted: true,
          from: "private",
          to: "pfc",
          score: 22,
          require: 18,
          trustBonus: 12,
        },
      ],
    });

    expect(event).toMatchObject({ type: "rankReviewed", day: 3, require: 18 });
    // 점수 내역은 전원에게 공개된다 (13.1 공개 원칙)
    expect(event && "outcomes" in event ? event.outcomes[0] : null).toMatchObject({
      memberId: "p1",
      promoted: true,
      score: 22,
      // E-2 — 승급 점수의 절반(신뢰보너스)이 예전엔 여기서 잘렸다. 이제는 그대로 흘려
      // 승급 화면이 "복무점수+신뢰보너스"로 분해할 수 있다(WORKORDER.md E-2, 감사 목록 3)
      trustBonus: 12,
    });
  });

  it("conditionCritical도 더 이상 버리지 않는다 — 후송/강제취침 직전 신호다", () => {
    expect(
      projectEffect({ type: "conditionCritical", memberId: "p1", stat: "stamina" }),
    ).toEqual({ type: "conditionCritical", memberId: "p1", stat: "stamina" });
  });

  it("하달 거부 사유는 더 이상 버리지 않는다 — 화면이 왜 안 됐는지 말해야 한다", () => {
    expect(
      projectEffect({ type: "delegationRefused", reason: "rankTooLow", questId: "c1" }),
    ).toEqual({ type: "delegationRefused", reason: "rankTooLow", questId: "c1" });
  });

  // WORKORDER.md E-2 잔여 — 군기 정산도 최종값 한 줄이 아니라 항목별 델타로 나가야 한다
  it("군기 정산은 최종값 한 줄이 아니라 항목별 델타와 함께 나간다", () => {
    expect(
      projectEffect({
        type: "disciplineChanged",
        from: 60,
        to: 78,
        band: "normal",
        deltas: [
          { reason: "onTimeCompletion", value: 5 },
          { reason: "jointFlawless", value: 12 },
          { reason: "noInjuryDay", value: 6 },
        ],
      }),
    ).toEqual({
      type: "disciplineChanged",
      from: 60,
      to: 78,
      band: "normal",
      deltas: [
        { reason: "onTimeCompletion", value: 5 },
        { reason: "jointFlawless", value: 12 },
        { reason: "noInjuryDay", value: 6 },
      ],
    });
  });
});

describe("17.0 이어하기와 기록", () => {
  it("만료 전에는 저장된 런을 되살린다", async () => {
    let clock = 1_000_000;
    const store = new RoomStore(() => {}, memoryStorage(() => clock));
    const room = store.createRoom({});
    const joined = room.join("김소총", "rifle");
    if (!joined.ok) throw new Error("입장 실패");
    room.start();
    room.tick(11_000); // 주기 저장이 돌 만큼

    const resumed = await store.resume(room.code);
    expect(resumed).not.toBeNull();
    expect(resumed?.run?.day).toBe(room.run?.day);
  });

  it("24시간이 지나면 되살리지 않는다", async () => {
    let clock = 1_000_000;
    const store = new RoomStore(() => {}, memoryStorage(() => clock));
    const room = store.createRoom({});
    room.join("김소총", "rifle");
    room.start();
    room.tick(11_000);

    const code = room.code;
    store.sweep();
    clock += RUN_TTL_MS + 1;

    // 진행 중인 방은 sweep이 지우지 않으므로 직접 저장소만 확인한다
    expect(await store.storage.snapshots.load(code)).toBeNull();
  });

  it("런이 끝나면 기록이 남고 이어하기 스냅샷은 정리된다", async () => {
    const storage = memoryStorage();
    const store = new RoomStore(() => {}, storage);
    const room = store.createRoom({ difficulty: "regular" });
    const joined = room.join("김소총", "rifle");
    if (!joined.ok) throw new Error("입장 실패");
    room.start();
    room.tick(11_000);

    // 필수를 남긴 채 하루를 넘겨 퇴소시킨다
    if (!room.run) throw new Error("런 없음");
    room.run.reliefsRemaining = 0;
    let guard = 0;
    while (room.run.status === "running" && guard++ < 200) {
      room.tick(5_000);
    }

    expect(room.run.status).not.toBe("running");
    const records = await storage.records.list();
    expect(records).toHaveLength(1);
    expect(records[0]?.status).toBe("discharged");
    expect(await storage.snapshots.load(room.code)).toBeNull();
  });

  it("기록은 한 번만 남는다", async () => {
    const storage = memoryStorage();
    const store = new RoomStore(() => {}, storage);
    const room = store.createRoom({});
    room.join("김소총", "rifle");
    room.start();
    if (!room.run) throw new Error("런 없음");
    room.run.status = "cleared";

    room.tick(1000);
    room.tick(1000);

    expect(await storage.records.list()).toHaveLength(1);
  });
});
