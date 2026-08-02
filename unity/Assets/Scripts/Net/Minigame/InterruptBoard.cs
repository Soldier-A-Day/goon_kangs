using UnityEngine;

namespace SoldierADay.Net
{
    /// <summary>
    /// `REACT` 인터럽트 모디파이어.
    ///
    /// **다른 원형 위에 끼워 넣는다.** 환자 후송(`BALANCE`)에 계단 구간이 끼고,
    /// 누수·정전(`TRACE`)에 밸브 차단이 낀다 — `REACT`를 단독 게임으로 쓰지
    /// 않는다는 설계안의 원칙이 여기서 실행된다.
    ///
    /// ── 왜 겉을 씌우는가 ─────────────────────────────────────────────────
    /// 원형마다 인터럽트를 넣으면 열네 번 같은 코드를 쓰게 되고, 그 중 하나만
    /// 빠뜨려도 그 일과에서만 조용히 안 뜬다. 한 겹 씌우면 어떤 원형에든 붙고,
    /// 붙일지 말지는 데이터가 정한다(`minigame.interrupt`).
    ///
    /// ── 왜 실패로 끝내지 않는가 ──────────────────────────────────────────
    /// 못 받으면 실수로만 센다. 인터럽트가 곧 실패면 24초짜리 후송이 1초 반응
    /// 하나로 무산되고, 그건 운반 판이 아니라 반응 판이 된다. 등급으로만 갚는다.
    /// </summary>
    public sealed class InterruptBoard : Board
    {
        private readonly Board _inner;

        private float _next;
        private float _open;
        private float _window;
        private int _caught;
        private int _missed;
        private float _flash;
        private bool _good;

        public InterruptBoard(Board inner) => _inner = inner;

        // 시간초과 판정은 안쪽 판에 완전히 위임한다(A-5 2차 관통 수리). 이
        // 래퍼가 스스로 `Elapsed >= Limit`을 판정해 버리면, 안쪽 판이 같은
        // 프레임에 `GradesOnTimeout`으로 이미 통과시킨 결과와 경쟁하게 된다
        // — `JointBoard`가 이미 쓰는 것과 같은 위임이다.
        protected override bool TimesOut => false;

        public override string Instruction =>
            _open > 0f ? "[SPACE] 지금!" : _inner.Instruction;

        public override string Status =>
            _inner.Status + (_caught + _missed > 0 ? $"  ·  경례 {_caught}/{_caught + _missed}" : "");

        protected override void Setup()
        {
            // 안쪽 판은 같은 정의·같은 제한 시간으로 돈다. 인터럽트는 그 위에 얹힌다
            _inner.Begin(Spec, Limit, Spec?.variant ?? "");
            _window = 1.3f * Mathf.Lerp(1f, 0.75f, (Difficulty - 1f) * 0.5f);
            Schedule();
        }

        /// <summary>
        /// 다음 인터럽트까지.
        ///
        /// 첫 번째는 판이 손에 익을 때까지 기다린다 — 열자마자 끊고 들어오면
        /// 무슨 판인지 읽기도 전에 실수가 된다.
        /// </summary>
        private void Schedule() => _next = RandRange(Limit * 0.22f, Limit * 0.42f);

        protected override void Advance(float dt, BoardInput input)
        {
            _flash = Mathf.Max(0f, _flash - dt * 2.5f);

            if (_open > 0f)
            {
                _open -= dt;
                if (_open <= 0f)
                {
                    _missed += 1;
                    _good = false;
                    _flash = 1f;
                    Miss();
                    Schedule();
                }
            }
            else
            {
                _next -= dt;
                if (_next <= 0f) _open = _window;
            }

            // 인터럽트를 받는 입력은 **안쪽 판에서 걷어낸다.** 안 그러면 경례가
            // 그대로 분류 클릭이 되어 오분류로 셈해진다
            var passthrough = input;
            if (_open > 0f && input.Tap)
            {
                _caught += 1;
                _good = true;
                _flash = 1f;
                _open = 0f;
                Schedule();

                passthrough.Tap = false;
                passthrough.Pressed = false;
            }

            var state = _inner.Tick(dt, passthrough);
            Fill = _inner.Fill;
            // 안쪽 판이 시간초과로 끝났으면 그 사실도 그대로 옮긴다 — 안 옮기면
            // `Grade()`가 시간초과 전용 잣대(Fill 기준) 대신 실수·잔여율로
            // 매겨져, 안쪽 판이 이미 통과시킨 결과와 등급이 어긋난다
            TimedOut = _inner.TimedOut;

            while (Mistakes < _inner.Mistakes + _missed) Miss();

            if (state == BoardState.Cleared) Clear();
            else if (state == BoardState.Failed) Fail();
        }

        public override void Draw(HudTheme theme, Rect body)
        {
            _inner.Draw(theme, body);

            if (_open <= 0f && _flash <= 0f) return;

            // 판 위를 가로지르는 띠. 진행 중인 판을 **끊고 들어온다**는 것이
            // 화면에서도 그래야 한다 — 구석에 작게 뜨면 인터럽트가 아니다
            var band = new Rect(body.x, body.center.y - 44f, body.width, 88f);

            if (_open > 0f)
            {
                theme.Fill(band, HudTheme.AlertW, 0.94f);
                theme.Border(band, HudTheme.Alert, 3f);
                GUI.Label(new Rect(band.x, band.y + 12f, band.width, 34f), "간부 등장 — 경례",
                    theme.At(theme.Heading, 22, HudTheme.Alert, TextAnchor.MiddleCenter));

                var bar = new Rect(band.center.x - 140f, band.yMax - 26f, 280f, 8f);
                theme.Fill(bar, HudTheme.Rule3);
                theme.Fill(new Rect(bar.x, bar.y, bar.width * (_open / _window), bar.height),
                           HudTheme.Alert);
                return;
            }

            GUI.Label(band, _good ? "경례" : "못 봤다",
                theme.At(theme.Heading, 22, _good ? HudTheme.Accent : HudTheme.Alert,
                    TextAnchor.MiddleCenter));
        }
    }
}
