using UnityEngine;

namespace SoldierADay.Net
{
    /// <summary>
    /// `REACT` 즉시 반응 — 단독으로 쓰는 것은 `간부 순찰` 하나뿐이다.
    ///
    /// 시야 원뿔에 들어온 순간 경례한다. 단발 입력 하나뿐이다.
    ///
    /// **나머지는 전부 인터럽트다.** 다른 원형 위에 끼워 넣는 모디파이어이고
    /// (`minigame.interrupt`), 그렇게 써야 돌발이 "일과와 같은 게임인데 이름만
    /// 다른 것"이 되지 않는다 — 진행 중인 판을 끊고 들어오는 것이 돌발의 성격이다.
    ///
    /// 미리 누르면 안 된다. 간부가 보기 전에 경례하는 것도 틀린 것이고,
    /// 그래서 **기다림이 이 판의 조작**이다.
    /// </summary>
    public sealed class ReactBoard : Board
    {
        private float _next;
        private float _open;
        private float _window;
        private int _done;
        private int _need;
        private float _flash;
        private string _verdict = "";

        public override string Instruction => "들어오는 순간 [SPACE] — 미리 누르면 틀린다";

        public override string Status => $"경례 {_done}/{_need}";

        protected override void Setup()
        {
            _need = Mathf.Clamp(ParamInt("count", 3), 1, 6);
            _window = Param("window", 1.4f) * Mathf.Lerp(1f, 0.72f, (Difficulty - 1f) * 0.5f);
            _done = 0;
            _open = 0f;
            Schedule();
        }

        /// <summary>다음 등장까지. 간격이 일정하면 세고 있으면 되므로 흔든다</summary>
        private void Schedule() => _next = RandRange(1.1f, Mathf.Max(1.6f, Limit / (_need + 1f)));

        protected override void Advance(float dt, BoardInput input)
        {
            _flash = Mathf.Max(0f, _flash - dt * 2.5f);

            if (_open > 0f)
            {
                _open -= dt;
                if (_open <= 0f)
                {
                    // 지나갔다. 못 본 것이다
                    _verdict = "놓쳤다";
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

            if (!input.Tap) return;

            if (_open > 0f)
            {
                _done += 1;
                _verdict = "경례";
                _flash = 1f;
                _open = 0f;
                Fill = (float)_done / _need;
                Schedule();
                if (_done >= _need) Clear();
                return;
            }

            // 아무도 없는데 경례했다
            _verdict = "성급했다";
            _flash = 1f;
            Miss();
        }

        public override void Draw(HudTheme theme, Rect body)
        {
            theme.Fill(body, HudTheme.Paper3);

            // 시야 원뿔 — 열려 있으면 밝다
            var cone = new Rect(body.center.x - 150f, body.center.y - 90f, 300f, 170f);
            var live = _open > 0f;
            theme.Fill(cone, live ? HudTheme.AlertW : HudTheme.Paper);
            theme.Border(cone, live ? HudTheme.Alert : HudTheme.Rule2, live ? 3f : 1f);

            GUI.Label(cone, live ? "간부" : "…",
                theme.At(theme.Display, live ? 44 : 30,
                    live ? HudTheme.Alert : HudTheme.Rule, TextAnchor.MiddleCenter));

            // 남은 창 — 언제까지 누를 수 있는가
            if (live)
            {
                var bar = new Rect(cone.x, cone.yMax + 10f, cone.width, 8f);
                theme.Fill(bar, HudTheme.Rule3);
                theme.Fill(new Rect(bar.x, bar.y, bar.width * (_open / _window), bar.height),
                           HudTheme.Alert);
            }

            if (_flash > 0f)
            {
                GUI.Label(new Rect(body.x, cone.yMax + 30f, body.width, 32f), _verdict,
                    theme.At(theme.Heading, 20,
                        _verdict == "경례" ? HudTheme.Accent : HudTheme.Alert,
                        TextAnchor.MiddleCenter));
            }

            for (var i = 0; i < _need; i += 1)
            {
                var dot = new Rect(body.center.x - _need * 13f + i * 26f, body.y + 26f, 18f, 18f);
                theme.Fill(dot, i < _done ? HudTheme.Accent : HudTheme.Rule2);
            }

            theme.Border(body, HudTheme.Rule);
        }
    }
}
