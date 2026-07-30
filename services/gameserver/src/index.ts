import { createServer, type IncomingMessage, type ServerResponse } from "node:http";
import { WebSocketServer, type WebSocket } from "ws";
import {
  PROTOCOL_VERSION,
  createRoomRequestSchema,
  joinRoomRequestSchema,
  parseIntent,
  type ServerMessage,
} from "@sad/protocol";
import { TICK_MS } from "./room.js";
import { RoomStore } from "./store.js";

const PORT = Number(process.env.PORT ?? 8080);

/**
 * 게임 서버.
 *
 * Next.js 라우트 핸들러가 아니라 상시 구동 Node 프로세스인 이유는 두 가지다.
 * 1. Next 16의 Route Handler는 WebSocket을 지원하지 않는다(연결이 응답 생성 후 닫힌다).
 * 2. 20Hz 룸 루프는 상태를 든 장수 프로세스가 필요하다.
 */
const sockets = new Map<string, WebSocket>();

const store = new RoomStore((memberId, message) => {
  const socket = sockets.get(memberId);
  if (socket && socket.readyState === socket.OPEN) {
    socket.send(JSON.stringify(message));
  }
});

/* ------------------------------------------------------------------ HTTP */

const server = createServer(async (req, res) => {
  cors(res);
  if (req.method === "OPTIONS") return end(res, 204, null);

  const url = new URL(req.url ?? "/", `http://${req.headers.host}`);
  const parts = url.pathname.split("/").filter(Boolean);

  try {
    if (req.method === "GET" && url.pathname === "/health") {
      return end(res, 200, { ok: true, rooms: store.size });
    }

    // POST /rooms — 방 생성 (생성자가 방장이 된다)
    if (req.method === "POST" && url.pathname === "/rooms") {
      const body = createRoomRequestSchema.safeParse(await readJson(req));
      if (!body.success) return end(res, 400, { error: "invalidBody" });

      const room = store.createRoom({
        difficulty: body.data.difficulty,
        season: body.data.season,
      });
      const joined = room.join(body.data.name, body.data.role);
      if (!joined.ok) return end(res, 409, { error: joined.reason });

      return end(res, 200, {
        code: room.code,
        memberId: joined.memberId,
        token: store.issueToken(room.code, joined.memberId),
      });
    }

    // GET /records — 리더보드 · 기록
    if (req.method === "GET" && url.pathname === "/records") {
      const limit = Number(url.searchParams.get("limit") ?? 50);
      return end(res, 200, await store.persistence.listRecords(limit));
    }

    // GET /records/:runId — 하달 장부 (QST-05)
    if (req.method === "GET" && parts[0] === "records" && parts[1]) {
      const record = await store.persistence.getRecord(parts[1]);
      if (!record) return end(res, 404, { error: "recordNotFound" });
      return end(res, 200, record);
    }

    // GET /rooms/:code — 로비 상태. 없으면 저장된 스냅샷에서 되살린다 (17.0)
    if (req.method === "GET" && parts[0] === "rooms" && parts[1]) {
      const room = store.get(parts[1]) ?? (await store.resume(parts[1]));
      if (!room) return end(res, 404, { error: "roomNotFound" });
      return end(res, 200, room.lobbyState());
    }

    // POST /rooms/:code/join — 보직을 골라 입장
    if (req.method === "POST" && parts[0] === "rooms" && parts[1] && parts[2] === "join") {
      const room = store.get(parts[1]);
      if (!room) return end(res, 404, { error: "roomNotFound" });

      const body = joinRoomRequestSchema.safeParse(await readJson(req));
      if (!body.success) return end(res, 400, { error: "invalidBody" });

      const joined = room.join(body.data.name, body.data.role);
      if (!joined.ok) return end(res, 409, { error: joined.reason });

      room.broadcastLobby();
      return end(res, 200, {
        code: room.code,
        memberId: joined.memberId,
        token: store.issueToken(room.code, joined.memberId),
      });
    }

    // POST /rooms/:code/start — 방장만
    if (req.method === "POST" && parts[0] === "rooms" && parts[1] && parts[2] === "start") {
      const room = store.get(parts[1]);
      if (!room) return end(res, 404, { error: "roomNotFound" });

      const token = url.searchParams.get("token") ?? "";
      const resolved = store.resolve(token);
      if (!resolved || resolved.room.code !== room.code) {
        return end(res, 401, { error: "invalidToken" });
      }
      if (room.hostId !== resolved.session.memberId) {
        return end(res, 403, { error: "notHost" });
      }
      if (!room.start()) return end(res, 409, { error: "alreadyStarted" });

      return end(res, 200, { ok: true });
    }

    return end(res, 404, { error: "notFound" });
  } catch {
    return end(res, 500, { error: "internal" });
  }
});

/* -------------------------------------------------------------------- WS */

const wss = new WebSocketServer({ server, path: "/ws" });

wss.on("connection", (socket, request) => {
  const url = new URL(request.url ?? "/", `http://${request.headers.host}`);
  const token = url.searchParams.get("token") ?? "";
  const resolved = store.resolve(token);

  if (!resolved) {
    socket.send(
      JSON.stringify({
        type: "error",
        code: "invalidToken",
        message: "로비에서 발급한 토큰이 필요하다",
      } satisfies ServerMessage),
    );
    socket.close();
    return;
  }

  const { room, session } = resolved;
  sockets.set(session.memberId, socket);
  room.reconnect(session.memberId);

  socket.send(
    JSON.stringify({
      type: "welcome",
      protocolVersion: PROTOCOL_VERSION,
      memberId: session.memberId,
      code: room.code,
    } satisfies ServerMessage),
  );
  room.broadcastLobby();

  socket.on("message", (raw) => {
    let parsed: unknown;
    try {
      parsed = JSON.parse(raw.toString());
    } catch {
      return;
    }
    // 스키마를 통과하지 못한 입력은 조용히 버린다 — 클라를 신뢰하지 않는다
    const intent = parseIntent(parsed);
    if (!intent) return;
    room.handleIntent(session.memberId, intent);
  });

  socket.on("close", () => {
    sockets.delete(session.memberId);
    room.disconnect(session.memberId);
  });
});

/* ------------------------------------------------------------------ 루프 */

let lastTick = Date.now();
setInterval(() => {
  const now = Date.now();
  const elapsed = now - lastTick;
  lastTick = now;

  for (const room of store.active()) {
    room.tick(elapsed);
  }
}, TICK_MS);

setInterval(() => store.sweep(), 60_000);

server.listen(PORT, () => {
  console.log(`[gameserver] http+ws :${PORT} — 시뮬 ${1000 / TICK_MS}Hz`);
});

/* ----------------------------------------------------------------- 유틸 */

function cors(res: ServerResponse): void {
  res.setHeader("Access-Control-Allow-Origin", process.env.CORS_ORIGIN ?? "*");
  res.setHeader("Access-Control-Allow-Methods", "GET,POST,OPTIONS");
  res.setHeader("Access-Control-Allow-Headers", "content-type");
}

function end(res: ServerResponse, status: number, body: unknown): void {
  res.statusCode = status;
  if (body === null) {
    res.end();
    return;
  }
  res.setHeader("content-type", "application/json; charset=utf-8");
  res.end(JSON.stringify(body));
}

async function readJson(req: IncomingMessage): Promise<unknown> {
  const chunks: Buffer[] = [];
  for await (const chunk of req) chunks.push(chunk as Buffer);
  if (chunks.length === 0) return {};
  return JSON.parse(Buffer.concat(chunks).toString());
}
