/**
 * M0 힙 측정용 스냅샷 표본 생성기.
 *
 * 100분 힙 측정은 정적 씬으로는 성립하지 않는다 — 할당이 없으면 힙이 평평한 게
 * 당연하고, 그건 누수가 없다는 증거가 아니라 아무것도 재지 않았다는 뜻이다.
 *
 * 기획서가 힙을 "완화 불가"로 둔 이유는 10Hz 스냅샷이다. 100분이면 6만 번
 * 역직렬화한다. 그래서 **실제 서버가 내보내는 스냅샷 그대로**를 표본으로 박아
 * 클라이언트가 그것을 6만 번 파싱하게 한다. 손으로 만든 가짜 JSON을 쓰면
 * 필드 수·배열 길이·문자열 길이가 달라져 할당량이 실제와 어긋난다.
 */
import { writeFileSync } from "node:fs";
import { projectSnapshot } from "../../services/gameserver/src/snapshot.js";
import { createRun, step, type RunState, type SimEvent } from "../../packages/sim/src/index.js";

let state: RunState = createRun({
  runId: "m0-heap",
  seed: 20260731,
  members: [
    { id: "p1", name: "일병 김", role: "rifle" },
    { id: "p2", name: "일병 이", role: "comms" },
    { id: "p3", name: "일병 박", role: "medic" },
    { id: "p4", name: "일병 최", role: "admin" },
  ],
});

// 퀘스트·이벤트·군기가 채워진 중간 상태로 밀어둔다. 갓 생성한 런은 배열이
// 비어 있어 실제보다 가벼운 스냅샷이 나온다.
const advance = (event: SimEvent) => {
  state = step(state, event).state;
};
advance({ type: "beginDay" });
for (let i = 0; i < 600; i += 1) advance({ type: "tick", elapsedMs: 1000 });

const snapshot = projectSnapshot(state, 12_345);
const json = JSON.stringify(snapshot);

writeFileSync("unity/Assets/Resources/m0_snapshot.json", json);
console.log(
  `표본 생성 — ${json.length}바이트 · 일차 ${snapshot.day} · ` +
  `멤버 ${snapshot.members.length} · 퀘스트 ${snapshot.quests.length}`,
);
