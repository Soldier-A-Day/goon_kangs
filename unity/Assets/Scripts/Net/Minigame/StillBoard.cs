using UnityEngine;

namespace SoldierADay.Net
{
    /// <summary>
    /// `STILL` 부동자세 — **입력을 참는 유일한 원형**(A-5 2차, 설계안 §A-5 2차).
    ///
    /// 나머지 열다섯 종은 눌러야 진행되는데 이 판은 반대다 — 판이 열리면
    /// "차렷"이고, 화면 속 몸(사각 실루엣)이 시간이 갈수록 떨리기 시작한다.
    /// 참는 시간이 길어질수록 떨림이 커지고, 그 폭이 곧 "참을성 게이지"다.
    /// 마지막에 진짜 "해산"이 뜨는 순간에만 눌러야 통과한다.
    ///
    /// ── 함정 금지 ────────────────────────────────────────────────────────
    /// 트위스트의 철칙(설계안 §A-5 1차 — "이미 보여준 규칙 안에서의 배신만
    /// 재미다. 몰래 넣으면 억울함이다")이 여기서도 그대로다. 그래서 중간에
    /// 지나가는 구령 1~2회는 "해산"을 흉내 내지 않는다 — 아예 다른 글자로,
    /// 눈에 잘 보이는 자리에 "해산이 아니다"라고 분명히 적는다. 유혹은
    /// 문구가 아니라 **떨림 자체**에서 나와야 한다.
    ///
    /// ── 판정 ─────────────────────────────────────────────────────────────
    /// 진짜 "해산" 전에 누르면 성급한 것이다 — 두 번까지는 `Miss()`로 봐주고
    /// (`MaxEarlyMistakes`), 세 번째는 되돌릴 수 없는 실수로 본다(`Fail()`).
    /// 진짜 "해산"이 뜬 뒤 반응 창(`window`, 기본 3초) 안에 못 누르면 그것도
    /// 되돌릴 수 없다 — 마지막 기회라 다음이 없다.
    ///
    /// ── 긴장감 패스 B — 무궁화꽃이 피었습니다 ─────────────────────────────
    /// 화면에 감시자(간부 실루엣)를 실제로 세운다. 딴 곳을 보다가(안전) 시드
    /// 고정 무작위 간격으로 이쪽을 돌아본다(감시) — 돌기 0.3초 전에 어깨가
    /// 들썩여 예고한다(트위스트 철칙과 같은 이유로, 몰래 돌지 않는다). 감시
    /// 중일 때만 마우스 이동·키 입력이 "감지 게이지"를 채우고, 게이지가
    /// 만땅이면 `Miss()` 한 번을 물고 다시 0에서 시작한다 — **즉사 아니다.**
    /// 진짜 "해산" 창(`_open`)이 열리면 이 관찰은 멈춘다 — 그때는 눌러야
    /// 하는 게 규칙이라, 그 입력을 감시 대상으로 세면 앞뒤가 안 맞는다.
    /// 기존 "해산 신호에 반응" 마무리(위 판정 문단)는 그대로 유지한다.
    /// </summary>
    public sealed class StillBoard : Board
    {
        /// <summary>일찍 누른 것은 이 횟수까지만 봐준다 — 그다음은 즉시 실패</summary>
        private const int MaxEarlyMistakes = 2;

        /// <summary>미끼 구령 하나가 화면에 남는 시간(초)</summary>
        private const float DecoyFlashDuration = 1.1f;

        private float[] _decoyAt;
        private int _decoyNext;
        private float _decoyFlashT;

        private float _disbandAt;
        private float _window;
        private bool _open;
        private float _openT;

        private int _earlyMistakes;
        private string _verdict = "";
        private float _verdictAge = 999f;

        /// <summary>떨림의 겉모습만 흔드는 개인차 — 같은 시드면 같은 떨림이 나온다</summary>
        private float _jitterSeed;

        /* ──────────────────────────────────────── 긴장감 패스 B — 감시자 */

        /// <summary>지금 감시자가 이쪽을 돌아보고 있는가(감시 중)</summary>
        private bool _watching;
        /// <summary>지금 단계(감시·비감시)가 끝날 때까지 남은 시간(초) — 시드 고정 랜덤</summary>
        private float _watchPhaseT;
        /// <summary>감지 게이지(0~1) — 감시 중 움직이면 차고, 그렇지 않으면 서서히 준다</summary>
        private float _detectGauge;
        /// <summary>직전 프레임 마우스 좌표 — 이동량을 재는 기준(음수면 아직 안 쟀다)</summary>
        private Vector2 _lastWatchMouse = new Vector2(-1f, -1f);

        /// <summary>마지막 3초 — 공통 하이라이트용 박동 타이머</summary>
        private float _finalBeatTimer;

        public override string Instruction => _open
            ? "해산! — 지금 눌러라"
            : "움직이지 마라 — '해산' 전에는 어떤 입력도 참는다. 감시자가 돌아보면 더더욱";

        public override string Status =>
            _open ? "해산 — 반응 창 안에"
                  : $"구령 대기 · 실수 {Mistakes} · 감시 {(_watching ? "중" : "소강")}";

        protected override void Setup()
        {
            // 미끼 구령은 1~2회. 난이도가 오르면 하나 늘어 판단할 거리가 많아진다
            var decoyCount = Mathf.Clamp(ParamInt("count", Difficulty <= 1f ? 1 : 2), 1, 2);

            // window는 마지막 해산의 반응 창(초). 워크오더가 못 박은 기본값은
            // 3초이고, 난이도는 그 창을 살짝만 좁힌다 — 이 판의 압박은 창이
            // 아니라 "얼마나 오래 참았는가"에 있다
            _window = Mathf.Max(1.5f, Param("window", 3f) * Mathf.Lerp(1f, 0.82f, (Difficulty - 1f) * 0.5f));

            // 진짜 해산은 제한 시간 뒤쪽 절반에서 뽑되, 반응 창이 다 열릴 자리와
            // 여유(0.6초)를 남긴다
            var disbandFloor = Limit * 0.55f;
            var disbandCeil = Mathf.Max(disbandFloor + 1f, Limit - _window - 0.6f);
            _disbandAt = RandRange(disbandFloor, disbandCeil);

            // 미끼는 해산보다 한참 전, 서로 간격을 두고 앞쪽에 흩는다
            _decoyAt = new float[decoyCount];
            var slot = _disbandAt / (decoyCount + 1f);
            for (var i = 0; i < decoyCount; i += 1)
            {
                var center = slot * (i + 1f);
                _decoyAt[i] = Mathf.Clamp(center + RandRange(-slot * 0.3f, slot * 0.3f), 0.8f, _disbandAt - 1f);
            }

            _decoyNext = 0;
            _decoyFlashT = 0f;
            _open = false;
            _openT = 0f;
            _earlyMistakes = 0;
            _verdict = "";
            _verdictAge = 999f;
            _jitterSeed = (float)Rng.NextDouble() * 100f;

            // 감시자 — 처음엔 딴 곳을 본다. 다음 단계 길이도 시드 고정 Rng로 뽑는다
            _watching = false;
            _watchPhaseT = RandRange(1.3f, 2.2f);
            _detectGauge = 0f;
            _lastWatchMouse = new Vector2(-1f, -1f);
        }

        protected override void Advance(float dt, BoardInput input)
        {
            _verdictAge += dt;
            if (_decoyFlashT > 0f) _decoyFlashT -= dt;
            FinalStretchTick(dt);

            if (_decoyNext < _decoyAt.Length && Elapsed >= _decoyAt[_decoyNext])
            {
                _decoyFlashT = DecoyFlashDuration;
                _decoyNext += 1;
            }

            if (_open)
            {
                _openT -= dt;
                if (input.Tap)
                {
                    Fill = 1f;
                    Clear();
                    return;
                }
                if (_openT <= 0f)
                {
                    // 마지막 해산을 놓쳤다 — 되돌릴 기회가 없다
                    _verdict = "해산을 놓쳤다";
                    _verdictAge = 0f;
                    Fail();
                }
                return;
            }

            // 감시자는 진짜 "해산" 전까지만 돈다 — 열리면 눌러야 하는 게 규칙이라
            // 그 입력을 감시 대상으로 세면 앞뒤가 안 맞는다
            UpdateWatcher(dt, input);

            if (Elapsed >= _disbandAt)
            {
                _open = true;
                _openT = _window;
                _verdict = "해산!";
                _verdictAge = 0f;
                return;
            }

            if (input.Tap)
            {
                _earlyMistakes += 1;
                _verdict = "성급했다";
                _verdictAge = 0f;
                if (_earlyMistakes > MaxEarlyMistakes) Fail();
                else Miss();
            }

            Fill = Mathf.Clamp01(Elapsed / Mathf.Max(0.01f, _disbandAt));
        }

        /// <summary>
        /// 감시자의 돌아보기 주기와 감지 게이지. 돌아보는 타이밍은 시드 고정
        /// `Rng`로 뽑아 매번 같지만, 언제 도는지는 화면(어깨 들썩)이 먼저
        /// 알려준다 — 몰래 돌지 않는다.
        /// </summary>
        private void UpdateWatcher(float dt, BoardInput input)
        {
            _watchPhaseT -= dt;
            if (_watchPhaseT <= 0f)
            {
                _watching = !_watching;
                _watchPhaseT = _watching ? RandRange(0.8f, 1.6f) : RandRange(1.3f, 2.4f);
            }

            var moved = _lastWatchMouse.x >= 0f && (input.Mouse - _lastWatchMouse).sqrMagnitude > 4f;
            _lastWatchMouse = input.Mouse;
            var keyed = Mathf.Abs(input.Horizontal) > 0.1f || Mathf.Abs(input.Vertical) > 0.1f;

            if (_watching && (moved || keyed))
            {
                _detectGauge = Mathf.Clamp01(_detectGauge + dt * 1.4f);
                if (_detectGauge >= 1f)
                {
                    // 즉사 아니다 — 실수 하나만 물고 다시 0에서 시작한다
                    _detectGauge = 0f;
                    Miss();
                }
            }
            else
            {
                _detectGauge = Mathf.Max(0f, _detectGauge - dt * 0.6f);
            }
        }

        /// <summary>마지막 3초 — 펄스 테두리 + 저음 심박(공통 패스 B 항목)</summary>
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

            // 감시자 — 위쪽에 작은 실루엣. 딴 곳을 보면 무채색, 돌아보면
            // 경고색. 돌기 0.3초 전엔 어깨가 들썩인다(예고, 함정 아님)
            if (!_open)
            {
                var pretell = !_watching && _watchPhaseT <= 0.3f && _watchPhaseT > 0f;
                var twitch = pretell ? (1f - _watchPhaseT / 0.3f) * 5f : 0f;
                var wy = body.y + 34f + Mathf.Sin(Time.unscaledTime * 26f) * twitch;

                var watcher = new Rect(body.center.x - 16f, wy, 32f, 46f);
                theme.Fill(watcher, _watching ? HudTheme.AlertW : HudTheme.Paper2);
                theme.Border(watcher, _watching ? HudTheme.Alert : HudTheme.Rule2, 2f);
                GUI.Label(new Rect(watcher.x - 70f, watcher.yMax + 4f, watcher.width + 140f, 20f),
                    _watching ? "감시자가 돌아봤다" : pretell ? "어깨가 들썩인다…" : "감시자는 딴 곳을 본다",
                    theme.At(theme.Label, 12,
                        _watching ? HudTheme.Alert : pretell ? HudTheme.Heat : HudTheme.Ink3,
                        TextAnchor.MiddleCenter));

                // 감지 게이지 — 감시 중에 움직이면 찬다. 만땅이면 실수 하나(즉사 아님)
                var gaugeRect = new Rect(body.center.x - 90f, watcher.yMax + 26f, 180f, 10f);
                theme.Bar(gaugeRect, _detectGauge, _detectGauge > 0.7f ? HudTheme.Alert : HudTheme.Heat);
            }

            // 떨림 — 참는 시간이 길어질수록 커진다. 해산이 뜨면 참는 게 아니라
            // "지금 누를 차례"이므로 떨림을 접는다
            var tremorK = _open ? 0f : Mathf.Clamp01(Elapsed / Mathf.Max(0.01f, _disbandAt));
            var amp = Mathf.Lerp(1f, 9f, tremorK);
            var jx = Mathf.PerlinNoise(_jitterSeed, Time.unscaledTime * 9f) * 2f - 1f;
            var jy = Mathf.PerlinNoise(Time.unscaledTime * 9f, _jitterSeed) * 2f - 1f;

            var figure = new Rect(body.center.x - 22f + jx * amp, body.center.y - 46f + jy * amp, 44f, 92f);
            theme.Fill(figure, _open ? HudTheme.AccentW : HudTheme.Paper2);
            theme.Border(figure, _open ? HudTheme.Accent : HudTheme.Rule, 2f);
            if (!_open)
            {
                GUI.Label(new Rect(figure.x - 60f, figure.yMax + 8f, figure.width + 120f, 22f),
                    tremorK > 0.55f ? "떨고 있다" : "차렷",
                    theme.At(theme.Small, 14, HudTheme.Ink2, TextAnchor.MiddleCenter));
            }

            // 참을성 게이지 — 떨림과 나란히 찬다
            var gauge = new Rect(body.center.x - 160f, body.yMax - 46f, 320f, 16f);
            theme.Bar(gauge, tremorK, tremorK > 0.75f ? HudTheme.Heat : HudTheme.Accent);
            GUI.Label(new Rect(gauge.x, gauge.y - 20f, gauge.width, 18f), "참는 중",
                theme.At(theme.Label, 13, HudTheme.Ink3, TextAnchor.MiddleCenter));

            // 미끼 구령 — "해산"이 아니라고 분명히 적는다. 몰래 덮지 않는다
            if (_decoyFlashT > 0f)
            {
                var band = new Rect(body.x, body.y + 20f, body.width, 40f);
                theme.Fill(band, HudTheme.Paper2, 0.92f);
                GUI.Label(band, "구령 — \"해산\" 아니다, 그대로",
                    theme.At(theme.Heading, 18, HudTheme.Cold, TextAnchor.MiddleCenter));
            }

            if (_open)
            {
                // 진짜 해산 — 알리지 않을 수 없게 크게
                var band = new Rect(body.x, body.center.y + 70f, body.width, 60f);
                theme.Fill(band, HudTheme.AlertW, 0.95f);
                theme.Border(band, HudTheme.Alert, 3f);
                GUI.Label(band, "해산!", theme.At(theme.Display, 30, HudTheme.Alert, TextAnchor.MiddleCenter));

                var bar = new Rect(band.x + 40f, band.yMax + 8f, band.width - 80f, 8f);
                theme.Fill(bar, HudTheme.Rule3);
                theme.Fill(new Rect(bar.x, bar.y, bar.width * Mathf.Clamp01(_openT / _window), bar.height),
                    HudTheme.Alert);
            }
            else if (_verdictAge < 0.9f)
            {
                GUI.Label(new Rect(body.x, body.y + 66f, body.width, 30f), _verdict,
                    theme.At(theme.Heading, 20, HudTheme.Alert, TextAnchor.MiddleCenter));
            }

            theme.Border(body, HudTheme.Rule);
            FinalStretchDraw(theme, body);
        }
    }
}
