import { createRun, type CreateRunOptions, type RunState } from "../src/index.js";

/** 4인 정원이 모두 찬 표준 분대 */
export function fullSquad(overrides: Partial<CreateRunOptions> = {}): RunState {
  return createRun({
    runId: "test-run",
    seed: 1234,
    members: [
      { id: "p1", name: "김소총", role: "rifle" },
      { id: "p2", name: "이통신", role: "comms" },
      { id: "p3", name: "박의무", role: "medic" },
      { id: "p4", name: "최행정", role: "admin" },
    ],
    ...overrides,
  });
}
