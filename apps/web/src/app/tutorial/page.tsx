"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { useEffect, useMemo, useState } from "react";

/**
 * 튜토리얼 (H2 발주).
 *
 * **게임 안에서 따라다니며 뜨던 8단계 카드를 걷어내고, 여기로 옮겼다.**
 * 사용자 지시 원문 — "온보딩 그냥 저따구로 하지 말고, 그냥 로비에서 튜토리얼
 * 누르면 하나하나 알려주는 걸로 바꿔." 즉 게임에 들어가기 **전에**, 원하는
 * 만큼 넘겨 보고 원하는 시점에 그만둘 수 있는 화면이다. Unity 쪽에 남는 것은
 * 언제든 다시 열어 보는 전체 키 안내 창(`1`) 하나뿐이고, 그 표가 이 화면의
 * 뼈대다(`unity/Assets/Scripts/Net/Tutorial.cs`의 `KeyGuideEntries`와 같은
 * 조작 목록을 쓴다 — 두 화면이 다른 소리를 하면 안 되기 때문이다).
 *
 * 진행 상태를 저장하지 않는다. 로비에서 언제나 다시 열 수 있으므로 "몇 단계
 * 까지 봤는지"를 기억할 이유가 없다 — 예전 `PlayerPrefs`·설정의 "다시 보기"
 * 신호가 통째로 사라진 이유와 같다.
 */
type Step = {
  kind: "controls" | "flow" | "info";
  title: string;
  keys?: string[];
  body: string[];
};

const STEPS: Step[] = [
  {
    kind: "info",
    title: "시작하기 전에",
    body: [
      "SOLDIER : A DAY는 4인이 함께 18일을 버티는 협동 생활관 시뮬레이션이다.",
      "여기서 조작과 하루의 흐름을 먼저 익힌다 — 게임 안에서는 안내 카드가 따로 뜨지 않는다.",
      "언제든 건너뛰어도 된다. 이 화면은 로비에서 몇 번이고 다시 열 수 있다.",
    ],
  },
  {
    kind: "controls",
    title: "이동",
    keys: ["W", "A", "S", "D"],
    body: ["상하좌우로 걷는다. 방향키도 똑같이 먹는다."],
  },
  {
    kind: "controls",
    title: "달리기",
    keys: ["SHIFT"],
    body: ["이동 중 누르고 있으면 뛴다."],
  },
  {
    kind: "controls",
    title: "상호작용",
    keys: ["E"],
    body: ["목표 앞에서 E를 누르면 일과가 시작된다.", "구조처럼 시간이 걸리는 동작은 E를 누르고 있는다(홀드)."],
  },
  {
    kind: "controls",
    title: "통합 수첩",
    keys: ["TAB"],
    body: [
      "TAB을 누를 때마다 일과표 → 일과요약 → 기록 → 닫힘 순서로 넘어간다.",
      "일과표에서 오늘 할 일을, 일과요약에서 필수/선택 진행 상황을 확인한다.",
    ],
  },
  {
    kind: "controls",
    title: "지도",
    keys: ["M"],
    body: ["미니맵을 전체화면 지도로 연다."],
  },
  {
    kind: "controls",
    title: "퀵 커맨드",
    keys: ["Q", "1", "~", "8"],
    body: [
      "Q를 누른 채 숫자 1~8로 분대에 전술 지시를 내린다.",
      "타이핑 없이 협동 퀘스트 대부분을 여기서 해결한다.",
    ],
  },
  {
    kind: "controls",
    title: "정형 문구",
    keys: ["C", "1", "~", "8"],
    body: ["C를 누른 채 숫자 1~8로 짧은 정형 문구를 발화한다."],
  },
  {
    kind: "controls",
    title: "컨디션 수치",
    keys: ["ALT"],
    body: ["누르고 있는 동안 체력 · 정신력 등 컨디션 수치가 화면에 보인다."],
  },
  {
    kind: "controls",
    title: "창 닫기",
    keys: ["ESC"],
    body: ["열려 있는 창을 닫는다."],
  },
  {
    kind: "controls",
    title: "미니게임 · 확인",
    keys: ["SPACE", "ENTER"],
    body: ["대부분의 미니게임 판과 확인 창에서 공통으로 쓴다(판마다 조작이 조금씩 다를 수 있다)."],
  },
  {
    kind: "controls",
    title: "방독면 훈련",
    keys: ["G"],
    body: ["화생방 훈련일에 G로 방독면 착용 절차를 시작한다."],
  },
  {
    kind: "controls",
    title: "시간대 스킵",
    body: [
      "키가 아니라 화면의 [투표하기]를 마우스로 누른다.",
      "분대원 다수가 동의하면 남은 시간대를 건너뛴다.",
    ],
  },
  {
    kind: "flow",
    title: "하루는 6개 시간대",
    body: [
      "하루가 6개 시간대로 나뉘어 흐른다. 시간이 다 되면 자동으로 다음 시간대로 넘어간다.",
      "다 같이 원하면 [투표하기]로 건너뛸 수도 있다.",
    ],
  },
  {
    kind: "flow",
    title: "필수 · 선택 일과",
    body: [
      "시간대마다 할 일이 뜬다. TAB으로 일과표를 열면 필수와 선택이 나뉘어 보인다.",
      "필수를 다 못 채우면 그날 밤 점호에서 걸린다. 선택은 여유가 있을 때 추가로 한다.",
    ],
  },
  {
    kind: "flow",
    title: "점호 판정",
    body: [
      "하루가 끝나면 점호에서 그날 성과가 판정된다.",
      "정규 난이도는 필수 미달 1회로 런이 끝나고, 완화 난이도는 3회까지 버틴다(보상은 60%로 줄어든다).",
    ],
  },
  {
    kind: "flow",
    title: "다음 날로",
    body: [
      "판정 요약을 확인해야 다음 날로 넘어간다.",
      "이렇게 하루하루를 쌓아 18일을 완주하면 런이 끝난다.",
    ],
  },
  {
    kind: "info",
    title: "다 됐다",
    body: [
      "게임 중에는 언제든 1번 키를 눌러 방금 본 조작 키 전체 목록을 다시 볼 수 있다.",
      "Q나 C를 누르고 있는 동안, 그리고 미니게임 판이 열려 있는 동안에는 1번 키가 그 판에 쓰이므로 안내 창이 끼어들지 않는다.",
      "이제 로비로 돌아가 분대를 편성하면 된다.",
    ],
  },
];

export default function TutorialPage() {
  const router = useRouter();
  const [index, setIndex] = useState(0);
  // `index`는 항상 0..STEPS.length-1로 클램프되어 있다(아래 `go`) — 배열은
  // 절대 비지 않으므로 non-null 단언이 안전하다
  const step = STEPS[index]!;
  const last = index === STEPS.length - 1;
  const first = index === 0;

  const go = useMemo(
    () => ({
      next: () => setIndex((i) => Math.min(STEPS.length - 1, i + 1)),
      prev: () => setIndex((i) => Math.max(0, i - 1)),
    }),
    [],
  );

  // 화살표로도 넘길 수 있게 — 마우스 없이 쭉 훑어보는 사람을 위해서다
  useEffect(() => {
    function onKey(event: KeyboardEvent) {
      if (event.key === "ArrowRight" || event.key === " ") go.next();
      else if (event.key === "ArrowLeft") go.prev();
      else if (event.key === "Escape") router.push("/lobby");
    }
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [go, router]);

  return (
    <main className="mx-auto flex w-full max-w-[900px] flex-1 flex-col px-6 py-6">
      <p className="label">튜토리얼 — 조작 + 하루의 흐름</p>

      <div className="mt-4 flex flex-col border-2 border-rule bg-paper">
        <header className="flex items-center justify-between border-b-2 border-ink bg-paper-2 px-8 py-5">
          <div>
            <span className="label" style={{ color: "var(--accent)" }}>
              {index + 1} / {STEPS.length} ·{" "}
              {step.kind === "flow" ? "게임 흐름" : step.kind === "info" ? "안내" : "조작"}
            </span>
            <h1 className="mt-1 text-2xl font-extrabold">{step.title}</h1>
          </div>
          {step.keys && step.keys.length > 0 && (
            <div className="flex flex-wrap items-center justify-end gap-2">
              {step.keys.map((key, i) => (
                <KeyOrJoin key={`${key}-${i}`} value={key} />
              ))}
            </div>
          )}
        </header>

        <div className="flex min-h-[220px] flex-col justify-center gap-3 px-8 py-10">
          {step.body.map((line, i) => (
            <p key={i} className="text-[1.0625rem] leading-relaxed">
              {line}
            </p>
          ))}
        </div>

        <div className="flex items-center gap-1.5 justify-center border-t border-rule-2 bg-paper-3 py-4">
          {STEPS.map((_, i) => (
            <button
              key={i}
              type="button"
              aria-label={`${i + 1}번째로 이동`}
              onClick={() => setIndex(i)}
              className="h-2 rounded-full transition-[width]"
              style={{
                width: i === index ? 22 : 8,
                background: i === index ? "var(--accent)" : "var(--rule)",
              }}
            />
          ))}
        </div>
      </div>

      <footer className="mt-6 flex items-center justify-between gap-4">
        <Link
          href="/lobby"
          className="flex h-[46px] w-[150px] items-center justify-center border text-base"
          style={{ borderColor: "var(--ink-2)", color: "var(--ink-2)" }}
        >
          로비로
        </Link>

        <div className="flex gap-3">
          <button
            type="button"
            onClick={go.prev}
            disabled={first}
            className="flex h-[46px] w-[110px] items-center justify-center border text-base disabled:opacity-30"
            style={{ borderColor: "var(--rule)", color: "var(--ink)" }}
          >
            이전
          </button>
          {last ? (
            <Link
              href="/lobby"
              className="flex h-[46px] w-[150px] items-center justify-center text-base font-extrabold"
              style={{ background: "var(--accent)", color: "var(--paper)" }}
            >
              마치기
            </Link>
          ) : (
            <button
              type="button"
              onClick={go.next}
              className="flex h-[46px] w-[150px] items-center justify-center text-base font-extrabold"
              style={{ background: "var(--accent)", color: "var(--paper)" }}
            >
              다음
            </button>
          )}
        </div>
      </footer>
    </main>
  );
}

/** 키캡 하나. `~`는 키가 아니라 "1부터 8까지"를 뜻하는 구분자라 옅게 그린다 */
function KeyOrJoin({ value }: { value: string }) {
  if (value === "~") {
    return (
      <span className="px-0.5 text-sm text-ink-2" aria-hidden>
        ~
      </span>
    );
  }
  return (
    <span
      className="inline-flex h-8 min-w-[32px] items-center justify-center border px-2 font-mono text-xs font-bold"
      style={{
        borderColor: "var(--accent)",
        color: "var(--accent)",
        background: "var(--sunk)",
      }}
    >
      {value}
    </span>
  );
}
