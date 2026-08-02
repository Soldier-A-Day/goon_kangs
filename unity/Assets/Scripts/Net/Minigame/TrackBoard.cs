using UnityEngine;

namespace SoldierADay.Net
{
    /// <summary>
    /// `TRACK` 조준 유지 — 2건. 사주경계 · 경계근무.
    ///
    /// 움직이는 표적 위에 커서를 얹고 유지한다. 조작은 커서 하나뿐이다.
    ///
    /// 둘 다 가장 긴 일과(22 · 25초)라 **버티는 판**이다. 경계근무는 여기에
    /// 졸음이 겹친다 — 표적이 잠깐씩 흐려지고, 그 동안에도 유지율은 흐른다.
    ///
    /// 유지율이 `holdRatio`에 닿으면 통과다. 놓쳐도 줄기만 하고 끝나지 않는다 —
    /// 25초를 한 번도 안 놓치라고 하면 그건 유지가 아니라 무결이다.
    /// </summary>
    public sealed class TrackBoard : Board
    {
        private Vector2 _target;
        private Vector2 _to;
        private float _hop;
        private float _speed;
        private float _held;
        private float _need;
        private bool _on;
        private int _lost;
        private float _drowsy;
        private bool _nightGuard;
        private Rect _area;

        private const float Radius = 46f;

        public override string Instruction => "표적 위에 커서를 유지하라";

        public override string Status
        {
            get
            {
                var pct = Mathf.RoundToInt(Fill * 100f);
                var text = $"유지율 {pct}%  ·  목표 {Mathf.RoundToInt(_need * 100f)}%";
                if (_lost > 0) text += $"  ·  놓침 {_lost}";
                return text;
            }
        }

        protected override void Setup()
        {
            _speed = Param("speed", 0.55f);
            _need = Param("holdRatio", 0.7f);
            _held = 0f;
            _lost = 0;
            _target = new Vector2(0.5f, 0.5f);
            _to = _target;
            // 경계근무만 졸음이 겹친다 — 가장 긴 일과다
            _nightGuard = Spec?.variant == "post";
        }

        protected override void Advance(float dt, BoardInput input)
        {
            // 표적은 목표점으로 미끄러지다가 이따금 새 목표를 잡는다.
            // 등속 직선으로 두면 예측이 너무 쉽고, 순간이동하면 따라갈 수 없다
            _hop -= dt;
            if (_hop <= 0f)
            {
                _hop = RandRange(0.9f, 1.8f) / Mathf.Max(0.2f, _speed);
                _to = new Vector2(RandRange(0.12f, 0.88f), RandRange(0.14f, 0.86f));
            }
            _target = Vector2.Lerp(_target, _to, Mathf.Clamp01(_speed * 1.6f * dt));

            if (_nightGuard)
            {
                // 졸음은 주기적으로 온다. 그 동안 표적이 흐려지고 반경이 좁아진다
                _drowsy = Mathf.Clamp01(Mathf.Sin(Elapsed * 0.55f) * 0.5f + 0.5f);
            }

            if (_area.width <= 0f) return;

            var center = new Vector2(_area.x + _target.x * _area.width,
                                     _area.y + _target.y * _area.height);
            var radius = Radius * Mathf.Lerp(1f, 0.62f, (Difficulty - 1f) * 0.5f)
                                * Mathf.Lerp(1f, 0.7f, _drowsy);

            var wasOn = _on;
            _on = Vector2.Distance(input.Mouse, center) <= radius;
            if (wasOn && !_on) _lost += 1;

            // 유지율은 **비율**이다. 붙어 있으면 오르고 떨어지면 준다
            var slice = dt / (Limit * 0.85f);
            _held = Mathf.Clamp01(_held + (_on ? slice : -slice * 0.75f));
            Fill = _held;

            // 놓친 횟수가 등급을 가른다. 두 번까지만 센다
            var expected = _lost >= 6 ? 2 : _lost >= 3 ? 1 : 0;
            while (Mistakes < expected && Mistakes < 2) Miss();

            if (_held >= _need) Clear();
        }

        public override void Draw(HudTheme theme, Rect body)
        {
            _area = body;
            theme.Fill(body, HudTheme.Paper3);

            // 섹터 눈금 — 어디를 보고 있는지
            for (var i = 1; i < 4; i += 1)
            {
                theme.Fill(new Rect(body.x + body.width * i / 4f, body.y, 1f, body.height),
                           HudTheme.Rule3);
            }

            var center = new Vector2(body.x + _target.x * body.width,
                                     body.y + _target.y * body.height);
            var radius = Radius * Mathf.Lerp(1f, 0.62f, (Difficulty - 1f) * 0.5f)
                                * Mathf.Lerp(1f, 0.7f, _drowsy);

            var box = new Rect(center.x - radius, center.y - radius, radius * 2f, radius * 2f);
            theme.Fill(box, _on ? HudTheme.AccentW : HudTheme.Paper2, _nightGuard ? 1f - _drowsy * 0.5f : 1f);
            theme.Border(box, _on ? HudTheme.Accent : HudTheme.Heat, 2f);
            // 가운데 점 — 표적이 어디인지
            theme.Fill(new Rect(center.x - 4f, center.y - 4f, 8f, 8f),
                       _on ? HudTheme.Accent : HudTheme.Heat);

            // 커서
            var mouse = BoardInput.Read().Mouse;
            if (body.Contains(mouse))
            {
                theme.Fill(new Rect(mouse.x - 12f, mouse.y - 1f, 24f, 2f), HudTheme.Ink);
                theme.Fill(new Rect(mouse.x - 1f, mouse.y - 12f, 2f, 24f), HudTheme.Ink);
            }

            // 유지율 — 목표선을 같이 긋는다. 어디까지 채워야 하는지가 보여야 한다
            var bar = new Rect(body.x + 40f, body.yMax - 40f, body.width - 80f, 18f);
            theme.Bar(bar, Fill, _on ? HudTheme.Accent : HudTheme.Heat);
            theme.Fill(new Rect(bar.x + bar.width * _need - 1f, bar.y - 5f, 2f, bar.height + 10f),
                       HudTheme.Ink);

            if (_nightGuard && _drowsy > 0.55f)
            {
                GUI.Label(new Rect(body.x, body.y + 16f, body.width, 26f), "졸음이 온다",
                    theme.At(theme.Small, 14, HudTheme.Heat, TextAnchor.MiddleCenter));
            }

            theme.Border(body, HudTheme.Rule);
        }
    }
}
