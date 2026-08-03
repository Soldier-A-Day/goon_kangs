using UnityEngine;

namespace SoldierADay.Net
{
    /// <summary>
    /// `MASH` 연타 작업 — 5건. 제설 · 제초 · 배수로 · 하역.
    ///
    /// 게이지가 계속 새고, 두드려서 채운다. 입력 하나뿐이다.
    ///
    /// **연타는 예외 없이 켜져 있다.** 예전에는 §15.0을 근거로 전면 금지했고
    /// 접근성 옵션이 모든 판을 홀드로 떨어뜨렸는데, 원형 14종이 저마다 다른
    /// 조작을 요구하는 설계에서 그 스위치 하나는 14종을 통째로 지우는 것과
    /// 같아졌다. 그래서 스위치를 뺐다.
    ///
    /// `stamina`가 1보다 작으면 **더 빨리 샌다**. 게이지 획득량을 깎지 않는
    /// 이유는, 깎으면 필요한 타수가 그대로 늘어 손이 못 따라가는 벽이 되기
    /// 때문이다 — 새는 속도로 옮기면 같은 압박이 "쉬면 잃는다"로 바뀐다.
    ///
    /// ── A-5 벤치마크 이식 S4 — Bishi Bashi 변주 ─────────────────────────────
    /// 예전에는 채워질 때까지 같은 입력을 반복하기만 하면 됐다 — 손은 바쁜데
    /// 머리는 할 일이 없었다. Bishi Bashi 계열 미니게임이 주는 손맛은 "지금
    /// 뭘 눌러야 하는지가 계속 바뀐다"는 데 있다. 그래서 게이지(=진행률)를
    /// 3구간으로 쪼갠다 — **순수 연타 → A/D 교대 → 막판 2배 저항**. `taps`
    /// `drain` `stamina`는 그대로다(밸런스 유지) — 바뀐 것은 "무엇을 눌러야
    /// 유효타인가"뿐이고, 게이지 한 칸의 가치와 새는 속도의 기준선은 원래
    /// 설계를 그대로 물려받는다.
    /// </summary>
    public sealed class MashBoard : Board
    {
        /// <summary>지금이 3구간 중 어디인가 — 게이지 값 하나로 정해진다(순서를 건너뛸 수 없다)</summary>
        private enum MashPhase
        {
            /// <summary>순수 연타 — 예전 그대로, 클릭·Space가 전부 유효타다</summary>
            Pure,
            /// <summary>A/D 교대 연타 — 지금 요구하는 방향과 같을 때만 유효타다</summary>
            Alternate,
            /// <summary>막판 스퍼트 — 교대 규칙은 그대로에 게이지가 90% 근처부터 더 빨리 샌다</summary>
            Spurt,
        }

        /// <summary>순수 연타 구간이 끝나는 게이지 값 — 이 위부터 교대 연타를 요구한다</summary>
        private const float PureEnd = 0.4f;

        /// <summary>스퍼트(2배 저항) 구간이 시작되는 게이지 값 — "90% 근처"</summary>
        private const float SpurtStart = 0.88f;

        /// <summary>스퍼트 구간의 감쇠 배율 — 쥐어짜야 겨우 버티는 손맛</summary>
        private const float SpurtDrainMultiplier = 1.8f;

        /// <summary>구간 전환 문구가 보이는 시간(초)</summary>
        private const float AnnounceDuration = 1f;

        /// <summary>공통 — 마지막 3초 심장 박동 tap 간격(초)</summary>
        private const float HeartbeatInterval = 0.5f;

        private float _gauge;
        private float _gain;
        private float _drain;
        private int _dropped;
        private float _kick;
        private float _rate;
        private float _rateWindow;
        private int _tapsInWindow;

        private MashPhase _phase;
        /// <summary>지난 프레임 A/D의 방향(-1·0·1) — 같은 방향을 누르고 있는 동안은 다시 세지 않는다</summary>
        private int _prevDir;
        /// <summary>지금 유효타로 쳐주는 방향 — 맞히면 반대로 뒤집힌다</summary>
        private int _expectedDir;
        private float _phaseAnnounceT;
        private string _phaseAnnounceText = "";

        /// <summary>공통 — 마지막 3초 심장 박동까지 남은 시간(초)</summary>
        private float _heartbeatCd;

        public override string Instruction => _phase switch
        {
            MashPhase.Alternate => "A · D 를 번갈아 눌러라",
            MashPhase.Spurt => "A · D 번갈아 — 거의 다 왔다, 쥐어짜라",
            _ => "[SPACE] 또는 클릭으로 두드려라",
        };

        public override string Status
        {
            get
            {
                var text = $"{Mathf.RoundToInt(_gauge * 100f)}%";
                if (_rate > 0f) text += $"  ·  {_rate:0.0}회/초";
                if (_dropped > 0) text += $"  ·  놓침 {_dropped}";
                return text;
            }
        }

        protected override void Setup()
        {
            var taps = Mathf.Max(4, ParamInt("taps", 36));
            _gain = 1f / taps;
            // `stamina` 0.7 = 30% 더 빨리 샌다
            _drain = Param("drain", 0.024f) / Mathf.Max(0.3f, Param("stamina", 1f));
            _gauge = 0f;
            _dropped = 0;

            _phase = MashPhase.Pure;
            _prevDir = 0;
            // 시드 고정 — 같은 questId는 항상 같은 손(A부터 · D부터)에서 시작한다
            _expectedDir = Rng.Next(2) == 0 ? 1 : -1;
            _phaseAnnounceT = 0f;
            _phaseAnnounceText = "";
        }

        protected override void Advance(float dt, BoardInput input)
        {
            TickHeartbeat(dt);
            _phaseAnnounceT = Mathf.Max(0f, _phaseAnnounceT - dt);

            var phase = CurrentPhase();
            if (phase != _phase)
            {
                _phase = phase;
                AnnouncePhase();
            }

            _gauge = Mathf.Max(0f, _gauge - CurrentDrain() * dt);
            _kick = Mathf.Max(0f, _kick - dt * 4f);

            _rateWindow += dt;

            // A/D(또는 ←→)를 "누른 순간"만 센다 — 쥔 채로 있어도 다시 세지
            // 않는다. 같은 방향을 눌러 쥐고만 있으면 연타가 아니라 홀드가 되고,
            // 그러면 교대 구간의 의미가 없어진다
            var dirNow = input.Horizontal > 0.5f ? 1 : input.Horizontal < -0.5f ? -1 : 0;
            var pressedDir = dirNow != 0 && dirNow != _prevDir;
            _prevDir = dirNow;

            var hit = false;
            if (_phase == MashPhase.Pure)
            {
                hit = input.Tap;
            }
            else if (pressedDir && dirNow == _expectedDir)
            {
                // 교대 구간(스퍼트 포함) — 지금 요구하는 방향과 같을 때만 유효타다.
                // 반대 방향을 눌러도 벌점은 없다 — 조용히 씹히고 다음 정타를 기다린다
                hit = true;
                _expectedDir = -_expectedDir;
            }

            if (hit)
            {
                _gauge = Mathf.Min(1f, _gauge + _gain);
                _kick = 1f;
                _tapsInWindow += 1;
            }

            if (_rateWindow >= 0.5f)
            {
                _rate = _tapsInWindow / _rateWindow;
                _rateWindow = 0f;
                _tapsInWindow = 0;
            }

            // 0까지 떨어뜨리면 실은 것을 놓친 것이다. 두 번까지 실수로 센다
            if (_gauge <= 0f && Fill > 0.1f && _dropped < 2)
            {
                _dropped += 1;
                Miss();
            }

            Fill = _gauge;
            if (_gauge >= 1f) Clear();
        }

        /// <summary>게이지 값만으로 구간을 정한다 — 순서를 건너뛰지 못하고, 새서 되돌아가도 그대로 되돌아간다</summary>
        private MashPhase CurrentPhase()
        {
            if (_gauge >= SpurtStart) return MashPhase.Spurt;
            if (_gauge >= PureEnd) return MashPhase.Alternate;
            return MashPhase.Pure;
        }

        private float CurrentDrain() => _phase == MashPhase.Spurt ? _drain * SpurtDrainMultiplier : _drain;

        private void AnnouncePhase()
        {
            _phaseAnnounceT = AnnounceDuration;
            _phaseAnnounceText = _phase switch
            {
                MashPhase.Alternate => "A · D 교대 구간",
                MashPhase.Spurt => "막판 스퍼트 — 2배 구간!",
                _ => "",
            };
        }

        private Color GaugeColor()
        {
            if (_gauge >= 1f) return HudTheme.Accent;
            return _phase switch
            {
                MashPhase.Alternate => HudTheme.Cold,
                MashPhase.Spurt => HudTheme.Alert,
                _ => HudTheme.Heat,
            };
        }

        /// <summary>공통 — 마지막 3초 하이라이트. 심장 박동처럼 0.5초 간격으로 저음 tap을 운다</summary>
        private void TickHeartbeat(float dt)
        {
            if (Remaining > 3f || Remaining <= 0f)
            {
                _heartbeatCd = 0f;
                return;
            }

            _heartbeatCd -= dt;
            if (_heartbeatCd > 0f) return;
            Sfx.Play("tap", 0.85f, 0.55f);
            _heartbeatCd = HeartbeatInterval;
        }

        public override void Draw(HudTheme theme, Rect body)
        {
            theme.Fill(body, HudTheme.Paper3);

            // 막판 스퍼트는 배경만 봐도 다르게 느껴져야 한다 — 옅은 붉은 기를 전면에 깐다
            if (_phase == MashPhase.Spurt)
            {
                theme.Fill(body, HudTheme.Alert, 0.1f);
            }

            // 세로 게이지. 가로로 두면 시간 막대와 헷갈린다 — 이건 **쌓는** 것이다
            var column = new Rect(body.center.x - 78f, body.y + 32f, 156f, body.height - 96f);
            theme.Fill(column, HudTheme.Paper);

            var filled = column.height * _gauge;
            var bar = new Rect(column.x, column.yMax - filled, column.width, filled);
            theme.Fill(bar, GaugeColor());

            // 두드릴 때마다 윗면이 튄다 — 입력이 먹었다는 것이 몸으로 보여야 한다
            if (_kick > 0f && filled > 2f)
            {
                theme.Fill(new Rect(column.x, bar.y - 3f, column.width, 3f),
                           HudTheme.White, _kick);
            }

            theme.Border(column, HudTheme.Rule, 2f);

            // 눈금 — 얼마나 남았는지 읽는다
            for (var i = 1; i < 4; i += 1)
            {
                var y = column.yMax - column.height * (i * 0.25f);
                theme.Fill(new Rect(column.x, y, column.width, 1f), HudTheme.Rule3);
            }

            GUI.Label(new Rect(column.x - 120f, column.center.y - 14f, 110f, 28f),
                $"{Mathf.RoundToInt(_gauge * 100f)}%",
                theme.At(theme.MonoBig, 26, HudTheme.Ink, TextAnchor.MiddleRight));

            GUI.Label(new Rect(column.xMax + 14f, column.center.y - 14f, 200f, 28f),
                _phase == MashPhase.Spurt ? "쥐어짜야 버틴다" : _drain > 0.03f ? "빨리 샌다" : "샌다",
                theme.At(theme.Small, 14, HudTheme.Ink3));

            // A-5 벤치마크 이식 S4 — 다음에 눌러야 할 키를 크게 띄운다. 화면을
            // 보지 않고 손이 알아서 움직이면 곧 교대가 아니라 양손 연타가 되므로,
            // 매번 "다음은 이쪽"이 눈에 들어와야 한다
            if (_phase != MashPhase.Pure)
            {
                var keyLabel = _expectedDir > 0 ? "D" : "A";
                var keyColor = _phase == MashPhase.Spurt ? HudTheme.Alert : HudTheme.Cold;
                GUI.Label(new Rect(column.xMax + 14f, column.center.y + 18f, 160f, 64f), keyLabel,
                    theme.At(theme.Display, 48, keyColor, TextAnchor.MiddleLeft));
            }

            theme.Border(body, HudTheme.Rule);

            // 구간 전환 배너 — 명확한 문구 · 색. 짧게 떴다 사라진다
            if (_phaseAnnounceT > 0f && !string.IsNullOrEmpty(_phaseAnnounceText))
            {
                var col = _phase == MashPhase.Spurt ? HudTheme.Alert : HudTheme.Heat;
                var alpha = Mathf.Clamp01(_phaseAnnounceT / AnnounceDuration);
                GUI.Label(new Rect(body.x, body.yMax - 44f, body.width, 30f), _phaseAnnounceText,
                    theme.At(theme.Heading, 20, new Color(col.r, col.g, col.b, alpha), TextAnchor.MiddleCenter));
            }

            // 공통 — 마지막 3초 하이라이트. 본문 위 가장자리를 심장 박동에 맞춰 깜빡인다
            if (Remaining <= 3f && Remaining > 0f && HudTheme.Pulse(2f))
            {
                theme.Fill(new Rect(body.x, body.y, body.width, 4f), HudTheme.Alert, 0.85f);
            }
        }
    }
}
