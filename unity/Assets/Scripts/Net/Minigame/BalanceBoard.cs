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

        public override string Instruction => "A · D 로 수평을 잡고 끝까지 간다";

        public override string Status =>
            $"{_segment}/{_segments} 구간" + (Mistakes > 0 ? $"  ·  흘림 {Mistakes}" : "");

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
        }

        protected override void Advance(float dt, BoardInput input)
        {
            // 기울기에 비례해 가속한다 — 기울수록 걷잡을 수 없다
            _velocity += (_tilt * 1.9f + _bias) * _sway * Difficulty * 0.42f * dt;
            // 보정 입력. 반대로 밀면 속도가 줄고, 늦으면 이미 넘어간 뒤다
            _velocity -= input.Horizontal * 1.45f * dt;
            _velocity = Mathf.Clamp(_velocity, -1.6f, 1.6f);
            _tilt = Mathf.Clamp(_tilt + _velocity * dt, -1.2f, 1.2f);

            if (Mathf.Abs(_tilt) >= _limit)
            {
                // 쏟았다. 넘어지는 것이 아니라 **흘린 것**이다 — 다시 세우고 간다
                Miss();
                _tilt = 0f;
                _velocity = 0f;
                _bias = RandRange(-0.35f, 0.35f);
                // 흘리면 그 구간을 다시 걷는다
                _segment = Mathf.Max(0, _segment - 1);
            }

            // 수평에 가까울수록 빨리 간다. 기울어진 채로도 가긴 간다
            var pace = Mathf.Lerp(1f, 0.25f, Mathf.Abs(_tilt) / Mathf.Max(0.01f, _limit));
            Fill = Mathf.Clamp01(Fill + pace * dt / (Limit * 0.78f));
            _segment = Mathf.Min(_segments, Mathf.FloorToInt(Fill * _segments));

            if (Fill >= 1f) Clear();
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
            GUIUtility.RotateAroundPivot(_tilt * 34f, pivot);
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

            theme.Border(body, HudTheme.Rule);
        }
    }
}
