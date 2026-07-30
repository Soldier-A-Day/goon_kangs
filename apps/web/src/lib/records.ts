/**
 * 런 기록의 표시용 타입.
 *
 * sim의 `RunRecord`와 같은 모양이지만 web은 sim을 참조할 수 없으므로(ARCH-02) 여기서 다시 쓴다.
 * 이 중복은 의도적이다 — 참조를 열어주면 클라이언트가 규칙을 들여다보기 시작한다.
 */
export interface RunRecord {
  readonly runId: string;
  readonly finishedAtDay: number;
  readonly status: "running" | "cleared" | "discharged" | "disbanded";
  readonly season: "cold" | "hot";
  readonly difficulty: string;
  readonly ending: { readonly id: string; readonly label: string } | null;
  readonly discipline: number;
  readonly hidden: readonly string[];
  readonly failedAt: string | null;
  readonly members: readonly {
    readonly name: string;
    readonly role: string;
    readonly rank: string;
    readonly serviceScore: number;
    readonly evacuations: number;
    readonly delegationsGiven: number;
    readonly delegationsReceived: number;
  }[];
}

export const RANK_LABELS: Record<string, string> = {
  private: "이병",
  pfc: "일병",
  corporal: "상병",
  sergeant: "병장",
};

export const ENDING_LABELS: Record<string, string> = {
  exemplary: "모범 전역",
  record: "분대 기록 엔딩",
  barely: "간신히 전역",
  normal: "정상 전역",
};

export const HIDDEN_LABELS: Record<string, string> = {
  flawlessDay: "완전한 하루",
  coldSnap: "혹한을 넘다",
  hydrated: "쓰러지지 않는다",
  ledgerClean: "제 몫은 제가",
  proxyKing: "대행의 달인",
  steadfast: "우수분대",
};
