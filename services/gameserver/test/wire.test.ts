import { spawn, type ChildProcess } from "node:child_process";
import { afterAll, beforeAll, describe, expect, it } from "vitest";
import WebSocket from "ws";
import {
  serverMessageSchema, snapshotSchema, PROTOCOL_VERSION,
} from "@sad/protocol";

/**
 * 와이어 프로토콜 통합 검증.
 *
 * 기존 26개 테스트는 `Room`을 직접 불러 규칙을 본다. 그건 규칙이 맞는지는
 * 알려주지만 **선을 타고 나가는 바이트가 스키마와 맞는지는 알려주지 않는다.**
 *
 * Unity 클라이언트는 그 바이트를 생성된 C# DTO로 파싱한다. DTO는 여기 있는
 * zod 스키마에서 나왔으므로, **서버 출력이 스키마를 만족하면 C# 파싱도 맞다** —
 * 반대로 여기서 어긋나면 Unity에서는 조용히 빈 필드로 나타나 원인을 찾기 어렵다.
 *
 * 그래서 Unity가 하는 것과 같은 순서로 돈다: 방 생성 → 시작 → WS → 스냅샷 → 의도.
 */
const PORT = 8099;
const BASE = `http://127.0.0.1:${PORT}`;

let server: ChildProcess;

const waitFor = async (check: () => Promise<boolean>, timeoutMs = 15_000) => {
  const started = Date.now();
  while (Date.now() - started < timeoutMs) {
    if (await check().catch(() => false)) return;
    await new Promise((resolve) => setTimeout(resolve, 100));
  }
  throw new Error("서버가 뜨지 않았다");
};

beforeAll(async () => {
  server = spawn("npx", ["tsx", "src/index.ts"], {
    cwd: new URL("..", import.meta.url).pathname,
    env: { ...process.env, PORT: String(PORT) },
    stdio: "ignore",
  });

  await waitFor(async () => (await fetch(`${BASE}/health`)).ok);
}, 30_000);

afterAll(() => server?.kill());

// `response.json()`은 `unknown`을 준다 — 테스트에서 매번 좁히면 읽기만 나빠지므로
// 여기서 한 번 푼다. 서버 응답 모양은 아래 단언들이 검사한다
const post = async (path: string, body: unknown) => {
  const response = await fetch(`${BASE}${path}`, {
    method: "POST",
    headers: { "content-type": "application/json" },
    body: JSON.stringify(body),
  });
  return { status: response.status, body: (await response.json()) as any };
};

/** 다음 메시지를 기다린다. 조건에 맞는 것이 올 때까지 흘려보낸다 */
const nextMessage = (socket: WebSocket, match: (m: any) => boolean, timeoutMs = 10_000) =>
  new Promise<any>((resolve, reject) => {
    const timer = setTimeout(() => reject(new Error("메시지가 오지 않았다")), timeoutMs);
    const onMessage = (raw: WebSocket.RawData) => {
      const parsed = JSON.parse(raw.toString());
      if (!match(parsed)) return;
      clearTimeout(timer);
      socket.off("message", onMessage);
      resolve(parsed);
    };
    socket.on("message", onMessage);
  });

describe("와이어 프로토콜", () => {
  it("Unity와 같은 순서로 붙어 스냅샷을 받는다", async () => {
    const created = await post("/rooms", {
      name: "일병 김", role: "rifle", difficulty: "regular", season: "cold",
    });
    expect(created.status).toBe(200);
    expect(created.body.token).toBeTruthy();

    const { code, memberId, token } = created.body;

    const started = await post(`/rooms/${code}/start?token=${encodeURIComponent(token)}`, {});
    expect(started.status).toBe(200);

    const socket = new WebSocket(`ws://127.0.0.1:${PORT}/ws?token=${encodeURIComponent(token)}`);
    await new Promise((resolve, reject) => {
      socket.once("open", resolve);
      socket.once("error", reject);
    });

    try {
      const welcome = await nextMessage(socket, (m) => m.type === "welcome");
      expect(welcome.protocolVersion).toBe(PROTOCOL_VERSION);
      expect(welcome.memberId).toBe(memberId);

      const snapshot = await nextMessage(socket, (m) => m.type === "snapshot");

      // **핵심 검사.** 서버가 실제로 내보낸 바이트가 스키마를 만족하는가.
      // C# DTO가 이 스키마에서 생성되므로, 여기가 맞으면 Unity 파싱도 맞다.
      const parsed = snapshotSchema.safeParse(snapshot);
      expect(parsed.success, JSON.stringify(parsed.error?.issues?.slice(0, 5))).toBe(true);

      expect(snapshot.members).toHaveLength(4); // ROLE-03 — 빈 보직은 NPC가 채운다
      expect(snapshot.members.map((m: any) => m.role).sort())
        .toEqual(["admin", "comms", "medic", "rifle"]);
    } finally {
      socket.close();
    }
  }, 40_000);

  it("서버가 보내는 모든 메시지가 스키마를 만족한다", async () => {
    const created = await post("/rooms", {
      name: "일병 이", role: "comms", difficulty: "regular", season: "cold",
    });
    const { code, token } = created.body;
    await post(`/rooms/${code}/start?token=${encodeURIComponent(token)}`, {});

    const socket = new WebSocket(`ws://127.0.0.1:${PORT}/ws?token=${encodeURIComponent(token)}`);
    await new Promise((resolve) => socket.once("open", resolve));

    const seen: string[] = [];
    const failures: string[] = [];

    socket.on("message", (raw) => {
      const message = JSON.parse(raw.toString());
      seen.push(message.type);

      const parsed = serverMessageSchema.safeParse(message);
      if (!parsed.success) {
        failures.push(`${message.type}: ${JSON.stringify(parsed.error.issues.slice(0, 3))}`);
      }
    });

    // 여러 틱을 돌려 스냅샷·이벤트가 두루 나오게 한다
    await new Promise((resolve) => setTimeout(resolve, 3_000));
    socket.close();

    expect(failures, failures.join("\n")).toEqual([]);
    expect(seen).toContain("welcome");
    expect(seen).toContain("snapshot");
  }, 40_000);

  it("토큰 없이 열면 거절한다", async () => {
    const socket = new WebSocket(`ws://127.0.0.1:${PORT}/ws`);

    // `open`을 기다린 뒤에 리스너를 걸면 놓친다. 서버는 연결되자마자 거절
    // 메시지를 보내고 곧바로 닫으므로, 그 사이에 리스너가 없으면 영영 못 받는다 —
    // 간헐 실패의 원인이었다. 리스너를 먼저 건다
    const error = await nextMessage(socket, (m) => m.type === "error");
    expect(error.code).toBe("invalidToken");
    socket.close();
  }, 20_000);

  it("이동 의도를 보내면 스냅샷의 구역이 따라온다", async () => {
    const created = await post("/rooms", {
      name: "일병 박", role: "medic", difficulty: "regular", season: "cold",
    });
    const { code, memberId, token } = created.body;
    await post(`/rooms/${code}/start?token=${encodeURIComponent(token)}`, {});

    const socket = new WebSocket(`ws://127.0.0.1:${PORT}/ws?token=${encodeURIComponent(token)}`);
    await new Promise((resolve) => socket.once("open", resolve));

    try {
      const first = await nextMessage(socket, (m) => m.type === "snapshot");
      const me = first.members.find((m: any) => m.id === memberId);
      const from = me.zone;

      // 지금 있는 곳이 아닌 데로 보낸다
      const to = from === "Z07" ? "Z08" : "Z07";
      socket.send(JSON.stringify({ type: "move", to }));

      // 이동에는 시간이 걸린다(4.3 구역 그래프). 도착할 때까지 스냅샷을 본다.
      const arrived = await nextMessage(
        socket,
        (m) => m.type === "snapshot" && m.members.find((x: any) => x.id === memberId)?.zone === to,
        20_000,
      );

      expect(arrived.members.find((m: any) => m.id === memberId).zone).toBe(to);
    } finally {
      socket.close();
    }
  }, 40_000);
});
