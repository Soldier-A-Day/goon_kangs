using UnityEngine;

namespace SoldierADay.Net
{
    /// <summary>
    /// `TIMING` 타이밍 바 — 3건. 시약 적정 · 팩 박기 · 장작 패기.
    ///
    /// 바가 왕복하고, 판정 구간에서 멈춘다. 단발 입력 하나뿐이다.
    ///
    /// **미스 3회면 재시도다.** 그래서 급하게 두드리면 진다 — 창이 지나가길
    /// 기다리는 것이 이 판의 조작이다.
    ///
    /// 명중할 때마다 창이 옮겨 가고 좁아진다. 한 자리에 고정하면 리듬이 되고,
    /// 리듬은 `RHYTHM`의 몫이다.
    /// </summary>
    public sealed class TimingBoard : Board
    {
        private float _cursor;
        private int _direction = 1;
        private float _speed;
        private float _window;
        private float _center;
        private int _hits;
        private int _need;
        private int _miss;
        private float _flash;
        private bool _good;

        public override string Instruction => "[SPACE] 또는 클릭 — 구간에서 멈춰라";

        public override string Status => $"명중 {_hits}/{_need}  ·  미스 {_miss}/3";

        protected override void Setup()
        {
            _need = Mathf.Clamp(ParamInt("hits", 4), 1, 8);
            _speed = Param("speed", 0.85f) * Mathf.Lerp(1f, 1.35f, (Difficulty - 1f) * 0.5f);
            _window = Param("window", 0.12f);
            _hits = 0;
            _miss = 0;
            _cursor = 0f;
            _direction = 1;
            Place();
        }

        private void Place()
        {
            // 양 끝은 피한다 — 벽에 붙은 창은 튕겨 나오는 순간 공짜로 맞는다
            _center = RandRange(0.22f, 0.78f);
        }

        protected override void Advance(float dt, BoardInput input)
        {
            _flash = Mathf.Max(0f, _flash - dt * 3f);

            _cursor += _direction * _speed * dt;
            if (_cursor >= 1f) { _cursor = 1f; _direction = -1; }
            if (_cursor <= 0f) { _cursor = 0f; _direction = 1; }

            if (!input.Tap) return;

            var half = _window * 0.5f;
            _good = Mathf.Abs(_cursor - _center) <= half;
            _flash = 1f;

            if (_good)
            {
                _hits += 1;
                Fill = (float)_hits / _need;
                if (_hits >= _need) { Clear(); return; }
                // 맞출수록 좁아진다. 마지막 한 번이 가장 어렵다
                _window = Mathf.Max(0.05f, _window * 0.88f);
                Place();
                return;
            }

            _miss += 1;
            Miss();
            if (_miss >= 3) Fail();
        }

        public override void Draw(HudTheme theme, Rect body)
        {
            theme.Fill(body, HudTheme.Paper3);

            var track = new Rect(body.x + 60f, body.center.y - 24f, body.width - 120f, 48f);
            theme.Fill(track, HudTheme.Paper);

            // 판정 구간
            var half = _window * 0.5f;
            var win = new Rect(track.x + track.width * (_center - half), track.y,
                               track.width * _window, track.height);
            theme.Fill(win, HudTheme.AccentW);
            theme.Border(win, HudTheme.Accent, 2f);

            // 바늘
            var x = track.x + track.width * _cursor;
            theme.Fill(new Rect(x - 3f, track.y - 10f, 6f, track.height + 20f),
                Mathf.Abs(_cursor - _center) <= half ? HudTheme.Accent : HudTheme.Ink);

            theme.Border(track, HudTheme.Rule, 2f);

            if (_flash > 0f)
            {
                GUI.Label(new Rect(body.x, track.yMax + 22f, body.width, 34f),
                    _good ? "명중" : "빗나감",
                    theme.At(theme.Heading, 22, _good ? HudTheme.Accent : HudTheme.Alert,
                        TextAnchor.MiddleCenter));
            }

            // 명중 표시 — 몇 번 남았는지
            for (var i = 0; i < _need; i += 1)
            {
                var dot = new Rect(body.center.x - _need * 13f + i * 26f, body.y + 26f, 18f, 18f);
                theme.Fill(dot, i < _hits ? HudTheme.Accent : HudTheme.Rule2);
            }

            theme.Border(body, HudTheme.Rule);
        }
    }
}
