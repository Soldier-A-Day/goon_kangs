using UnityEngine;

namespace SoldierADay.Net
{
    /// <summary>
    /// `BALANCE` 균형 운반 — 5건. 잔반통 · 탄통 · 적재 스택 · 유류통 · 들것.
    ///
    /// 짐이 한쪽으로 기울고, 좌우로 보정하며 구간을 완주한다. 조작은 좌우 하나뿐이다.
    ///
    /// **기울기가 커질수록 더 빨리 기운다.** 선형으로 두면 한 번 잡아두고 가만히
    /// 있으면 되는데, 그러면 운반이 아니라 정지다. 기울기에 비례해 가속하면
    /// 계속 되잡아야 하고, 그게 무거운 것을 드는 느낌이 된다.
    ///
    ///   `sway`       기우는 세기. 적재 스택(1.2)이 잔반통(1.0)보다 무겁다
    ///   `tolerance`  넘어지는 각. 유류통(0.16)은 좁고 잔반통(0.3)은 넉넉하다
    ///   `segments`   구간 수. 흘린 것은 구간마다 기록된다
    /// </summary>
    public sealed class BalanceBoard : Board
    {
        /// <summary>−1 ~ 1. 0이 수평</summary>
        private float _tilt;
        private float _velocity;
        private float _limit;
        private float _sway;
        private int _segment;
        private int _segments;
        private float _bias;

        /// <summary>
        /// 위험 구역에서 즉시 흘리지 않고 봐주는 유예 틱 수(A-5 벤치마크
        /// 이식 S5). 한 프레임 스친 것까지 실수로 세면 억울하다 — 3틱
        /// 동안 되돌리지 못했을 때만 진짜로 쏟은 것으로 친다.
        /// </summary>
        private const int OverLimitGrace = 3;
        private int _overLimitTicks;

        /* ──────────────────────────────────────────── 돌풍 예고(A-5 S5) */

        /// <summary>다음 돌풍이 불기까지 남은 시간(초) — 0이 되면 분다</summary>
        private float _gustTimer;
        /// <summary>이번 주기의 예고를 이미 냈는가 — 판당 한 번씩 소리·화살표를 낸다</summary>
        private bool _gustWarned;
        /// <summary>예고된 돌풍의 방향(-1·1)</summary>
        private int _gustDir;

        private const float GustForecastLead = 0.5f;
        private const float GustMinInterval = 2.2f;
        private const float GustMaxInterval = 3.8f;
        private const float GustStrength = 0.7f;

        /// <summary>공통 — 마지막 3초 심장 박동까지 남은 시간(초)</summary>
        private float _heartbeatCd;
        private const float HeartbeatInterval = 0.5f;

        public override string Instruction => "A · D 로 수평을 잡고 끝까지 간다";

        public override string Status =>
            $"{_segment}/{_segments} 구간" + (Mistakes > 0 ? $"  ·  흘림 {Mistakes}" : "")
            + (_gustWarned ? "  ·  돌풍이 온다" : "");

        protected override void Setup()
        {
            _segments = Mathf.Clamp(ParamInt("segments", 4), 2, 8);
            _sway = Param("sway", 1f);
            _limit = Param("tolerance", 0.25f) * 4f;
            _tilt = 0f;
            _velocity = 0f;
            _segment = 0;
            // 짐은 처음부터 한쪽으로 쏠려 있다. 어느 쪽인지는 일과마다 다르다
            _bias = RandRange(-0.35f, 0.35f);

            _overLimitTicks = 0;
            _gustTimer = RandRange(GustMinInterval, GustMaxInterval);
            _gustWarned = false;
            _gustDir = 0;
        }

        protected override void Advance(float dt, BoardInput input)
        {
            TickHeartbeat(dt);
            TickGust(dt);

            // 기울기에 비례해 가속한다 — 기울수록 걷잡을 수 없다
            _velocity += (_tilt * 1.9f + _bias) * _sway * Difficulty * 0.42f * dt;
            // 보정 입력. 반대로 밀면 속도가 줄고, 늦으면 이미 넘어간 뒤다
            _velocity -= input.Horizontal * 1.45f * dt;
            _velocity = Mathf.Clamp(_velocity, -1.6f, 1.6f);
            _tilt = Mathf.Clamp(_tilt + _velocity * dt, -1.2f, 1.2f);

            if (Mathf.Abs(_tilt) >= _limit)
            {
                // A-5 벤치마크 이식 S5 — 3틱 유예. 위험 구역에 들어간 순간 바로
                // 쏟은 것으로 치지 않는다 — 3틱 안에 되잡으면 아무 일도 없던
                // 것으로 넘어간다. 그 안에도 못 되돌리면 그제서야 흘린 것이다
                _overLimitTicks += 1;
                if (_overLimitTicks >= OverLimitGrace)
                {
                    // 쏟았다. 넘어지는 것이 아니라 **흘린 것**이다 — 다시 세우고 간다
                    Miss();
                    _tilt = 0f;
                    _velocity = 0f;
                    _bias = RandRange(-0.35f, 0.35f);
                    // 흘리면 그 구간을 다시 걷는다
                    _segment = Mathf.Max(0, _segment - 1);
                    _overLimitTicks = 0;
                }
            }
            else
            {
                _overLimitTicks = 0;
            }

            // 수평에 가까울수록 빨리 간다. 기울어진 채로도 가긴 간다
            var pace = Mathf.Lerp(1f, 0.25f, Mathf.Abs(_tilt) / Mathf.Max(0.01f, _limit));
            Fill = Mathf.Clamp01(Fill + pace * dt / (Limit * 0.78f));
            _segment = Mathf.Min(_segments, Mathf.FloorToInt(Fill * _segments));

            if (Fill >= 1f) Clear();
        }

        /// <summary>
        /// A-5 벤치마크 이식 S5 — 돌풍 예고.
        ///
        /// 몰래 밀면 배신이 아니라 억울함이다(`ScrubBoard`의 김 서림 트위스트와
        /// 같은 철칙). 그래서 실제로 밀기 `GustForecastLead`초 전에 방향과
        /// 소리로 미리 알리고, 그 뒤에야 속도에 충격을 얹는다. 주기는 시드
        /// 고정 `Rng`라 같은 일과는 재시도해도 같은 타이밍에 같은 방향으로 분다.
        /// </summary>
        private void TickGust(float dt)
        {
            _gustTimer -= dt;

            if (!_gustWarned && _gustTimer <= GustForecastLead)
            {
                _gustWarned = true;
                _gustDir = Rng.Next(2) == 0 ? 1 : -1;
                // 바람 소리 — 저피치 tap으로 대신한다. "쿵" 대신 "휘이" 쪽 느낌을 준다
                Sfx.Play("tap", 0.75f, 0.5f);
            }

            if (_gustTimer > 0f) return;

            if (_gustWarned) _velocity = Mathf.Clamp(_velocity + _gustDir * GustStrength, -1.6f, 1.6f);
            _gustTimer = RandRange(GustMinInterval, GustMaxInterval);
            _gustWarned = false;
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

            // 구간 — 얼마나 남았는지
            var trackY = body.y + 28f;
            for (var i = 0; i < _segments; i += 1)
            {
                var w = (body.width - 40f) / _segments;
                var cell = new Rect(body.x + 20f + i * w, trackY, w - 6f, 14f);
                theme.Fill(cell, i < _segment ? HudTheme.Accent : HudTheme.Paper);
                theme.Border(cell, HudTheme.Rule3);
            }

            // 짐 — 기울어진 막대. 각이 몸으로 읽혀야 한다
            var pivot = new Vector2(body.center.x, body.center.y + 40f);
            var beam = new Rect(pivot.x - 190f, pivot.y - 11f, 380f, 22f);
            var over = Mathf.Abs(_tilt) / Mathf.Max(0.01f, _limit);
            var color = over > 0.75f ? HudTheme.Alert : over > 0.45f ? HudTheme.Heat : HudTheme.Accent;

            var matrix = GUI.matrix;
            HudTheme.RotateAt(_tilt * 34f, pivot);
            theme.Fill(beam, HudTheme.Paper2);
            theme.Border(beam, color, 2f);
            // 짐덩이 — 막대 위에 얹혀 있다
            theme.Fill(new Rect(pivot.x - 52f, beam.y - 44f, 104f, 44f), HudTheme.Paper2);
            theme.Border(new Rect(pivot.x - 52f, beam.y - 44f, 104f, 44f), color, 2f);
            GUI.matrix = matrix;

            // 받침
            theme.Fill(new Rect(pivot.x - 10f, pivot.y + 12f, 20f, 46f), HudTheme.Rule2);

            // 수평계 — 지금 얼마나 기울었는가
            var meter = new Rect(body.center.x - 200f, body.yMax - 46f, 400f, 16f);
            theme.Fill(meter, HudTheme.Rule3);
            var safe = new Rect(meter.center.x - meter.width * 0.5f * (_limit / 1.2f), meter.y,
                                meter.width * (_limit / 1.2f), meter.height);
            theme.Fill(safe, HudTheme.AccentW);
            theme.Fill(new Rect(meter.center.x + _tilt / 1.2f * meter.width * 0.5f - 3f,
                                meter.y - 5f, 6f, meter.height + 10f), color);
            theme.Border(meter, HudTheme.Rule);

            // A-5 벤치마크 이식 S5 — 위험 구역 단계적 경보. 판 테두리 자체가
            // 2단계로 올라간다 — `over`(수평계·짐과 같은 잣대)가 45%를 넘으면
            // 굵어지고, 75%를 넘으면 색이 경고로 바뀌며 깜빡인다. 짐 색과
            // 이중으로 알려야 시선이 짐에 없어도(수첩을 보는 순간에도) 위험을 안다
            var borderColor = over > 0.75f ? HudTheme.Alert : over > 0.45f ? HudTheme.Heat : HudTheme.Rule;
            var borderWeight = over > 0.45f ? 3f : 1f;
            if (over > 0.75f && !HudTheme.Pulse(4f)) borderColor = HudTheme.Rule;
            theme.Border(body, borderColor, borderWeight);

            // 돌풍 예고 — 실제로 밀기 전에 방향과 함께 보인다. 몰래 밀면
            // 배신이 아니라 억울함이다(`ScrubBoard`의 김 서림 트위스트와 같은 철칙)
            if (_gustWarned)
            {
                var arrow = _gustDir > 0 ? "→ 돌풍 →" : "← 돌풍 ←";
                var alpha = HudTheme.Pulse(6f) ? 1f : 0.45f;
                GUI.Label(new Rect(body.x, body.y + 6f, body.width, 34f), arrow,
                    theme.At(theme.Heading, 24, new Color(HudTheme.Cold.r, HudTheme.Cold.g, HudTheme.Cold.b, alpha),
                        TextAnchor.MiddleCenter));
            }

            // 공통 — 마지막 3초 하이라이트. 본문 위 가장자리를 심장 박동에 맞춰 깜빡인다
            if (Remaining <= 3f && Remaining > 0f && HudTheme.Pulse(2f))
            {
                theme.Fill(new Rect(body.x, body.y, body.width, 4f), HudTheme.Alert, 0.85f);
            }
        }
    }
}
