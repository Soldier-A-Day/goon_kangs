using UnityEngine;

namespace SoldierADay.Net
{
    /// <summary>
    /// `RaceBoard` — 같은 판을 고스트와 나란히(A-5 2차, 설계안 §A-5 2차).
    ///
    /// **실제 상대 동기화는 서버 작업이라 범위 밖이다.** 이 래퍼가 하는 것은
    /// 로컬 비교 연출뿐이다 — 내 판은 그대로 돌고, 그 위에 "고스트 진행 바"
    /// 하나가 시드 고정 페이스로 함께 찬다. 고스트는 실제로 판을 도는 두
    /// 번째 `Board` 인스턴스가 아니다(그러려면 16종 전부에 봇 입력 AI가
    /// 있어야 한다) — 그냥 시간에 따라 차는 막대다. 워크오더가 요구한 것도
    /// "고스트 진행 **바**(봇 페이스)"이지 고스트 판이 아니다.
    ///
    /// ── 왜 겉을 씌우는가 ─────────────────────────────────────────────────
    /// `JointBoard`·`InterruptBoard`와 같은 이유다. 경쟁 겉을 원형마다 새로
    /// 넣지 않고, 안쪽 판은 그대로 두고 비교 막대와 보상 판정만 한 겹
    /// 씌운다.
    ///
    /// ── 보상 ─────────────────────────────────────────────────────────────
    /// 내가 고스트보다 먼저 판을 끝내면(`Clear`) 실수 1회를 무효로 돌린다
    /// (`Mistakes`를 직접 1 깎는다) — "먼저 끝낸 사람이 남을 돕는다"는
    /// 비시바시식 보상을, 서버 없이 로컬에서 흉내 낸다. 진 쪽에는 벌점이
    /// 없다 — 경쟁이 아니라 눈에 보이는 페이스일 뿐이다.
    ///
    /// ── 시간초과 관통(§InterruptBoard와 같은 수리) ──────────────────────────
    /// 안쪽 판의 `TimedOut`을 그대로 옮긴다 — 안 옮기면 `GradesOnTimeout`
    /// 판(예: `DODGE`)이 시간초과로 통과했을 때도 이 래퍼의 `Grade()`가
    /// 실수·잔여율 잣대를 써 버려 등급이 어긋난다.
    ///
    /// ── 소유 범위 ────────────────────────────────────────────────────────
    /// `BoardLab` 전용 진입이다. `quests.json`에는 묶지 않는다 — 실전 배치는
    /// 후속 발주의 몫이다(진짜 동기화가 있어야 실전에서 의미가 있다).
    /// </summary>
    public sealed class RaceBoard : Board
    {
        private Board _inner;

        /// <summary>고스트가 판을 "끝내는" 시각(초) — 실제 판이 아니라 막대가 이 시각에 다 찬다</summary>
        private float _ghostFinishAt;

        private bool _resolved;
        private bool _wonRace;

        public override string Instruction => _inner?.Instruction ?? "";

        public override string Status =>
            (_inner?.Status ?? "") + (_wonRace ? "  ·  먼저 끝냈다 — 실수 1회 무효" : "");

        // 시간초과 판정은 안쪽 판에 위임한다 — InterruptBoard와 같은 이유다
        protected override bool TimesOut => false;

        protected override void Setup()
        {
            _inner = Boards.Create(Spec?.type);
            _inner.Begin(Spec, Limit, Spec?.variant ?? "");

            // 고스트 페이스는 "같은 판·다른 시드"의 그 다른 시드다 — 실제 판을
            // 돌리는 데는 안 쓰고, 막대가 몇 초에 다 찰지만 뽑는 데 쓴다.
            // 55~85% 구간은 "이길 수 있는 경쟁자"를 위한 여유다 — 항상 지면
            // 보상이 죽은 기능이 되고, 항상 이기면 긴장이 없다
            var ghostRng = new System.Random(StableHash((Spec?.variant ?? "") + "#ghost"));
            _ghostFinishAt = Mathf.Lerp(Limit * 0.55f, Limit * 0.85f, (float)ghostRng.NextDouble());

            _resolved = false;
            _wonRace = false;
        }

        protected override void Advance(float dt, BoardInput input)
        {
            if (_inner == null) return;

            var state = _inner.Tick(dt, input);
            Fill = _inner.Fill;
            TimedOut = _inner.TimedOut;
            while (Mistakes < _inner.Mistakes) Miss();

            if (_resolved) return;

            if (state == BoardState.Cleared)
            {
                if (Elapsed < _ghostFinishAt)
                {
                    // 고스트보다 먼저 끝냈다 — 로컬 보상. 서버에 올리는 것은
                    // 여전히 `Mistakes` 하나뿐이라 판정 계약을 새로 만들지 않는다
                    Mistakes = Mathf.Max(0, Mistakes - 1);
                    _wonRace = true;
                }
                _resolved = true;
                Clear();
            }
            else if (state == BoardState.Failed)
            {
                _resolved = true;
                Fail();
            }
        }

        public override void Draw(HudTheme theme, Rect body)
        {
            const float ghostH = 34f;
            var ghostBar = new Rect(body.x, body.y, body.width, ghostH - 10f);
            var ghostRatio = Mathf.Clamp01(Elapsed / Mathf.Max(0.01f, _ghostFinishAt));
            var ghostDone = Elapsed >= _ghostFinishAt;

            GUI.Label(new Rect(ghostBar.x, ghostBar.y - 2f, ghostBar.width, 16f),
                ghostDone ? "고스트 — 먼저 끝냈다" : "고스트 페이스",
                theme.At(theme.Label, 12, HudTheme.Ink3));
            theme.Bar(new Rect(ghostBar.x, ghostBar.y + 14f, ghostBar.width, 10f), ghostRatio,
                ghostDone ? HudTheme.Heat : HudTheme.Cold);

            var inner = new Rect(body.x, body.y + ghostH + 20f, body.width, body.height - ghostH - 20f);
            _inner?.Draw(theme, inner);

            if (_wonRace)
            {
                var banner = new Rect(body.center.x - 170f, inner.y + 6f, 340f, 32f);
                theme.Fill(banner, HudTheme.AccentW, 0.92f);
                theme.Border(banner, HudTheme.Accent);
                GUI.Label(banner, "먼저 끝냈다 — 실수 1회 무효",
                    theme.At(theme.Small, 14, HudTheme.Accent, TextAnchor.MiddleCenter));
            }

            theme.Border(body, HudTheme.Rule);
        }
    }
}
