"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { useEffect, useMemo, useState } from "react";

/**
 * 튜토리얼 (H2 발주 → 친절하게 다시 씀).
 *
 * **게임 안에서 따라다니며 뜨던 8단계 카드를 걷어내고, 여기로 옮겼다.**
 * 사용자 지시 원문 — "온보딩 그냥 저따구로 하지 말고, 그냥 로비에서 튜토리얼
 * 누르면 하나하나 알려주는 걸로 바꿔." 즉 게임에 들어가기 **전에**, 원하는
 * 만큼 넘겨 보고 원하는 시점에 그만둘 수 있는 화면이다.
 *
 * ── 다시 쓴 이유 ────────────────────────────────────────────────────────
 * 첫 판은 키 목록이었다. 게임 안 키 안내 창(`Tutorial.cs`의 `KeyGuideEntries`)을
 * 웹으로 옮겨 한 장에 하나씩 넘기게 한 것에 가까웠고, 그래서 다 읽고 나도
 * **이 게임이 무엇인지는 모르는 채** 시작하게 됐다 — 보직도, 분대장도,
 * 승급도, 왜 혼자 잘하면 안 되는지도 한 줄도 없었다.
 *
 * 그래서 셋을 바꿨다.
 *
 *   1. **말투** 매뉴얼체("~한다")를 걷고 읽는 사람에게 말한다("~합니다").
 *      게임 안 문구는 건조한 군대 말투가 맞지만, 튜토리얼은 이 게임에서
 *      유일하게 플레이어를 **맞이하는** 화면이다
 *   2. **왜** 키가 무엇인지가 아니라 그 키가 언제 필요한지를 적는다.
 *      "SHIFT — 뛴다"로는 왜 뛰어야 하는지 알 수 없다
 *   3. **빠져 있던 것** 보직 4종 · 분대장과 구제권 · 승급 심사 · 하루 시간표 ·
 *      난이도별 실패 처리. 전부 이 게임을 이해하는 데 키보다 중요한 것들이다
 *
 * ── 여기 적힌 수치의 출처 ───────────────────────────────────────────────
 * 지어내지 않았다. 화면과 코드가 다른 소리를 하면 튜토리얼이 오히려 방해가 된다.
 *
 *   보직 4종·실패 파급     `apps/web/src/lib/api.ts` ROLE_LABELS
 *   하루 6개 시간대·시각    `packages/sim/data/phases.json`
 *   승급 심사 D-03/09/15   `packages/sim/data/ranks.json`
 *   구제 총량 3 = 분대장 2 + 간부 1
 *                          `packages/sim/src/types.ts` · `relief.ts` LEADER_RELIEF_LIMIT
 *   완화 난이도 경고 단계    `packages/sim/src/judge.ts` applyJudgement
 *   조작 키 전체            `unity/Assets/Scripts/Net/Tutorial.cs` KeyGuideEntries
 *
 * 진행 상태를 저장하지 않는다. 로비에서 언제나 다시 열 수 있으므로 "몇 단계
 * 까지 봤는지"를 기억할 이유가 없다.
 */
type Chapter = "intro" | "controls" | "day" | "deeper";

type Step = {
  chapter: Chapter;
  title: string;
  keys?: string[];
  body: string[];
  /** 본문 아래 따로 떼어 강조하는 한 줄. 몰라도 되지만 알면 편한 것 */
  tip?: string;
};

const CHAPTERS: Record<Chapter, { label: string; tint: string }> = {
  intro: { label: "어떤 게임인가요", tint: "var(--accent)" },
  controls: { label: "조작", tint: "var(--cold)" },
  day: { label: "하루의 흐름", tint: "var(--heat)" },
  deeper: { label: "알아두면 좋은 것", tint: "var(--role-admin)" },
};

const STEPS: Step[] = [
  /* ── 어떤 게임인가요 ─────────────────────────────────────────────── */
  {
    chapter: "intro",
    title: "환영합니다",
    body: [
      "SOLDIER : A DAY는 네 명이 함께 18일을 버티는 협동 게임입니다.",
      "하루하루 주어진 일과를 해내고 밤 점호를 통과하면 다음 날로 넘어가요. 그렇게 18일을 채우면 전역입니다.",
      "이 화면에서 조작과 하루의 흐름을 먼저 익히고 가시면 됩니다. 다 보는 데 3분이면 충분해요.",
    ],
    tip: "급하면 언제든 [로비로]를 눌러 나가도 됩니다. 이 튜토리얼은 로비에서 몇 번이든 다시 열 수 있어요.",
  },
  {
    chapter: "intro",
    title: "혼자 잘해서는 안 됩니다",
    body: [
      "네 사람이 소총수 · 통신병 · 의무병 · 행정병을 하나씩 맡습니다.",
      "보직은 잘하면 좋은 특기가 아니라, 그 사람이 못 하면 나머지 셋이 같이 손해를 보는 자리예요. 통신병이 무전을 놓치면 모두의 미니맵에서 마커가 사라지고, 의무병이 밀리면 전원의 수분 소모가 두 배가 됩니다.",
      "그래서 이 게임의 진짜 난이도는 조작이 아니라 서로 챙기기입니다.",
    ],
    tip: "보직은 로비에서 방을 만들거나 들어갈 때 고릅니다. 비어 있는 보직이 있으면 그 자리의 일은 아무도 대신 못 합니다.",
  },

  /* ── 조작 ────────────────────────────────────────────────────────── */
  {
    chapter: "controls",
    title: "걷기",
    keys: ["W", "A", "S", "D"],
    body: [
      "상하좌우로 걷습니다. 방향키도 똑같이 동작해요.",
      "부대는 걸어서 다니는 넓이입니다. 목적지가 어디인지 모르겠으면 TAB(일과표)과 M(지도)을 함께 보세요.",
    ],
  },
  {
    chapter: "controls",
    title: "뛰기",
    keys: ["SHIFT"],
    body: [
      "이동 중에 누르고 있으면 뜁니다.",
      "시간대마다 제한 시간이 있어서, 이동에 쓰는 시간이 곧 일과에 못 쓰는 시간입니다. 습관적으로 누르고 다니는 편이 편해요.",
    ],
  },
  {
    chapter: "controls",
    title: "일과 시작하기",
    keys: ["E"],
    body: [
      "목표 앞에 서면 안내가 뜹니다. 그때 E를 누르면 일과가 시작돼요.",
      "대부분은 짧은 미니게임으로 이어집니다. 쓰러진 동료를 일으키는 구조처럼 시간이 걸리는 동작은 E를 꾹 누르고 있으면 됩니다.",
    ],
    tip: "E를 눌렀는데 아무 일도 없다면 대개 목표에 조금 덜 다가간 것입니다. 한두 걸음 더 붙어 보세요.",
  },
  {
    chapter: "controls",
    title: "수첩 — 오늘 할 일 보기",
    keys: ["TAB"],
    body: [
      "TAB을 누를 때마다 일과표 → 일과요약 → 기록 → 닫힘 순서로 넘어갑니다.",
      "일과표는 오늘 무엇을 해야 하는지, 일과요약은 필수와 선택을 얼마나 채웠는지, 기록은 오늘 무슨 일이 있었는지를 보여줍니다.",
      "길을 잃었다 싶을 때 가장 먼저 눌러 볼 키예요.",
    ],
  },
  {
    chapter: "controls",
    title: "지도",
    keys: ["M"],
    body: [
      "오른쪽 위 미니맵을 전체화면 지도로 엽니다.",
      "부대 밖으로 나가면 훈련장 쪽 지도로 바뀌고, 훈련장 입구와 거기까지 이어진 길이 표시됩니다.",
    ],
  },
  {
    chapter: "controls",
    title: "퀵 커맨드 — 타이핑 없이 지시하기",
    keys: ["Q", "1", "~", "8"],
    body: [
      "Q를 누른 채로 숫자 1~8을 누르면 분대에 전술 지시가 나갑니다.",
      "협동 일과 대부분은 이것만으로 해결됩니다. 채팅을 칠 시간이 없을 때를 위한 장치예요.",
    ],
  },
  {
    chapter: "controls",
    title: "정형 문구 — 짧게 말하기",
    keys: ["C", "1", "~", "8"],
    body: [
      "C를 누른 채로 숫자 1~8을 누르면 미리 정해진 짧은 말을 합니다.",
      "\"확인\", \"지원 요청\" 같은 것들이라 손을 키보드에서 떼지 않고도 대화가 됩니다.",
    ],
  },
  {
    chapter: "controls",
    title: "내 상태 보기",
    keys: ["ALT"],
    body: [
      "누르고 있는 동안 체력 · 정신력 같은 컨디션 수치가 화면에 나타납니다.",
      "컨디션이 떨어지면 일과 성공률이 같이 떨어져요. 잘 안 풀린다 싶을 때 한 번 확인해 보세요.",
    ],
  },
  {
    chapter: "controls",
    title: "방독면",
    keys: ["G"],
    body: ["화생방 훈련이 있는 날, G로 방독면 착용 절차를 시작합니다."],
  },
  {
    chapter: "controls",
    title: "확인 · 닫기",
    keys: ["SPACE", "ENTER", "ESC"],
    body: [
      "미니게임과 확인 창에서는 대체로 SPACE나 ENTER로 진행합니다(판마다 조작이 조금씩 다를 수 있어요).",
      "ESC는 열려 있는 창을 닫습니다.",
    ],
  },
  {
    chapter: "controls",
    title: "다 외우지 않아도 됩니다",
    keys: ["1"],
    body: [
      "게임 중 아무 때나 1번 키를 누르면 지금 본 키 전체가 한 장에 다시 뜹니다.",
      "Q나 C를 누르고 있는 동안, 그리고 미니게임 판이 열려 있는 동안에는 그 숫자를 판이 쓰고 있으므로 안내 창이 끼어들지 않아요.",
    ],
    tip: "지금 이 목록을 외우려 하지 마세요. 필요할 때 1을 누르는 것만 기억하면 충분합니다.",
  },

  /* ── 하루의 흐름 ─────────────────────────────────────────────────── */
  {
    chapter: "day",
    title: "하루는 여섯 시간대",
    body: [
      "06:00 기상 · 점검 → 08:00 오전 일과 → 12:00 중식 · 휴식 → 14:00 오후 일과 → 18:00 석식 · 개인정비 → 22:00 점호 · 판정.",
      "시간이 다 되면 자동으로 다음 시간대로 넘어갑니다. 못 끝낸 일과는 그대로 남아요.",
      "다 같이 준비됐다면 화면의 [투표하기]를 눌러 남은 시간을 건너뛸 수도 있습니다. 이건 키가 아니라 마우스로 누릅니다.",
    ],
  },
  {
    chapter: "day",
    title: "필수와 선택",
    body: [
      "시간대마다 할 일이 뜹니다. TAB으로 열면 필수와 선택이 나뉘어 보여요.",
      "필수는 그날 밤 점호에서 세는 것이고, 선택은 안 해도 그날은 무사한 것입니다.",
      "그렇다고 선택이 덤은 아닙니다 — 승급에 쓰이는 복무 점수는 오직 선택 일과에서만 쌓입니다.",
    ],
    tip: "필수는 오늘을 살아남는 일, 선택은 18일 뒤를 준비하는 일이라고 생각하면 됩니다.",
  },
  {
    chapter: "day",
    title: "밤 점호",
    body: [
      "하루가 끝나면 점호에서 그날 성과를 판정합니다.",
      "정규 난이도는 필수 미달 한 번으로 런이 끝납니다.",
      "완화 난이도는 세 번까지 버팁니다. 1차는 경고(다음 날 필수 +2), 2차는 근신(다음 날 개인정비 시간 박탈), 3차에 끝나요. 대신 보상이 60%로 줄어듭니다.",
    ],
    tip: "처음이라면 완화를 권합니다. 한 번 실수했다고 18일이 그대로 날아가지는 않아요.",
  },
  {
    chapter: "day",
    title: "다음 날로",
    body: [
      "판정 요약을 모두가 확인해야 다음 날이 시작됩니다. 누군가 아직 확인 중이면 시간은 멈춰 있으니 서두르지 않아도 돼요.",
      "이렇게 하루하루를 쌓아 18일을 채우면 전역입니다.",
    ],
  },

  /* ── 알아두면 좋은 것 ────────────────────────────────────────────── */
  {
    chapter: "deeper",
    title: "분대장과 구제권",
    body: [
      "런이 시작되면 넷 중 한 명이 분대장이 됩니다. 머리 위 이름표 옆 다이아몬드 표시가 분대장이에요. 분대원 과반이 동의하면 도중에 바꿀 수도 있습니다.",
      "분대장에게는 구제권이 있습니다. 오늘 안에 도저히 못 끝낼 것 같은 필수 일과 하나를 선택으로 내려, 그날 점호에서 세지 않게 만드는 권한이에요.",
      "런 전체에서 분대장 몫 2회 + 간부 몫 1회, 모두 3회뿐입니다. 하루가 지나도 다시 차지 않아요.",
    ],
    tip: "쓸 수 있는 대상은 \"지금 필수인데 아직 못 끝낸\" 일과뿐입니다. 이미 끝난 일과나 선택 일과에는 쓸 수 없어요.",
  },
  {
    chapter: "deeper",
    title: "계급과 승급 심사",
    body: [
      "3일차 · 9일차 · 15일차에 승급 심사가 있습니다. 통과하면 이병 → 일병 → 상병 → 병장으로 올라가요.",
      "기준은 복무 점수이고, 복무 점수는 앞서 말한 것처럼 선택 일과에서만 쌓입니다. 필수만 겨우 채우며 버티면 계급은 그대로예요.",
      "심사에서 보류돼도 다음 정비일에 다시 받을 수 있고, 그때는 요구치가 조금 낮아집니다.",
    ],
  },
  {
    chapter: "deeper",
    title: "잘 안 풀릴 때",
    body: [
      "미니맵 마커가 사라졌다면 무전이 끊긴 것입니다. 통신병이 무전 유지 일과를 해내면 돌아옵니다.",
      "일과가 자꾸 실패한다면 ALT로 컨디션을 확인해 보세요. 지쳐 있으면 성공률이 떨어집니다.",
      "어디로 가야 할지 모르겠으면 TAB → 일과표, 그리고 M으로 지도를 여세요.",
    ],
  },
  {
    chapter: "deeper",
    title: "이제 시작해 볼까요",
    body: [
      "첫 판은 헤매는 게 정상입니다. 18일을 처음부터 완주하는 분대는 거의 없어요.",
      "잊어버린 키는 게임 중 1번, 오늘 할 일은 TAB, 길은 M. 이 셋만 기억하면 나머지는 하면서 익혀집니다.",
      "로비로 돌아가 분대를 편성하시면 됩니다. 잘 다녀오세요.",
    ],
  },
];

export default function TutorialPage() {
  const router = useRouter();
  const [index, setIndex] = useState(0);
  // `index`는 항상 0..STEPS.length-1로 클램프되어 있다(아래 `go`) — 배열은
  // 절대 비지 않으므로 non-null 단언이 안전하다
  const step = STEPS[index]!;
  const chapter = CHAPTERS[step.chapter];
  const last = index === STEPS.length - 1;
  const first = index === 0;

  const go = useMemo(
    () => ({
      next: () => setIndex((i) => Math.min(STEPS.length - 1, i + 1)),
      prev: () => setIndex((i) => Math.max(0, i - 1)),
    }),
    [],
  );

  // 장(章)마다 몇 장짜리인지 — 진행 점을 장 단위로 끊어 그린다. 18개가
  // 한 줄로 늘어서면 "아직도 이만큼 남았나" 말고는 읽히는 것이 없다
  const groups = useMemo(() => {
    const out: { chapter: Chapter; from: number; count: number }[] = [];
    STEPS.forEach((s, i) => {
      const tail = out[out.length - 1];
      if (tail && tail.chapter === s.chapter) tail.count += 1;
      else out.push({ chapter: s.chapter, from: i, count: 1 });
    });
    return out;
  }, []);

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
      <p className="label">튜토리얼 — 처음이라면 여기부터</p>

      <div className="mt-4 flex flex-col border-2 border-rule bg-paper">
        <header className="flex items-start justify-between gap-6 border-b-2 border-ink bg-paper-2 px-8 py-5">
          <div>
            <span className="label" style={{ color: chapter.tint }}>
              {chapter.label} · {index + 1} / {STEPS.length}
            </span>
            <h1 className="mt-1 text-2xl font-extrabold">{step.title}</h1>
          </div>
          {step.keys && step.keys.length > 0 && (
            <div className="flex flex-wrap items-center justify-end gap-2 pt-1">
              {step.keys.map((key, i) => (
                <KeyOrJoin key={`${key}-${i}`} value={key} />
              ))}
            </div>
          )}
        </header>

        <div className="flex min-h-[240px] flex-col justify-center gap-3 px-8 py-9">
          {step.body.map((line, i) => (
            <p key={i} className="text-[1.0625rem] leading-relaxed">
              {line}
            </p>
          ))}

          {step.tip && (
            // 본문과 같은 크기로 이어 적으면 "몰라도 되는 것"과 "알아야
            // 하는 것"이 구별되지 않는다. 왼쪽 선 하나로 갈라 놓는다
            <p
              className="mt-2 border-l-2 py-1 pl-4 text-[0.9375rem] leading-relaxed text-ink-2"
              style={{ borderColor: chapter.tint }}
            >
              {step.tip}
            </p>
          )}
        </div>

        <div className="flex items-center justify-center gap-4 border-t border-rule-2 bg-paper-3 py-4">
          {groups.map((group) => (
            <div key={group.from} className="flex items-center gap-1.5">
              {Array.from({ length: group.count }, (_, i) => {
                const at = group.from + i;
                const here = at === index;
                return (
                  <button
                    key={at}
                    type="button"
                    aria-label={`${CHAPTERS[group.chapter].label} ${i + 1}번째로 이동`}
                    aria-current={here ? "step" : undefined}
                    onClick={() => setIndex(at)}
                    className="h-2 rounded-full transition-[width]"
                    style={{
                      width: here ? 22 : 8,
                      background: here
                        ? CHAPTERS[group.chapter].tint
                        : at < index
                          ? "var(--rule)"
                          : "var(--rule-2)",
                    }}
                  />
                );
              })}
            </div>
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
              시작하기
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

      {/* 키로도 넘길 수 있다는 것을 알려준다. 마우스를 쥐고 있지 않은
          사람에게는 이 한 줄이 버튼 두 개보다 빠르다 */}
      <p className="mt-4 text-center text-sm text-ink-2">
        ← → 로 넘기고, ESC로 로비에 돌아갑니다
      </p>
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
