import { describe, expect, it } from "vitest";
import {
  LEADER_RELIEF_LIMIT,
  OFFICER_RELIEF_LIMIT,
  OFFICER_RELIEF_TRUST_THRESHOLD,
  judgeDay,
  step,
  type Quest,
  type RunState,
} from "../src/index.js";
import { FULL_DAY, SECOND, beginDay, fullSquad, playDays, toPhase, withQuests } from "./helpers.js";

function quest(overrides: Partial<Quest> = {}): Quest {
  return {
    id: "q",
    kind: "role",
    label: "총기 수입",
    training: null,
    ownerId: "p1",
    required: true,
    phase: "morning",
    zone: "Z01",
    spot: null,
    jointTotal: 0,
    jointDone: 0,
    jointAsymmetric: false,
    workMs: 10 * SECOND,
    workedMs: 0,
    minActors: 1,
    status: "pending",
    delegatedFrom: null,
    minigame: null,
    grade: null,
    ...overrides,
  };
}

/**
 * 하루를 통째로 흘려보내 점호까지 도달시킨다.
 * 배정기가 만든 일과는 지우고 테스트가 준 퀘스트만 남긴다 — 판정 규칙만 보기 위해서다.
 */
function playDay(state: RunState, quests: readonly Quest[] = []): RunState {
  const started = withQuests(beginDay(state), quests);
  return step(started, { type: "tick", elapsedMs: FULL_DAY }).state;
}

describe("JDG-01 판정 조건", () => {
  it("필수를 전부 끝내면 통과한다", () => {
    const state = fullSquad();
    state.quests = [quest({ id: "a", status: "done" }), quest({ id: "b", status: "done" })];
    const judgement = judgeDay(state);
    expect(judgement.passed).toBe(true);
    expect(judgement.requiredDone).toBe(2);
    expect(judgement.failedAt).toBeNull();
  });

  it("선택 퀘스트 미완은 판정에 영향을 주지 않는다", () => {
    const state = fullSquad();
    state.quests = [quest({ id: "a", status: "done" }), quest({ id: "b", required: false })];
    expect(judgeDay(state).passed).toBe(true);
  });

  it("조건 B — 합동 퀘스트는 70% 부분 성공까지 인정한다", () => {
    const state = fullSquad();
    state.quests = [
      quest({ id: "j", kind: "joint", ownerId: null, required: false, workedMs: 7 * SECOND }),
    ];
    expect(judgeDay(state).passed).toBe(true);

    state.quests = [
      quest({ id: "j", kind: "joint", ownerId: null, required: false, workedMs: 6.9 * SECOND }),
    ];
    expect(judgeDay(state).failedAt).toBe("B");
  });

  it("조건 C — 군기 40 미만이면 실패한다", () => {
    const state = fullSquad();
    state.discipline = 39;
    expect(judgeDay(state).failedAt).toBe("C");
    state.discipline = 40;
    expect(judgeDay(state).passed).toBe(true);
  });

  it("조건 D — 청결 20 미만이면 실패한다", () => {
    const state = fullSquad();
    const member = state.members[0];
    if (!member) throw new Error("분대원 없음");
    member.stats.hygiene = 19;
    expect(judgeDay(state).failedAt).toBe("D");
  });

  it("NPC 대리의 청결은 조건 D 대상이 아니다", () => {
    const state = fullSquad({ members: [{ id: "p1", name: "김소총", role: "rifle" }] });
    for (const member of state.members) {
      if (member.presence === "npcVacant") member.stats.hygiene = 0;
    }
    expect(judgeDay(state).passed).toBe(true);
  });

  it("깨진 첫 조건만 지목한다", () => {
    const state = fullSquad();
    state.quests = [quest({ id: "a" })];
    state.discipline = 0;
    expect(judgeDay(state).failedAt).toBe("A");
  });
});

/**
 * B-4 — 구제권을 자동 상쇄에서 발동형으로.
 *
 * `relief.ts`(발동 자격·거부 사유)의 단위 테스트는 `relief.test.ts`에 있다. 여기서는
 * judge.ts가 더 이상 스스로 아무것도 결정하지 않는다는 것 — 분대장 몫은 판정에
 * 닿기도 전에 이미 끝나 있고, 간부 몫은 `officerReliefArmedToday`를 읽기만
 * 한다는 것 — 을 검증한다.
 */
describe("구제권 (총량 3회 — 발동형)", () => {
  it("자동 상쇄는 없다 — 아무도 발동하지 않으면 미완료 필수 1건도 런을 끝낸다", () => {
    let state = fullSquad();
    state = playDay(state, [quest({ id: "a" })]);

    expect(state.status).toBe("discharged");
    expect(state.judgements[0]?.failedAt).toBe("A");
    expect(state.judgements[0]?.reliefsUsed).toBe(0);
    // 총량은 안 건드렸으니 그대로다 — 쓰지 않은 구제는 줄지 않는다
    expect(state.reliefsRemaining).toBe(LEADER_RELIEF_LIMIT + OFFICER_RELIEF_LIMIT);
  });

  it("분대장이 판정 전에 필수를 봐주면 그 몫만큼 판정을 통과한다", () => {
    let state = withQuests(beginDay(fullSquad()), [
      quest({ id: "a", status: "done" }),
      quest({ id: "b" }),
    ]);
    state.leaderId = "p1";
    state = step(state, { type: "useRelief", leaderId: "p1", questId: "b" }).state;
    state = step(state, { type: "tick", elapsedMs: FULL_DAY }).state;

    expect(state.status).toBe("running");
    expect(state.day).toBe(2);
    expect(state.leaderReliefsRemaining).toBe(LEADER_RELIEF_LIMIT - 1);
    expect(state.reliefsRemaining).toBe(LEADER_RELIEF_LIMIT - 1 + OFFICER_RELIEF_LIMIT);
    // 분대장 몫은 발동 시점에 이미 소모됐다 — 판정의 reliefsUsed는 간부 몫만 잡는다
    expect(state.judgements[0]?.reliefsUsed).toBe(0);
  });

  it("간부 구제가 저녁 개인정비에 발동되면 그날 미달 1건을 상쇄하고 총량이 준다", () => {
    let state = withQuests(beginDay(fullSquad()), [
      quest({ id: "a", status: "done" }),
      quest({ id: "b" }),
    ]);
    state = toPhase(state, "personal");
    state.trust.sergeantMajor = OFFICER_RELIEF_TRUST_THRESHOLD;

    const granted = step(state, { type: "useOfficerRelief", memberId: "p1" });
    expect(granted.effects.some((e) => e.type === "reliefGranted")).toBe(true);
    state = granted.state;

    state = step(state, { type: "skipPhase" }).state; // 개인정비 종료 → 점호 칸
    state = step(state, { type: "skipPhase" }).state; // 점호 종료 → 판정

    expect(state.status).toBe("running");
    expect(state.day).toBe(2);
    expect(state.officerReliefsRemaining).toBe(OFFICER_RELIEF_LIMIT - 1);
    expect(state.reliefsRemaining).toBe(LEADER_RELIEF_LIMIT + OFFICER_RELIEF_LIMIT - 1);
    expect(state.judgements[0]?.reliefsUsed).toBe(1);
  });

  it("간부 구제가 발동된 날은 조건 A의 미달 1건을 상쇄한다 (판정 순수 함수 단위)", () => {
    const state = fullSquad();
    state.officerReliefArmedToday = true;
    state.quests = [quest({ id: "a", status: "done" }), quest({ id: "b" })];

    const judgement = judgeDay(state);
    expect(judgement.passed).toBe(true);
    expect(judgement.reliefsUsed).toBe(1);
  });

  it("발동됐어도 그날 미달이 없으면 소모되지 않는다", () => {
    const state = fullSquad();
    state.officerReliefArmedToday = true;
    state.quests = [quest({ id: "a", status: "done" })];

    const judgement = judgeDay(state);
    expect(judgement.passed).toBe(true);
    expect(judgement.reliefsUsed).toBe(0);
  });

  it("발동돼도 미달이 여러 건이면 판정은 실패하고 그 몫은 소모되지 않는다", () => {
    const state = fullSquad();
    state.officerReliefArmedToday = true;
    state.quests = [quest({ id: "a" }), quest({ id: "b" }), quest({ id: "c" }), quest({ id: "d" })];

    const judgement = judgeDay(state);
    expect(judgement.passed).toBe(false);
    expect(judgement.reliefsUsed).toBe(0);
  });

  it("간부 구제는 조건 A에만 쓰이고 군기는 구제하지 못한다", () => {
    const state = fullSquad();
    state.officerReliefArmedToday = true;
    state.discipline = 10;

    expect(judgeDay(state).failedAt).toBe("C");
    expect(judgeDay(state).reliefsUsed).toBe(0);
  });
});

describe("난이도", () => {
  it("정규 — 1회 미달로 즉시 종료된다", () => {
    let state = fullSquad({ config: { difficulty: "regular" } });
    state = playDay(state, [quest({ id: "a" })]);
    expect(state.status).toBe("discharged");
  });

  it("완화 — 1차는 경고, 3차에 종료된다", () => {
    let state = fullSquad({ config: { difficulty: "relaxed" } });

    state = playDay(state, [quest({ id: "a" })]);
    expect(state.status).toBe("running");
    expect(state.warnings).toBe(1);

    state = playDay(state, [quest({ id: "b" })]);
    expect(state.status).toBe("running");
    expect(state.personalTimeRevoked).toBe(true);

    state = playDay(state, [quest({ id: "c" })]);
    expect(state.status).toBe("discharged");
    expect(state.warnings).toBe(3);
  });
});

describe("런 종료", () => {
  it("완화 난이도라도 마지막 날 판정을 놓치면 전역하지 못한다", () => {
    let state = fullSquad({ config: { difficulty: "relaxed" } });
    state.day = 18;
    state = playDay(state, [quest({ id: "a" })]);

    expect(state.status).toBe("discharged");
    expect(state.day).toBe(18);
    expect(state.warnings).toBe(1);
  });

  it("18일차 판정을 통과하면 전역한다", () => {
    const state = playDays(fullSquad(), 18);
    expect(state.status).toBe("cleared");
    expect(state.judgements).toHaveLength(18);
    expect(state.judgements.every((j) => j.passed)).toBe(true);
  });

  it("실제 플레이어가 2명 미만이 되면 분대가 해체된다", () => {
    let state = fullSquad();
    for (const member of state.members.slice(1)) {
      member.presence = "npcLeave";
    }
    state = playDays(state, 1);
    expect(state.status).toBe("disbanded");
  });

  it("처음부터 1인으로 시작한 방은 해체 대상이 아니다", () => {
    let state = fullSquad({ members: [{ id: "p1", name: "김소총", role: "rifle" }] });
    state = playDays(state, 1);
    expect(state.status).toBe("running");
  });

  it("런이 끝난 뒤의 이벤트는 상태를 바꾸지 않는다", () => {
    let state = fullSquad();
    state = playDay(state, [quest({ id: "a" })]);

    const after = step(state, { type: "tick", elapsedMs: FULL_DAY }).state;
    expect(after.day).toBe(state.day);
    expect(after.status).toBe("discharged");
  });
});

/**
 * F-2(WORKORDER) 잔여 — 세션 종료 화면의 "내일 예고".
 *
 * 새 프로토콜 필드를 만들지 않고 기존 `log` 이펙트(문자열 한 줄)에 실어
 * 보낸다(`judge.ts` `pushTomorrowPreview` 주석 참고 — Unity `Generated/Protocol.cs`
 * 재생성이 금지돼 있어 새 필드를 만들어도 지금은 읽을 방법이 없다). 접두어
 * `내일 예고 |`로 골라낼 수 있어야 하고, 확률의 소수점이 아니라 방향·확정
 * 사실만 담겨야 한다.
 */
describe("F-2 — 내일 예고", () => {
  function tomorrowPreviewLog(effects: readonly { type: string; message?: string }[]) {
    return effects.find(
      (e) => e.type === "log" && typeof e.message === "string" && e.message.startsWith("내일 예고 |"),
    ) as { type: "log"; message: string } | undefined;
  }

  it("런이 다음 날로 이어지면 내일 예고 로그가 뜬다", () => {
    const state = withQuests(beginDay(fullSquad()), []);
    const result = step(state, { type: "tick", elapsedMs: FULL_DAY });

    expect(result.state.status).toBe("running");
    const preview = tomorrowPreviewLog(result.effects);
    expect(preview).toBeDefined();
    // 날씨 힌트는 방향 문구지 숫자가 아니다 — 소수점이 새면 안 된다
    expect(preview!.message).not.toMatch(/\d\.\d/);
  });

  it("내일 열리는 게 있으면 이름이 그대로 들어간다 — D-1 다음 날(D-2)은 '보직 퀘스트'가 열린다", () => {
    let state = fullSquad();
    state.day = 1;
    state = withQuests(beginDay(state), []);
    const result = step(state, { type: "tick", elapsedMs: FULL_DAY });

    const preview = tomorrowPreviewLog(result.effects);
    expect(preview?.message).toContain("보직 퀘스트");
  });

  it("런이 여기서 끝나면(전역·퇴소) 내일 예고를 심지 않는다", () => {
    const state = fullSquad({ config: { difficulty: "regular" } });
    const started = withQuests(beginDay(state), [quest({ id: "a" })]); // 필수 미완 → 즉시 퇴소
    const result = step(started, { type: "tick", elapsedMs: FULL_DAY });

    expect(result.state.status).toBe("discharged");
    expect(tomorrowPreviewLog(result.effects)).toBeUndefined();
  });

  it("18일차를 통과해 전역하면 내일 예고를 심지 않는다 — 내일이 없다", () => {
    let state = fullSquad();
    state.day = 18;
    state = withQuests(beginDay(state), []);
    const result = step(state, { type: "tick", elapsedMs: FULL_DAY });

    expect(result.state.status).toBe("cleared");
    expect(tomorrowPreviewLog(result.effects)).toBeUndefined();
  });
});
