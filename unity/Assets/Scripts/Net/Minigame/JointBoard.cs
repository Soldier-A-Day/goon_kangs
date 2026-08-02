using UnityEngine;

namespace SoldierADay.Net
{
    /// <summary>
    /// QST-01 합동 판 — **판 하나를 인원이 나눠 채운다.**
    ///
    /// 각자 같은 게임을 따로 통과하는 것이 아니다. 목표가 조각으로 쪼개져 있고,
    /// 누가 채우든 같은 칸에 쌓인다 — 남이 채운 조각이 내 화면에서도 채워지고,
    /// 인원이 모자라면 아무리 눌러도 하나도 오르지 않는다.
    ///
    /// ── 겉을 씌우는 이유 ─────────────────────────────────────────────────
    /// 합동 전용 원형을 새로 만들지 않는다. 창고 재물조사는 `AUDIT`이고 하역
    /// 릴레이는 `MASH`다 — 다른 것은 **누가 채우는가**뿐이라, 안쪽 판은 그대로
    /// 두고 조각을 세는 겉을 한 겹 씌운다(`InterruptBoard`와 같은 방식).
    ///
    /// ── 안쪽 판은 계속 다시 열린다 ───────────────────────────────────────
    /// 제 몫을 다 돌면 안쪽 판이 통과하는데, 거기서 끝내면 남은 조각을 채울
    /// 방법이 없다. 그래서 곧바로 새 판을 차린다 — 릴레이에서 다음 상자가 오는 것과 같다.
    ///
    /// ── 통과는 서버가 정한다 ─────────────────────────────────────────────
    /// 조각이 다 차면 서버가 완료로 넘기고 스냅샷의 상태가 바뀐다. 이 판은
    /// 스스로 통과를 선언하지 않는다 — 분대가 채운 것을 개인이 선언할 수 없다.
    /// </summary>
    public sealed class JointBoard : Board
    {
        private Board _inner;
        private int _round;

        /// <summary>서버가 센 조각. `QuestPlay`가 스냅샷에서 넣어준다</summary>
        public int Done { get; set; }
        public int Total { get; set; } = 1;
        /// <summary>요구 인원과 지금 그 자리에 있는 인원 — 미달이면 경고를 띄운다</summary>
        public int NeedActors { get; set; } = 2;
        public int HereActors { get; set; }

        /// <summary>조각 하나를 채웠다. `QuestPlay`가 서버로 올린다</summary>
        public System.Action OnStep;

        /// <summary>한 사람이 한 판에서 낼 조각 수 — 요구 인원이 나눠 가진다</summary>
        private int PerRound => Mathf.Max(1, Mathf.RoundToInt((float)Total / Mathf.Max(1, NeedActors)));

        private int _sentThisRound;
        private bool Short => HereActors < NeedActors;

        public override string Instruction =>
            Short ? $"{NeedActors}인이 모여야 진행된다 — 지금 {HereActors}명"
                  : _inner?.Instruction ?? "";

        public override string Status =>
            $"분대 {Done}/{Total} 조각" +
            (string.IsNullOrEmpty(_inner?.Status) ? "" : $"  ·  내 판 {_inner.Status}");

        protected override void Setup()
        {
            _round = 0;
            OpenRound();
        }

        private void OpenRound()
        {
            _sentThisRound = 0;
            _inner = Boards.Create(Spec?.type);
            // 판마다 다른 배치가 나와야 두 번째가 첫 번째의 반복이 아니다
            _inner.Begin(Spec, Limit, $"{Spec?.variant}#{_round}");
            _round += 1;
        }

        // 제한 시간은 시간대가 정한다 (QST-01). 판이 스스로 실패하지 않는다
        protected override bool TimesOut => false;

        protected override void Advance(float dt, BoardInput input)
        {
            if (_inner == null) return;

            // **미달이면 손이 안 먹는다.** 게이지가 안 차는 것을 보여주는 것보다
            // 아예 못 만지게 하는 편이 "인원을 모아라"를 더 빨리 읽힌다
            var state = Short ? BoardState.Running : _inner.Tick(dt, input);

            Fill = Total <= 0 ? 0f : Mathf.Clamp01((float)Done / Total);

            if (!Short)
            {
                // 안쪽 판이 얼마나 갔는지를 조각으로 환산해 올린다
                var earned = Mathf.FloorToInt(_inner.Fill * PerRound);
                while (_sentThisRound < earned)
                {
                    _sentThisRound += 1;
                    OnStep?.Invoke();
                }
            }

            // 제 몫을 다 돌았으면 다음 판을 차린다 — 남은 조각은 아직 남아 있다
            if (state != BoardState.Running) OpenRound();
        }

        public override void Draw(HudTheme theme, Rect body)
        {
            // 조각 띠 — **이 판의 주인공이다.** 남이 채운 것이 여기서 보인다
            var strip = new Rect(body.x, body.y, body.width, 34f);
            var cell = (strip.width - (Total - 1) * 4f) / Mathf.Max(1, Total);
            for (var i = 0; i < Total; i += 1)
            {
                var box = new Rect(strip.x + i * (cell + 4f), strip.y, cell, strip.height);
                theme.Fill(box, i < Done ? HudTheme.Accent : HudTheme.Paper);
                theme.Border(box, i < Done ? HudTheme.Accent : HudTheme.Rule2);
            }

            var inner = new Rect(body.x, strip.yMax + 12f, body.width, body.height - strip.height - 12f);
            _inner?.Draw(theme, inner);

            if (!Short) return;

            // 인원 미달 — 게이지가 안 차는 이유를 화면이 말한다 (§7.1.5)
            var warn = new Rect(inner.x, inner.center.y - 46f, inner.width, 92f);
            theme.Fill(warn, HudTheme.AlertW, 0.95f);
            theme.Border(warn, HudTheme.Alert, 2f);
            GUI.Label(new Rect(warn.x, warn.y + 14f, warn.width, 32f),
                $"인원 미달 — {HereActors}/{NeedActors}",
                theme.At(theme.Heading, 22, HudTheme.Alert, TextAnchor.MiddleCenter));
            GUI.Label(new Rect(warn.x, warn.y + 50f, warn.width, 28f),
                "혼자서는 한 조각도 오르지 않는다",
                theme.At(theme.Body, 17, HudTheme.Ink, TextAnchor.MiddleCenter));
        }
    }
}
