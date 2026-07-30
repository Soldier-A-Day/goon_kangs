import type { LobbyState, Session } from "@sad/protocol";

/**
 * 게임 서버 클라이언트.
 *
 * 웹 셸은 3D도 규칙도 갖지 않는다 — 방을 만들고 토큰을 받아 넘겨주는 것까지가 역할이다
 * (ARCH-02 핸드오프). 게임 서버는 상시 구동 Node 프로세스라 이 앱과 오리진이 다르다.
 */
export const HTTP_BASE =
  process.env.NEXT_PUBLIC_GAME_HTTP_URL ?? "http://localhost:8080";

export const WS_BASE = process.env.NEXT_PUBLIC_GAME_WS_URL ?? "ws://localhost:8080/ws";

export type Role = "rifle" | "comms" | "medic" | "admin";

export const ROLE_LABELS: Record<Role, { name: string; code: string; duty: string }> = {
  rifle: { name: "소총수", code: "RIFLE", duty: "전투 · 경계 · 기동" },
  comms: { name: "통신병", code: "COMMS", duty: "정보 전달" },
  medic: { name: "의무병", code: "MEDIC", duty: "컨디션 전반" },
  admin: { name: "행정병", code: "ADMIN", duty: "정보 관리 · 정비" },
};

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await fetch(`${HTTP_BASE}${path}`, {
    ...init,
    headers: { "content-type": "application/json", ...init?.headers },
  });
  const body = (await response.json()) as T & { error?: string };
  if (!response.ok) throw new Error(body.error ?? `요청 실패 (${response.status})`);
  return body;
}

export function createRoom(input: {
  name: string;
  role: Role;
  difficulty: "regular" | "relaxed";
  season: "cold" | "hot" | "random";
}): Promise<Session> {
  return request<Session>("/rooms", {
    method: "POST",
    body: JSON.stringify(input),
  });
}

export function joinRoom(code: string, input: { name: string; role: Role }): Promise<Session> {
  return request<Session>(`/rooms/${code.toUpperCase()}/join`, {
    method: "POST",
    body: JSON.stringify(input),
  });
}

export function fetchLobby(code: string): Promise<LobbyState> {
  return request<LobbyState>(`/rooms/${code.toUpperCase()}`, { cache: "no-store" });
}

export function startRun(code: string, token: string): Promise<{ ok: boolean }> {
  return request<{ ok: boolean }>(
    `/rooms/${code.toUpperCase()}/start?token=${encodeURIComponent(token)}`,
    { method: "POST" },
  );
}
