"use client";

const KEY = "sad.playerName";

/**
 * 마지막으로 쓴 이름을 브라우저에 들고 있는다.
 *
 * `session.ts`의 세션은 "이 방에 들어와 있다"는 사실이고, 이건 그보다 오래
 * 산다 — 방을 나가고 다른 방에 다시 들어와도 이름은 남아 있어야 한다.
 * 초대받은 사람이 두 번째 방문부터는 이름 칸을 건드릴 필요가 없게 만드는
 * 것이 H-1의 3단계(링크 탭 → 이름 → 입장) 중 "이름" 한 칸을 없애는 길이다.
 */
export function saveName(name: string): void {
  const trimmed = name.trim();
  if (!trimmed) return;
  localStorage.setItem(KEY, trimmed);
}

export function loadName(): string {
  if (typeof window === "undefined") return "";
  try {
    return localStorage.getItem(KEY) ?? "";
  } catch {
    return "";
  }
}
