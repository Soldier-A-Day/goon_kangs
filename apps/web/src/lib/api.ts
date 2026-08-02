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

/**
 * 보직 4종 (§3.0).
 *
 * `fail`은 **그 보직이 비었을 때 분대가 무엇을 잃는가**다. 목업 §11이 이걸
 * 카드마다 붉게 적어둔 이유는, 보직을 고르는 순간이 "뭘 하고 싶은가"가 아니라
 * "누가 빠지면 안 되는가"를 정하는 순간이기 때문이다.
 */
export const ROLE_LABELS: Record<
  Role,
  { name: string; code: string; duty: string; fail: string; tint: string }
> = {
  rifle: {
    name: "소총수", code: "RFL", duty: "전투 · 경계 · 기동",
    fail: "실패 시 기습 이벤트 2배", tint: "var(--role-rifle)",
  },
  comms: {
    name: "통신병", code: "COM", duty: "정보 전달",
    fail: "실패 시 미니맵 마커 소멸", tint: "var(--role-comms)",
  },
  medic: {
    name: "의무병", code: "MED", duty: "컨디션 전반 ← 취사 흡수",
    fail: "실패 시 전원 수분 소모 2배", tint: "var(--role-medic)",
  },
  admin: {
    name: "행정병", code: "ADM", duty: "정보 관리 · 정비 ← 정비 흡수",
    fail: "실패 시 혹한 보온 −50%", tint: "var(--role-admin)",
  },
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
