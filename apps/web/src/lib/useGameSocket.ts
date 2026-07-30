"use client";

import { useCallback, useEffect, useRef, useState } from "react";
import {
  parseServerMessage,
  type Intent,
  type LobbyState,
  type ServerEvent,
  type Snapshot,
} from "@sad/protocol";
import { WS_BASE } from "./api";

export type SocketStatus = "connecting" | "open" | "closed" | "error";

export interface GameSocket {
  status: SocketStatus;
  snapshot: Snapshot | null;
  lobby: LobbyState | null;
  events: TimedEvent[];
  memberId: string | null;
  send: (intent: Intent) => void;
  errorMessage: string | null;
}

export interface TimedEvent {
  readonly key: number;
  readonly event: ServerEvent;
}

const MAX_LOG = 40;

/**
 * 서버가 보내는 스냅샷을 그대로 그린다.
 *
 * 이 훅은 규칙을 하나도 갖지 않는다 — 남은 시간을 스스로 세지도, 완료를 예측하지도 않는다.
 * 클라이언트가 판정을 재현할 수 있으면 언젠가 재현하고, 그 순간 서버 권위가 무너진다(ARCH-02).
 * 화면이 부드럽게 흐르는 것은 스냅샷 사이를 보간하는 것으로 충분하다.
 */
export function useGameSocket(token: string | null): GameSocket {
  const [status, setStatus] = useState<SocketStatus>("connecting");
  const [snapshot, setSnapshot] = useState<Snapshot | null>(null);
  const [lobby, setLobby] = useState<LobbyState | null>(null);
  const [events, setEvents] = useState<TimedEvent[]>([]);
  const [memberId, setMemberId] = useState<string | null>(null);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);

  const socketRef = useRef<WebSocket | null>(null);
  const seqRef = useRef(0);
  const counterRef = useRef(0);

  useEffect(() => {
    if (!token) return;

    const socket = new WebSocket(`${WS_BASE}?token=${encodeURIComponent(token)}`);
    socketRef.current = socket;
    setStatus("connecting");

    socket.onopen = () => setStatus("open");
    socket.onclose = () => setStatus("closed");
    socket.onerror = () => setStatus("error");

    socket.onmessage = (raw) => {
      let parsed: unknown;
      try {
        parsed = JSON.parse(raw.data as string);
      } catch {
        return;
      }
      const message = parseServerMessage(parsed);
      if (!message) return;

      switch (message.type) {
        case "welcome":
          setMemberId(message.memberId);
          break;
        case "lobby":
          setLobby(message);
          break;
        case "snapshot":
          // 늦게 도착한 스냅샷은 버린다
          if (message.seq < seqRef.current) return;
          seqRef.current = message.seq;
          setSnapshot(message);
          break;
        case "events":
          setEvents((previous) => {
            const added = message.items.map((event) => ({
              key: (counterRef.current += 1),
              event,
            }));
            return [...previous, ...added].slice(-MAX_LOG);
          });
          break;
        case "error":
          setErrorMessage(message.message);
          break;
      }
    };

    return () => {
      socket.close();
      socketRef.current = null;
    };
  }, [token]);

  const send = useCallback((intent: Intent) => {
    const socket = socketRef.current;
    if (!socket || socket.readyState !== WebSocket.OPEN) return;
    socket.send(JSON.stringify(intent));
  }, []);

  return { status, snapshot, lobby, events, memberId, send, errorMessage };
}

/**
 * 스냅샷은 10Hz로 온다. 그 사이를 로컬 시계로 메워 타이머가 끊겨 보이지 않게 한다.
 * 표시용 보간일 뿐이며 판정에는 아무 영향이 없다 — 서버가 끝났다고 할 때 끝난다.
 */
export function useSmoothClock(snapshot: Snapshot | null): number {
  const [now, setNow] = useState(0);
  const baseRef = useRef({ elapsed: 0, at: 0 });

  useEffect(() => {
    if (!snapshot) return;
    baseRef.current = { elapsed: snapshot.phase.elapsedMs, at: performance.now() };
    setNow(snapshot.phase.elapsedMs);
  }, [snapshot]);

  useEffect(() => {
    let frame = 0;
    const tick = () => {
      const base = baseRef.current;
      setNow(base.elapsed + (performance.now() - base.at));
      frame = requestAnimationFrame(tick);
    };
    frame = requestAnimationFrame(tick);
    return () => cancelAnimationFrame(frame);
  }, []);

  return now;
}
