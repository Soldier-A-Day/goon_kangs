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
    ///
    /// ── 긴장감 패스 B — 골프 더블 판정 ────────────────────────────────────
    /// 클릭 한 번으로 끝나던 것을 두 번으로 늘렸다. 1차 클릭은 지금까지의
    /// 판정 그대로(`_window` 폭, "명중/빗나감")고, 통과하면 바가 그 자리에서
    /// **바로 반대로 돌아온다** — 되짚어 오는 두 번째 구간이 훨씬 좁은 "정밀
    /// 존"이다. 2차 클릭은 실패가 없다(등급 축을 그대로 두는 것이 조건이라
    /// 새 실패 사유를 만들면 안 된다) — 대신 얼마나 가운데에 눌렀는지로
    /// PERFECT · GOOD · OK 세 등급을 매기고, 화면과 소리로만 크게 보상한다.
    /// PERFECT라고 실수를 깎거나 시간을 보태지 않는다 — 그건 `Board.Grade()`의
    /// 몫이고, 여기서 건드리면 14종이 공유하는 잣대가 판마다 달라진다.
    /// </summary>
    public sealed class TimingBoard : Board
    {
        private enum SwingPhase
        {
            /// <summary>1차 — 예전과 같은 판정 폭(`_window`)</summary>
            Power,
            /// <summary>2차 — 되짚어 오는 좁은 정밀 존</summary>
            Precision,
        }

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

        /* ──────────────────────────────────────── 긴장감 패스 B — 골프 더블 판정 */

        /// <summary>정밀 존이 1차 판정 구간(`_window`)의 몇 배 폭인가 — "더 좁게"의 실제 값</summary>
        private const float PrecisionRatio = 0.4f;

        private SwingPhase _phase;
        /// <summary>2차 판정 폭. `_window`가 라운드마다 줄어드는 만큼 같이 줄어든다</summary>
        private float _precisionWindow;
        /// <summary>이번 라운드의 등급 — "" · PERFECT · GOOD · OK</summary>
        private string _tier = "";

        /// <summary>마지막 3초 — 공통 하이라이트용 박동 타이머</summary>
        private float _finalBeatTimer;

        public override string Instruction =>
            _phase == SwingPhase.Precision
                ? "[SPACE] 또는 클릭 — 되돌아오는 정밀 존에서 한 번 더"
                : "[SPACE] 또는 클릭 — 구간에서 멈춰라(두 번 맞혀야 한다)";

        public override string Status =>
            _phase == SwingPhase.Precision
                ? $"명중 {_hits}/{_need}  ·  2차(정밀) 대기  ·  미스 {_miss}/3"
                : $"명중 {_hits}/{_need}  ·  미스 {_miss}/3";

        protected override void Setup()
        {
            _need = Mathf.Clamp(ParamInt("hits", 4), 1, 8);
            _speed = Param("speed", 0.85f) * Mathf.Lerp(1f, 1.35f, (Difficulty - 1f) * 0.5f);
            _window = Param("window", 0.12f);
            _precisionWindow = _window * PrecisionRatio;
            _hits = 0;
            _miss = 0;
            _cursor = 0f;
            _direction = 1;
            _phase = SwingPhase.Power;
            _tier = "";
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
            FinalStretchTick(dt);

            _cursor += _direction * _speed * dt;
            if (_cursor >= 1f) { _cursor = 1f; _direction = -1; }
            if (_cursor <= 0f) { _cursor = 0f; _direction = 1; }

            PlayApproachCue(dt);

            if (!input.Tap) return;

            if (_phase == SwingPhase.Power) HandlePowerTap();
            else HandlePrecisionTap();
        }

        /// <summary>1차 클릭 — 예전과 같은 판정. 통과하면 바가 그 자리에서 반대로 돌아온다</summary>
        private void HandlePowerTap()
        {
            var half = _window * 0.5f;
            _good = Mathf.Abs(_cursor - _center) <= half;
            _flash = 1f;
            _tier = "";

            if (_good)
            {
                // 골프 스윙처럼 — 파워를 재고 나면 바로 되짚어 온다
                _direction = -_direction;
                _phase = SwingPhase.Precision;
                Sfx.Play("tap", 0.9f, 1.1f);
                return;
            }

            _miss += 1;
            Miss();
            if (_miss >= 3) Fail();
        }

        /// <summary>
        /// 2차 클릭 — 정밀 존. 실패가 없다(등급 축은 그대로 실수·잔여 시간만 본다).
        /// 얼마나 가운데에 눌렀는지로만 PERFECT · GOOD · OK를 가른다.
        /// </summary>
        private void HandlePrecisionTap()
        {
            var dist = Mathf.Abs(_cursor - _center);
            _tier = dist <= _precisionWindow * 0.5f ? "PERFECT"
                  : dist <= _window * 0.5f ? "GOOD"
                  : "OK";

            _good = true;
            _flash = 1f;
            _hits += 1;
            Fill = (float)_hits / _need;

            // PERFECT는 실수 감산도 시간 보너스도 없다 — 오직 보이고 들리는 것만 커진다
            if (_tier == "PERFECT") Sfx.Play("success", 1f, 1.3f);
            else Sfx.Play("tap", 0.85f, 1f);

            if (_hits >= _need) { Clear(); return; }

            // 라운드마다 존 10% 축소 — 1차·2차 폭이 같이 좁아진다
            _window = Mathf.Max(0.05f, _window * 0.9f);
            _precisionWindow = _window * PrecisionRatio;
            Place();
            _phase = SwingPhase.Power;
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

        /// <summary>마지막 3초 — 펄스 테두리 + 저음 심박(공통 패스 B 항목). 남은 시간이
        /// 짧아질수록 박동이 빨라진다. `Board.Remaining`이 3 이하로 떨어졌을
        /// 때만 켜지고, 판이 멈추면(성공·실패) `Advance`가 더는 안 불려 조용해진다</summary>
        private void FinalStretchTick(float dt)
        {
            if (State != BoardState.Running || Remaining > 3f || Remaining <= 0f) return;
            _finalBeatTimer -= dt;
            if (_finalBeatTimer > 0f) return;
            var urgency = 1f - Mathf.Clamp01(Remaining / 3f);
            Sfx.Play("tap", 0.5f, Mathf.Lerp(0.75f, 0.5f, urgency));
            _finalBeatTimer = Mathf.Lerp(0.5f, 0.22f, urgency);
        }

        private void FinalStretchDraw(HudTheme theme, Rect body)
        {
            if (State != BoardState.Running || Remaining > 3f || Remaining <= 0f) return;
            var urgency = 1f - Mathf.Clamp01(Remaining / 3f);
            if (!HudTheme.Pulse(Mathf.Lerp(2f, 4f, urgency))) return;
            theme.Border(body, HudTheme.Alert, 3f);
        }

        public override void Draw(HudTheme theme, Rect body)
        {
            theme.Fill(body, HudTheme.Paper3);

            var track = new Rect(body.x + 60f, body.center.y - 24f, body.width - 120f, 48f);
            theme.Fill(track, HudTheme.Paper);

            // 1차(파워) 구간 — 실제 판정 폭(`_window`)은 그대로 두고, **보이는** 폭만
            // 난이도 3에서 줄인다. 시각 마커가 좁아진 만큼은 `PlayApproachCue`의
            // 피치가 메운다(시각 큐는 줄이는 것이지 없애는 것이 아니다)
            var visualWindow = Difficulty >= 2.5f ? _window * 0.45f : _window;
            var half = visualWindow * 0.5f;
            var win = new Rect(track.x + track.width * (_center - half), track.y,
                               track.width * visualWindow, track.height);
            theme.Fill(win, HudTheme.AccentW);
            theme.Border(win, HudTheme.Accent, 2f);

            // 2차(정밀) 존 — 지금 그 단계일 때만 겹쳐 그린다. 눈에 띄게 트랙 위아래로 삐져나온다
            if (_phase == SwingPhase.Precision)
            {
                var pHalf = _precisionWindow * 0.5f;
                var pWin = new Rect(track.x + track.width * (_center - pHalf), track.y - 6f,
                                    track.width * _precisionWindow, track.height + 12f);
                theme.Fill(pWin, HudTheme.Heat, 0.35f);
                theme.Border(pWin, HudTheme.Heat, 2f);
            }

            // 바늘
            var x = track.x + track.width * _cursor;
            theme.Fill(new Rect(x - 3f, track.y - 10f, 6f, track.height + 20f),
                Mathf.Abs(_cursor - _center) <= half ? HudTheme.Accent : HudTheme.Ink);

            theme.Border(track, HudTheme.Rule, 2f);

            if (_flash > 0f)
            {
                var label = _phase == SwingPhase.Power && !_good ? "빗나감"
                          : _tier != "" ? _tier
                          : "1차 통과 — 곧바로 되돌아온다";
                var color = _tier == "PERFECT" ? HudTheme.Heat
                          : !_good ? HudTheme.Alert
                          : HudTheme.Accent;
                GUI.Label(new Rect(body.x, track.yMax + 22f, body.width, 34f), label,
                    theme.At(theme.Heading, 22, color, TextAnchor.MiddleCenter));
            }

            // 명중 표시 — 몇 번 남았는지
            for (var i = 0; i < _need; i += 1)
            {
                var dot = new Rect(body.center.x - _need * 13f + i * 26f, body.y + 26f, 18f, 18f);
                theme.Fill(dot, i < _hits ? HudTheme.Accent : HudTheme.Rule2);
            }

            theme.Border(body, HudTheme.Rule);
            FinalStretchDraw(theme, body);
        }
    }
}
