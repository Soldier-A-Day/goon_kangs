using SoldierADay.Protocol;
using UnityEngine;

namespace SoldierADay.Net
{
    /// <summary>판이 지금 어떤 상태인가</summary>
    public enum BoardState
    {
        /// <summary>진행 중</summary>
        Running,
        /// <summary>목표를 채웠다 — 이제 서버에 통과를 신고한다</summary>
        Cleared,
        /// <summary>못 채웠다 — 판이 다시 열린다. 잃은 시간이 페널티다</summary>
        Failed,
    }

    /// <summary>
    /// 한 프레임의 입력.
    ///
    /// **원형 하나에 조작은 한 종류다.** 12초짜리에 조작 두 개를 얹으면 설명만
    /// 하다 끝난다(설계안 §전제 3). 그래서 여기 담긴 것을 원형이 전부 쓰는 것이
    /// 아니라, 저마다 **하나만** 골라 쓴다 — 드래그거나, 연타거나, 단발이거나.
    /// </summary>
    public struct BoardInput
    {
        /// <summary>1920×1080 기준 좌표. HUD와 같은 공간이라 판 사각형과 바로 비교된다</summary>
        public Vector2 Mouse;
        public bool Down;
        public bool Pressed;
        public bool Released;
        /// <summary>단발·연타 키. 마우스 클릭과 같이 취급한다 — 손을 옮기지 않아도 된다</summary>
        public bool Tap;
        public bool Hold;
        /// <summary>A · D 또는 ←→</summary>
        public float Horizontal;

        public static BoardInput Read()
        {
            // HUD가 매 프레임 재는 값을 그대로 쓴다 — 여기서 따로 계산하면
            // 화면 크기가 바뀌는 순간 판의 클릭 자리와 그림이 어긋난다
            var scale = HudTheme.ViewScale;
            var mouse = new Vector2(Input.mousePosition.x / scale,
                                    (Screen.height - Input.mousePosition.y) / scale);

            var pressed = Input.GetMouseButtonDown(0);
            var down = Input.GetMouseButton(0);

            return new BoardInput
            {
                Mouse = mouse,
                Down = down,
                Pressed = pressed,
                Released = Input.GetMouseButtonUp(0),
                Tap = pressed || Input.GetKeyDown(KeyCode.Space),
                Hold = down || Input.GetKey(KeyCode.Space),
                Horizontal = Input.GetAxisRaw("Horizontal"),
            };
        }
    }

    /// <summary>
    /// 일과 미니게임 원형 하나.
    ///
    /// ── 판이 정하는 것과 정하지 않는 것 ──────────────────────────────────
    /// 판은 **통과 여부와 등급**만 정한다. 완료 선언은 서버가 하고(`clearQuest`),
    /// 구역·시간대·소유자·최소 진척은 전부 서버가 막는다 — 판을 통과해도 엉뚱한
    /// 자리에 서 있으면 아무 일도 일어나지 않는다(ARCH-02).
    ///
    /// ── 통과와 등급은 다른 축이다 ────────────────────────────────────────
    /// 목표를 못 채우면 완료가 아니라 **재시도**다. 그래서 등급에 "실패"가 없고,
    /// 통과한 판을 얼마나 깨끗하게 했는가만 가른다 — **남은 시간과 실수 횟수**다.
    ///
    /// ── 파라미터는 서버가 준다 ───────────────────────────────────────────
    /// 예전에는 클라가 일과 **이름을 보고** 유형을 골랐다. 새 일과가 생겨도
    /// 서버가 안 바뀐다는 것이 이유였는데, 그 대가로 "총기 수입"과 "공구 정리"가
    /// 같은 판이 됐다. 지금은 `quests.json`이 69건의 원형과 파라미터를 소유하고
    /// 스냅샷으로 내려온다 — 같은 사실은 한 곳에만 있다.
    /// </summary>
    public abstract class Board
    {
        /// <summary>서버가 준 판 정의</summary>
        public SnapshotQuestsItemMinigame Spec { get; private set; }

        /// <summary>제한 시간(초) = 일과의 소요. 판 안에 시간을 따로 두지 않는다</summary>
        public float Limit { get; private set; }
        public float Elapsed { get; private set; }
        public float Remaining => Mathf.Max(0f, Limit - Elapsed);
        /// <summary>0~1. 남은 시간 막대</summary>
        public float TimeLeftRatio => Limit <= 0f ? 0f : Remaining / Limit;

        /// <summary>0~1. 목표를 얼마나 채웠는가 — 판마다 세는 것이 다르다</summary>
        public float Fill { get; protected set; }

        /// <summary>실수 횟수. 등급을 가르는 두 축 중 하나다</summary>
        public int Mistakes { get; protected set; }

        /// <summary>조작 한 줄. 다가서면 바로 읽혀야 한다 (§7.1.5)</summary>
        public abstract string Instruction { get; }

        /// <summary>지금 무슨 일이 벌어지고 있는가 — 남은 개수 · 적발 수 같은 것</summary>
        public virtual string Status => "";

        /// <summary>같은 일과는 늘 같은 판이 나와야 플레이어가 익힌다</summary>
        protected System.Random Rng { get; private set; }

        public BoardState State { get; private set; }

        /// <summary>
        /// 시간이 다 흘렀을 때 실패가 아니라 그때까지의 Fill로 등급을 매겨
        /// 통과하는 판인가.
        ///
        /// 14종 원형 전부 기본은 false(시간초과=실패)다. 판단형(대조 점검·
        /// 분류)은 시간이 압박이어야 맞지만, 과정형(문지르기·탐색·자유 배치)은
        /// 파워워시 시뮬레이터의 젠이 돼야 하는데 같은 잣대를 쓰면 못 끝낸
        /// 죄로 처음부터 다시 시킨다 — 그 판만 여기를 override한다.
        /// </summary>
        protected virtual bool GradesOnTimeout => false;

        /// <summary>
        /// 시간초과로 끝났다 — 등급 상한과 결과 문구가 갈린다.
        ///
        /// `GradesOnTimeout` 판이 시간 안에 끝내지 못하고 Fill로 통과했을 때,
        /// 또는 Fill이 절반도 안 돼 재시도로 넘어갔을 때 켜진다. 시간 안에
        /// 끝낸 통과·실수로 인한 즉시 실패와는 결과 화면 문구가 달라야 한다.
        /// </summary>
        public bool TimedOut { get; private set; }

        /// <summary>
        /// 0~1. 성공/실수 플래시 세기 — 지수 감쇠. `HudMinigame`이 본문 위에 겹쳐 그린다.
        ///
        /// 예전에는 `RhythmBoard` 등 3종이 저마다 `_flash` 필드를 따로 들고
        /// 있었다. 여기로 올리면 `Clear`/`Miss`/`Fail`을 부르는 14종 전체가
        /// 개별 배선 없이 같은 피드백을 받는다 — 그게 이 필드가 여기 있는 이유다.
        /// </summary>
        public float FlashT { get; private set; }

        /// <summary>지금 플래시가 성공(Accent)인지 실수(Alert)인지</summary>
        public bool FlashSuccess { get; private set; }

        /// <summary>결과 화면(통과·재시도)이 뜬 뒤 지난 시간(초) — 스케일 팝·흔들림이 쓴다</summary>
        public float ResultAge { get; private set; }

        private float _flashDuration = 0.25f;
        private BoardState _presentedState = BoardState.Running;

        public void Begin(SnapshotQuestsItemMinigame spec, float limitSeconds, string questId)
        {
            Spec = spec;
            Limit = Mathf.Max(1f, limitSeconds);
            Elapsed = 0f;
            Fill = 0f;
            Mistakes = 0;
            State = BoardState.Running;
            FlashT = 0f;
            ResultAge = 0f;
            TimedOut = false;
            _presentedState = BoardState.Running;
            Rng = new System.Random(StableHash(questId ?? ""));
            Setup();
        }

        /// <summary>판을 차린다. `Rng`와 `Spec`을 여기서 읽는다</summary>
        protected abstract void Setup();

        /// <summary>
        /// 한 프레임.
        ///
        /// 시간이 다 흐르면 실패다 — **끝내지 못한 것이지 진 것이 아니다.**
        /// 판이 다시 열리고, 계속 실패하면 시간대 종료로 잠긴다.
        /// </summary>
        public BoardState Tick(float dt, BoardInput input)
        {
            if (State != BoardState.Running) return State;

            Elapsed += dt;
            Advance(dt, input);

            if (State != BoardState.Running) return State;
            if (TimesOut && Elapsed >= Limit) HandleTimeout();
            return State;
        }

        protected abstract void Advance(float dt, BoardInput input);

        /// <summary>
        /// 시간이 다 흘렀을 때의 판정.
        ///
        /// 판단형은 여전히 실패다 — 압박이 그 원형의 본질이다. 과정형
        /// (`GradesOnTimeout`)은 그때까지 채운 양이 절반을 넘었으면 인정하고
        /// 통과시킨다. 다만 다 끝낸 것과 같은 세기로 축하하면 시간초과가
        /// 성공처럼 읽히므로 플래시는 짧게 절제한다. 절반도 못 채웠으면
        /// 가만히 있어도(AFK) 판이 넘어가는 구멍이 되므로 그대로 재시도로
        /// 돌려보낸다.
        /// </summary>
        private void HandleTimeout()
        {
            TimedOut = true;

            if (!GradesOnTimeout)
            {
                State = BoardState.Failed;
                return;
            }

            if (Fill >= 0.5f)
            {
                State = BoardState.Cleared;
                SetFlash(true, 0.12f);
                Sfx.Play("success");
            }
            else
            {
                State = BoardState.Failed;
            }
        }

        /// <summary>
        /// 시간이 다 흐르면 실패인가.
        ///
        /// 판이 없는 일과만 예외다 — 끝내는 것이 서버라서 제한 시간이 곧 완료
        /// 시점이고, 그때 실패로 넘기면 완료되는 순간 실패 화면이 뜬다.
        /// </summary>
        protected virtual bool TimesOut => true;

        /// <summary>목표를 채웠다</summary>
        protected void Clear()
        {
            State = BoardState.Cleared;
            SetFlash(true, 0.25f);
            Sfx.Play("success");
        }

        /// <summary>즉시 실패 — 극성을 반대로 꽂는 것 같은, 되돌릴 수 없는 실수</summary>
        protected void Fail()
        {
            State = BoardState.Failed;
            // 되돌릴 수 없는 실수라 플래시도 소리도 `Miss`보다 크게 간다
            SetFlash(false, 0.3f);
            Sfx.Play("warning");
        }

        protected void Miss()
        {
            Mistakes += 1;
            SetFlash(false, 0.25f);
            Sfx.Play("miss");
        }

        private void SetFlash(bool success, float duration)
        {
            FlashT = 1f;
            FlashSuccess = success;
            _flashDuration = duration;
        }

        /// <summary>
        /// 매 프레임 화면이 채운다. `Tick`과 분리한 이유는 판이 멈춘(Cleared·
        /// Failed) 뒤에도 결과창 애니메이션(스케일 팝·흔들림)과 플래시 감쇠는
        /// 계속 흘러야 하는데, `QuestPlay`는 그 상태에서 더 이상 `Tick`을
        /// 부르지 않기 때문이다(판정을 서버 응답까지 붙잡아 두는 동안에도
        /// 화면은 살아 있어야 한다).
        /// </summary>
        public void Present(float dt)
        {
            if (State != _presentedState)
            {
                _presentedState = State;
                ResultAge = 0f;
            }
            else if (State != BoardState.Running)
            {
                ResultAge += dt;
            }

            if (FlashT <= 0f) return;
            // 지수 감쇠 — `_flashDuration`초 만에 세기의 1%로 떨어진다
            FlashT *= Mathf.Pow(0.01f, dt / Mathf.Max(0.001f, _flashDuration));
            if (FlashT < 0.01f) FlashT = 0f;
        }

        /// <summary>
        /// 등급.
        ///
        /// 축 두 개뿐이다 — **여유와 실수**. 원형마다 다른 잣대를 만들면 플레이어가
        /// 14번 새로 배워야 하고, 무엇을 잘해야 하는지가 판마다 달라진다.
        ///
        ///   A  실수 0 · 제한 시간의 20% 이상 남김
        ///   B  실수 1 이하
        ///   C  통과는 했다
        ///
        /// **시간초과로 통과했으면 이 잣대를 안 쓴다.** 실수·여유는 시간 안에
        /// 끝낸 사람에게만 의미가 있는 축이라, 시간이 다 돼서 넘어간 판은
        /// 그때까지 채운 양(Fill)으로만 잰다. A는 시간 안에 끝낸 사람의
        /// 몫이라 시간초과로는 닿지 않는다.
        ///
        ///   B  Fill 85% 이상
        ///   C  그 외
        /// </summary>
        public string Grade()
        {
            if (TimedOut) return Fill >= 0.85f ? SnapshotQuestsItemGradeValues.B : SnapshotQuestsItemGradeValues.C;
            if (Mistakes == 0 && TimeLeftRatio >= 0.2f) return SnapshotQuestsItemGradeValues.A;
            if (Mistakes <= 1) return SnapshotQuestsItemGradeValues.B;
            return SnapshotQuestsItemGradeValues.C;
        }

        /// <summary>판 본문을 그린다. 테두리·제목·시간 막대는 `HudMinigame`이 맡는다</summary>
        public abstract void Draw(HudTheme theme, Rect body);

        /* ------------------------------------------------------------ 유틸 */

        protected float Param(string key, float fallback)
        {
            if (Spec == null) return fallback;
            switch (key)
            {
                case "stains": return Spec.stains > 0 ? (float)Spec.stains : fallback;
                case "brush": return Spec.brush > 0 ? (float)Spec.brush : fallback;
                case "coverage": return Spec.coverage > 0 ? (float)Spec.coverage : fallback;
                case "supply": return Spec.supply > 0 ? (float)Spec.supply : fallback;
                case "pieces": return Spec.pieces > 0 ? (float)Spec.pieces : fallback;
                case "snapDeg": return Spec.snapDeg > 0 ? (float)Spec.snapDeg : fallback;
                case "tolerance": return Spec.tolerance > 0 ? (float)Spec.tolerance : fallback;
                case "entries": return Spec.entries > 0 ? (float)Spec.entries : fallback;
                case "mismatches": return Spec.mismatches > 0 ? (float)Spec.mismatches : fallback;
                case "strikes": return Spec.strikes > 0 ? (float)Spec.strikes : fallback;
                case "taps": return Spec.taps > 0 ? (float)Spec.taps : fallback;
                case "drain": return Spec.drain > 0 ? (float)Spec.drain : fallback;
                case "stamina": return Spec.stamina > 0 ? (float)Spec.stamina : fallback;
                case "sway": return Spec.sway > 0 ? (float)Spec.sway : fallback;
                case "segments": return Spec.segments > 0 ? (float)Spec.segments : fallback;
                case "bandMin": return Spec.bandMin > 0 ? (float)Spec.bandMin : fallback;
                case "bandMax": return Spec.bandMax > 0 ? (float)Spec.bandMax : fallback;
                case "drift": return Spec.drift > 0 ? (float)Spec.drift : fallback;
                case "slack": return Spec.slack > 0 ? (float)Spec.slack : fallback;
                case "nodes": return Spec.nodes > 0 ? (float)Spec.nodes : fallback;
                case "branches": return Spec.branches > 0 ? (float)Spec.branches : fallback;
                case "rotations": return Spec.rotations > 0 ? (float)Spec.rotations : fallback;
                case "items": return Spec.items > 0 ? (float)Spec.items : fallback;
                case "bins": return Spec.bins > 0 ? (float)Spec.bins : fallback;
                case "ambiguous": return Spec.ambiguous > 0 ? (float)Spec.ambiguous : fallback;
                case "hits": return Spec.hits > 0 ? (float)Spec.hits : fallback;
                case "window": return Spec.window > 0 ? (float)Spec.window : fallback;
                case "speed": return Spec.speed > 0 ? (float)Spec.speed : fallback;
                case "length": return Spec.length > 0 ? (float)Spec.length : fallback;
                case "steps": return Spec.steps > 0 ? (float)Spec.steps : fallback;
                case "show": return Spec.show > 0 ? (float)Spec.show : fallback;
                case "beats": return Spec.beats > 0 ? (float)Spec.beats : fallback;
                case "bpm": return Spec.bpm > 0 ? (float)Spec.bpm : fallback;
                case "combo": return Spec.combo > 0 ? (float)Spec.combo : fallback;
                case "targets": return Spec.targets > 0 ? (float)Spec.targets : fallback;
                case "holdRatio": return Spec.holdRatio > 0 ? (float)Spec.holdRatio : fallback;
                case "hidden": return Spec.hidden > 0 ? (float)Spec.hidden : fallback;
                case "cells": return Spec.cells > 0 ? (float)Spec.cells : fallback;
                case "signal": return Spec.signal > 0 ? (float)Spec.signal : fallback;
                case "count": return Spec.count > 0 ? (float)Spec.count : fallback;
                default: return fallback;
            }
        }

        protected int ParamInt(string key, int fallback) =>
            Mathf.RoundToInt(Param(key, fallback));

        /// <summary>
        /// 난이도 보정 — 1~3.
        ///
        /// 파라미터를 난이도별로 세 벌 만들지 않는다. 같은 값에 이 계수만 곱하면
        /// 야간·혹한기에 런타임으로 +1 하는 여지가 그대로 남는다.
        /// </summary>
        protected float Difficulty => Spec == null ? 1f : Mathf.Clamp(Spec.difficulty, 1, 3);

        protected float RandRange(float min, float max) =>
            min + (float)Rng.NextDouble() * (max - min);

        /// <summary>
        /// 실행마다 값이 달라지지 않는 해시.
        ///
        /// `string.GetHashCode`는 .NET에서 실행마다 달라진다(해시 DoS 방어).
        /// 그러면 같은 일과가 매번 다른 판을 내고, 플레이어는 익힐 수 없다.
        /// </summary>
        public static int StableHash(string text)
        {
            unchecked
            {
                var hash = 17;
                foreach (var c in text) hash = hash * 31 + c;
                return hash & 0x7FFFFFFF;
            }
        }
    }
}
