using UnityEngine;

namespace SoldierADay.Net
{
    /// <summary>
    /// `REACT` 즉시 반응 — 단독으로 쓰는 것은 `간부 순찰` 하나뿐이다.
    ///
    /// 쉘(패널) 하나가 색으로 신호를 준다. 평소엔 초록 — 안전하다는 뜻이고,
    /// 시드 고정 무작위 간격 뒤 빨강으로 바뀐 순간이 곧 누를 때다. 단발
    /// 입력 하나뿐이다.
    ///
    /// **나머지는 전부 인터럽트다.** 다른 원형 위에 끼워 넣는 모디파이어이고
    /// (`minigame.interrupt`), 그렇게 써야 돌발이 "일과와 같은 게임인데 이름만
    /// 다른 것"이 되지 않는다 — 진행 중인 판을 끊고 들어오는 것이 돌발의 성격이다.
    ///
    /// 미리 누르면 안 된다. 빨강으로 바뀌기 전에 누르는 것도 틀린 것이고,
    /// 그래서 **기다림이 이 판의 조작**이다. 판정 구조(`_next`/`_open`/
    /// `_window`/`Schedule`)는 원래 시야 원뿔 버전 그대로다 — 색으로
    /// 바뀐 것은 겉모습뿐이라 갈아 끼울 이유가 없었다.
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

        public override string Instruction => "쉘이 빨갛게 바뀌는 순간 눌러라 — 미리 누르면 틀린다";

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

            // 판 가운데 큰 쉘 하나. 시야 원뿔·경례 대상을 그리지 않는다 —
            // 이 판이 읽는 것은 오직 색이다: 초록이면 기다리고, 빨강이면 누른다
            var shell = new Rect(body.center.x - 150f, body.center.y - 90f, 300f, 170f);
            var live = _open > 0f;
            theme.Fill(shell, live ? HudTheme.AlertW : HudTheme.AccentW);
            theme.Border(shell, live ? HudTheme.Alert : HudTheme.Accent, live ? 3f : 2f);

            // 색만으로 안 읽히는 색각 대응 — 문구도 같이 바뀐다
            GUI.Label(shell, live ? "지금!" : "대기",
                theme.At(theme.Display, live ? 48 : 30,
                    live ? HudTheme.Alert : HudTheme.Accent, TextAnchor.MiddleCenter));

            // 남은 창 — 언제까지 누를 수 있는가
            if (live)
            {
                var bar = new Rect(shell.x, shell.yMax + 10f, shell.width, 8f);
                theme.Fill(bar, HudTheme.Rule3);
                theme.Fill(new Rect(bar.x, bar.y, bar.width * (_open / _window), bar.height),
                           HudTheme.Alert);
            }

            if (_flash > 0f)
            {
                GUI.Label(new Rect(body.x, shell.yMax + 30f, body.width, 32f), _verdict,
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
