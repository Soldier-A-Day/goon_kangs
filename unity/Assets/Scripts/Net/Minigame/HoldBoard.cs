using UnityEngine;

namespace SoldierADay.Net
{
    /// <summary>
    /// `HOLD` 계기 유지 — 5건. 급수통 · 졸음 · 발전기 급유 ×2 · 난방기 압력.
    ///
    /// 바늘이 계속 흐르고, 밸브로 안전 범위에 붙잡아 둔다. 조작은 홀드 하나뿐이다.
    ///
    /// **`BALANCE`와 다른 점은 목표가 가운데가 아니라는 것이다.** 급수통은
    /// 넘치기 직전(0.72~0.9)이 안전 범위라 위쪽에 붙어 있고, 난방기는 아래쪽
    /// (0.45~0.7)이다. 범위를 눈에 보이게 칠하지 않으면 플레이어는 늘 가운데를
    /// 맞추려 든다 — 사이드뷰 페이스 게이지에서 배운 것과 같다.
    ///
    /// 이탈은 즉시 실패가 아니라 **누적**이다(`slack`, 초). 계기는 잠깐 벗어나도
    /// 되돌리면 되고, 그 여유가 없으면 손이 굳는 순간 끝난다.
    /// </summary>
    public sealed class HoldBoard : Board
    {
        private float _needle;
        private float _min;
        private float _max;
        private float _drift;
        private float _slack;
        private float _leaked;
        private float _held;
        private float _need;
        private bool _phase2;

        public override string Instruction => "누르고 있으면 오르고, 놓으면 내려간다";

        public override string Status
        {
            get
            {
                var text = $"유지 {_held:0.0}/{_need:0.0}초";
                if (_leaked > 0f) text += $"  ·  이탈 {_leaked:0.0}/{_slack:0.0}초";
                if (_phase2) text += "  ·  범위 축소";
                return text;
            }
        }

        protected override void Setup()
        {
            _min = Param("bandMin", 0.5f);
            _max = Param("bandMax", 0.8f);
            _drift = Param("drift", 0.32f);
            _slack = Param("slack", 1.5f);
            _needle = 0f;
            _leaked = 0f;
            _held = 0f;
            _phase2 = false;
            // 제한 시간의 60%를 범위 안에서 버텨야 한다
            _need = Limit * 0.6f;
        }

        protected override void Advance(float dt, BoardInput input)
        {
            // 놓으면 흐르고, 누르면 오른다. 누르는 힘이 흐름보다 조금 세다
            _needle += (input.Hold ? _drift * 1.8f : -_drift) * dt;
            _needle = Mathf.Clamp01(_needle);

            var inBand = _needle >= _min && _needle <= _max;
            if (inBand) _held += dt;
            else
            {
                _leaked += dt;
                if (_leaked >= _slack) Fail();
            }

            Fill = Mathf.Clamp01(_held / _need);

            // 2페이즈 — 절반을 버티면 범위가 좁아진다 (난방기 점검)
            if (!_phase2 && Fill >= 0.5f && Spec?.phase2 != null)
            {
                _phase2 = true;
                var center = (_min + _max) * 0.5f;
                var half = (_max - _min) * 0.32f;
                _min = center - half;
                _max = center + half;
                // 좁아진 범위에서 처음부터 다시 세는 것이 아니라 이어서 센다
                Miss();
            }

            if (Fill >= 1f) Clear();
        }

        public override void Draw(HudTheme theme, Rect body)
        {
            theme.Fill(body, HudTheme.Paper3);

            // 세로 계기. 급수통도 압력계도 세로로 읽는 물건이다
            var gauge = new Rect(body.center.x - 46f, body.y + 24f, 92f, body.height - 96f);
            theme.Fill(gauge, HudTheme.Paper);

            // 안전 범위 — 가운데가 아니라는 것이 먼저 보여야 한다
            var band = new Rect(gauge.x, gauge.yMax - gauge.height * _max,
                                gauge.width, gauge.height * (_max - _min));
            theme.Fill(band, HudTheme.AccentW);
            theme.Border(band, HudTheme.Accent);

            var inBand = _needle >= _min && _needle <= _max;
            var color = inBand ? HudTheme.Accent
                      : _leaked > _slack * 0.6f ? HudTheme.Alert
                      : HudTheme.Heat;

            // 바늘
            var y = gauge.yMax - gauge.height * _needle;
            theme.Fill(new Rect(gauge.x - 18f, y - 3f, gauge.width + 36f, 6f), color);

            theme.Border(gauge, HudTheme.Rule, 2f);

            GUI.Label(new Rect(gauge.xMax + 24f, y - 14f, 120f, 28f),
                $"{Mathf.RoundToInt(_needle * 100f)}",
                theme.At(theme.MonoBig, 24, color));

            // 유지 시간 — 이 판이 무엇을 세는지 말한다
            var bar = new Rect(body.x + 40f, body.yMax - 46f, body.width - 80f, 18f);
            theme.Bar(bar, Fill, HudTheme.Accent);
            GUI.Label(new Rect(bar.x, bar.y - 24f, bar.width, 20f), "안전 범위 유지",
                theme.At(theme.Label, 12, HudTheme.Ink2));

            // 이탈 여유 — 얼마나 벗어나 있어도 되는가
            if (_leaked > 0f)
            {
                var leak = new Rect(bar.x, bar.yMax + 6f, bar.width, 5f);
                theme.Fill(leak, HudTheme.Rule3);
                theme.Fill(new Rect(leak.x, leak.y, leak.width * Mathf.Clamp01(_leaked / _slack),
                                    leak.height), HudTheme.Alert);
            }

            theme.Border(body, HudTheme.Rule);
        }
    }
}
