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
    /// </summary>
    public sealed class MashBoard : Board
    {
        private float _gauge;
        private float _gain;
        private float _drain;
        private int _dropped;
        private float _kick;
        private float _rate;
        private float _rateWindow;
        private int _tapsInWindow;

        public override string Instruction => "[SPACE] 또는 클릭으로 두드려라";

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
        }

        protected override void Advance(float dt, BoardInput input)
        {
            _gauge = Mathf.Max(0f, _gauge - _drain * dt);
            _kick = Mathf.Max(0f, _kick - dt * 4f);

            _rateWindow += dt;
            if (input.Tap)
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

        public override void Draw(HudTheme theme, Rect body)
        {
            theme.Fill(body, HudTheme.Paper3);

            // 세로 게이지. 가로로 두면 시간 막대와 헷갈린다 — 이건 **쌓는** 것이다
            var column = new Rect(body.center.x - 78f, body.y + 32f, 156f, body.height - 96f);
            theme.Fill(column, HudTheme.Paper);

            var filled = column.height * _gauge;
            var bar = new Rect(column.x, column.yMax - filled, column.width, filled);
            theme.Fill(bar, _gauge >= 1f ? HudTheme.Accent : HudTheme.Heat);

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
                _drain > 0.03f ? "빨리 샌다" : "샌다",
                theme.At(theme.Small, 14, HudTheme.Ink3));

            theme.Border(body, HudTheme.Rule);
        }
    }
}
