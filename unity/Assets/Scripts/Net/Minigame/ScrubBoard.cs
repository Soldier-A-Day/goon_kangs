using UnityEngine;

namespace SoldierADay.Net
{
    /// <summary>
    /// `SCRUB` 문지르기 — 8건.
    ///
    /// 오염을 브러시로 덮어 커버리지를 채운다. 드래그 하나뿐이다.
    ///
    /// **같은 원형이 여덟 번 나오는 것을 감추지 않는다**(설계안 §전제 4). 대신
    /// 파라미터가 다른 일로 읽히게 만든다 — 침상 밑 먼지는 브러시가 좁고(22),
    /// 차량 세척은 면적이 넓으며(40), 방역 소독과 모래 살포는 **약제 잔량**이
    /// 있어서 중복해서 뿌리면 모자란다(`supply`).
    ///
    /// 등급의 실수 축이 곧 낭비다. 이미 깨끗한 자리를 계속 문지르면 시간도
    /// 약제도 같이 나간다.
    /// </summary>
    public sealed class ScrubBoard : Board
    {
        private const float Cell = 20f;

        private int _cols;
        private int _rows;
        private bool[] _dirty;
        private bool[] _cleaned;
        private int _total;
        private int _done;

        /// <summary>약제 잔량. 0보다 크면 문지를 수 있는 거리가 제한된다</summary>
        private float _supply;
        private float _supplyMax;
        private bool _dry;

        private float _waste;
        private float _strokes;
        private Vector2 _last;
        private bool _dragging;
        private Rect _area;

        public override string Instruction =>
            _supplyMax > 0f ? "끌어서 문질러라 — 약제가 모자란다" : "끌어서 문질러라";

        public override string Status
        {
            get
            {
                var left = Mathf.Max(0, _total - _done);
                if (_supplyMax > 0f)
                {
                    return $"남은 오염 {left}칸  ·  약제 {Mathf.RoundToInt(_supply / _supplyMax * 100f)}%";
                }
                return $"남은 오염 {left}칸";
            }
        }

        protected override void Setup()
        {
            // 판 크기는 `Draw`가 처음 도는 순간에야 알 수 있다. 격자는 고정
            // 해상도로 잡아두고 그릴 때 늘린다 — 그래야 같은 일과가 창 크기와
            // 무관하게 같은 오염 배치를 낸다
            _cols = 34;
            _rows = 18;
            _dirty = new bool[_cols * _rows];
            _cleaned = new bool[_cols * _rows];

            var stains = ParamInt("stains", 6);
            var radius = Mathf.Lerp(3.2f, 2.2f, (Difficulty - 1f) * 0.5f);

            for (var i = 0; i < stains; i += 1)
            {
                var cx = RandRange(radius, _cols - radius);
                var cy = RandRange(radius, _rows - radius);
                // 덩어리마다 크기가 다르다. 전부 같은 원이면 여섯 개가 아니라
                // 한 개를 여섯 번 그린 것으로 보인다
                var r = radius * RandRange(0.7f, 1.35f);

                for (var y = 0; y < _rows; y += 1)
                for (var x = 0; x < _cols; x += 1)
                {
                    var dx = x + 0.5f - cx;
                    var dy = y + 0.5f - cy;
                    if (dx * dx + dy * dy <= r * r) _dirty[y * _cols + x] = true;
                }
            }

            _total = 0;
            foreach (var d in _dirty) if (d) _total += 1;
            _done = 0;

            var supply = Param("supply", 0f);
            if (supply > 0f)
            {
                // 잔량은 **오염을 정확히 덮는 데 필요한 거리**의 배수다.
                // 1.35면 35%까지 헛손질을 봐준다
                _supplyMax = _total * Cell * 0.55f * supply;
                _supply = _supplyMax;
            }
        }

        protected override void Advance(float dt, BoardInput input)
        {
            if (_area.width <= 0f) return;

            if (!input.Down)
            {
                _dragging = false;
                return;
            }

            if (!_area.Contains(input.Mouse))
            {
                _dragging = false;
                return;
            }

            var moved = _dragging ? Vector2.Distance(input.Mouse, _last) : 0f;
            _last = input.Mouse;
            _dragging = true;

            if (_dry) return;
            if (_supplyMax > 0f)
            {
                _supply -= moved;
                if (_supply <= 0f)
                {
                    _supply = 0f;
                    // 약제가 떨어지면 더 못 지운다. 남은 시간은 그대로 흐르고,
                    // 커버리지를 못 채우면 재시도다 — 낭비의 대가가 여기서 온다
                    _dry = true;
                    Miss();
                    return;
                }
            }

            var brush = Param("brush", 28f) * 0.5f;
            var scaleX = _area.width / _cols;
            var scaleY = _area.height / _rows;

            var minX = Mathf.Max(0, Mathf.FloorToInt((input.Mouse.x - brush - _area.x) / scaleX));
            var maxX = Mathf.Min(_cols - 1, Mathf.CeilToInt((input.Mouse.x + brush - _area.x) / scaleX));
            var minY = Mathf.Max(0, Mathf.FloorToInt((input.Mouse.y - brush - _area.y) / scaleY));
            var maxY = Mathf.Min(_rows - 1, Mathf.CeilToInt((input.Mouse.y + brush - _area.y) / scaleY));

            var touched = 0;
            var useful = 0;

            for (var y = minY; y <= maxY; y += 1)
            for (var x = minX; x <= maxX; x += 1)
            {
                var cx = _area.x + (x + 0.5f) * scaleX;
                var cy = _area.y + (y + 0.5f) * scaleY;
                var dx = cx - input.Mouse.x;
                var dy = cy - input.Mouse.y;
                if (dx * dx + dy * dy > brush * brush) continue;

                touched += 1;
                var i = y * _cols + x;
                if (!_dirty[i] || _cleaned[i]) continue;

                _cleaned[i] = true;
                _done += 1;
                useful += 1;
            }

            if (touched > 0)
            {
                _strokes += touched;
                _waste += touched - useful;
            }

            Fill = _total == 0 ? 1f : (float)_done / _total;

            // 낭비가 쌓이면 실수로 센다. 두 번까지만 — 그 아래는 C를 넘지 않는다
            var wasteRatio = _strokes <= 0f ? 0f : _waste / _strokes;
            var expected = wasteRatio > 0.75f ? 2 : wasteRatio > 0.55f ? 1 : 0;
            while (Mistakes < expected && Mistakes < 2) Miss();

            if (Fill >= Param("coverage", 0.9f)) Clear();
        }

        public override void Draw(HudTheme theme, Rect body)
        {
            _area = body;

            // 바탕 — 닦아야 할 면
            theme.Fill(body, HudTheme.Paper2);

            var scaleX = body.width / _cols;
            var scaleY = body.height / _rows;

            for (var y = 0; y < _rows; y += 1)
            for (var x = 0; x < _cols; x += 1)
            {
                var i = y * _cols + x;
                if (!_dirty[i] || _cleaned[i]) continue;
                theme.Fill(new Rect(body.x + x * scaleX, body.y + y * scaleY, scaleX + 1f, scaleY + 1f),
                           HudTheme.Heat, 0.72f);
            }

            theme.Border(body, HudTheme.Rule);

            // 브러시 — 어디를 닦고 있는지 손이 보여야 한다
            var mouse = BoardInput.Read().Mouse;
            if (body.Contains(mouse))
            {
                var r = Param("brush", 28f) * 0.5f;
                var ring = new Rect(mouse.x - r, mouse.y - r, r * 2f, r * 2f);
                theme.Border(ring, _dry ? HudTheme.Alert : HudTheme.Accent, 2f);
            }

            if (_dry)
            {
                GUI.Label(body, "약제가 떨어졌다",
                    theme.At(theme.Heading, 22, HudTheme.Alert, TextAnchor.MiddleCenter));
            }
        }
    }
}
