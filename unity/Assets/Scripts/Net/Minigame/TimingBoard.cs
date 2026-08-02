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
    ///
    /// ── A-5 2차 — 접근을 피치로 듣는다 ───────────────────────────────────
    /// 바늘이 판정 구간에 가까워질수록 `tap` 클립을 더 높은 피치로, 더 자주
    /// 튕긴다("가까울수록 더 급해진다" — 가이거 계수기와 같은 문법). 구간
    /// 밖에서는 조용하다 — 판이 도는 내내 배경 잡음이 되면 그 자체로 피로다.
    /// 실제 판정(`_window`)은 전혀 안 건드린다 — 소리는 어디까지나 보조고,
    /// 소리 없이도 시각 바늘만으로 깰 수 있어야 한다(청각장애 접근성 최저선).
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

        /// <summary>이 거리 안쪽부터 접근음이 들리기 시작한다. 그 밖은 조용하다</summary>
        private const float CueRange = 0.3f;

        private float _cueTimer;

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

            PlayApproachCue(dt);

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

        /// <summary>바늘-구간 거리를 피치로 튕긴다. 멀면 낮고 뜸하게, 가까울수록 높고
        /// 잦게 — 간격 자체가 좁아지는 것도 접근감의 일부다. 실제 명중 판정과는
        /// 완전히 분리된 연출용 타이머라 `_window`가 아니라 `CueRange`를 쓴다</summary>
        private void PlayApproachCue(float dt)
        {
            var dist = Mathf.Abs(_cursor - _center);
            if (dist > CueRange)
            {
                _cueTimer = 0f; // 범위 밖 — 다음에 들어오는 즉시 울리도록 리셋해 둔다
                return;
            }

            _cueTimer -= dt;
            if (_cueTimer > 0f) return;

            var closeness = 1f - Mathf.Clamp01(dist / CueRange);
            Sfx.Play("tap", 0.16f, Mathf.Lerp(0.6f, 1.9f, closeness));
            _cueTimer = Mathf.Lerp(0.22f, 0.06f, closeness);
        }

        public override void Draw(HudTheme theme, Rect body)
        {
            theme.Fill(body, HudTheme.Paper3);

            var track = new Rect(body.x + 60f, body.center.y - 24f, body.width - 120f, 48f);
            theme.Fill(track, HudTheme.Paper);

            // 판정 구간 — 실제 판정 폭(`_window`)은 그대로 두고, **보이는** 폭만
            // 난이도 3에서 줄인다. 시각 마커가 좁아진 만큼은 `PlayApproachCue`의
            // 피치가 메운다 — 판정 자체가 어려워지는 게 아니라 "무엇으로 알아채는가"가
            // 바뀌는 것이다(시각 큐는 줄이는 것이지 없애는 것이 아니다)
            var visualWindow = Difficulty >= 2.5f ? _window * 0.45f : _window;
            var half = visualWindow * 0.5f;
            var win = new Rect(track.x + track.width * (_center - half), track.y,
                               track.width * visualWindow, track.height);
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
