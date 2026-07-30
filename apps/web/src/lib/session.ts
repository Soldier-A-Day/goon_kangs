"use client";

import type { Session } from "@sad/protocol";

const KEY = "sad.session";

/**
 * 로비가 발급한 세션을 브라우저에 들고 있는다.
 * 이 토큰이 있어야 WS에 붙을 수 있고, 없으면 게임 화면은 로비로 돌려보낸다.
 */
export function saveSession(session: Session): void {
  localStorage.setItem(KEY, JSON.stringify(session));
}

export function loadSession(): Session | null {
  if (typeof window === "undefined") return null;
  const raw = localStorage.getItem(KEY);
  if (!raw) return null;
  try {
    return JSON.parse(raw) as Session;
  } catch {
    return null;
  }
}

export function clearSession(): void {
  localStorage.removeItem(KEY);
}
