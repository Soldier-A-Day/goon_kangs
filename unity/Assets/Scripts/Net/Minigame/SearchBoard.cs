using UnityEngine;

namespace SoldierADay.Net
{
    /// <summary>
    /// `SEARCH` 탐색 — 4건. 환자 발생 점검 · 보일러실 순찰 · 울타리 순찰 · 분실 장비 수색.
    ///
    /// 화면을 훑다가 신호가 세지는 곳을 찍는다. 조작은 커서와 클릭 하나뿐이다.
    ///
    /// **찾기 놀이가 되면 안 된다.** 아무 단서 없이 넓은 화면을 훑게 하면 그건
    /// 운이고, 20초 안에 되는 일도 아니다. 그래서 `signal` 반경 안에서는 신호가
    /// 세지고, 커서 주변만 밝아진다 — 어디를 이미 봤는지가 화면에 남으므로
    /// 훑는 순서가 실력이 된다.
    ///
    /// 헛짚어도 실패는 아니다. 실수로만 세고, 못 찾은 채 시간이 다하면 재시도다.
    /// </summary>
    public sealed class SearchBoard : Board
    {
        private Vector2[] _hidden;
        private bool[] _found;
        private bool[] _swept;
        private int _cols;
        private int _rows;
        private int _count;
        private float _signal;
        private float _strength;
        private float _ping;
        private Rect _area;

        public override string Instruction => "훑다가 신호가 세지면 눌러라";

        public override string Status
        {
            get
            {
                var swept = 0;
                if (_swept != null) foreach (var s in _swept) if (s) swept += 1;
                var pct = _swept == null || _swept.Length == 0 ? 0 : swept * 100 / _swept.Length;
                return $"발견 {_count}/{_hidden?.Length ?? 0}  ·  수색 {pct}%";
            }
        }

        protected override void Setup()
        {
            var count = Mathf.Clamp(ParamInt("hidden", 3), 1, 6);
            _hidden = new Vector2[count];
            _found = new bool[count];
            _count = 0;
            _signal = Param("signal", 0.2f);

            var cells = Mathf.Clamp(ParamInt("cells", 24), 12, 48);
            _cols = Mathf.Clamp(Mathf.RoundToInt(Mathf.Sqrt(cells * 2f)), 4, 12);
            _rows = Mathf.Max(3, Mathf.CeilToInt((float)cells / _cols));
            _swept = new bool[_cols * _rows];

            for (var i = 0; i < count; i += 1)
            {
                // 가장자리에 붙이지 않는다 — 구석부터 찍는 것이 최적해가 되면 안 된다
                _hidden[i] = new Vector2(RandRange(0.12f, 0.88f), RandRange(0.14f, 0.86f));
            }
        }

        protected override void Advance(float dt, BoardInput input)
        {
            _ping = Mathf.Max(0f, _ping - dt * 2f);
            if (_area.width <= 0f) return;
            if (!_area.Contains(input.Mouse)) { _strength = 0f; return; }

            var norm = new Vector2((input.Mouse.x - _area.x) / _area.width,
                                   (input.Mouse.y - _area.y) / _area.height);

            // 훑은 자리는 남는다. 어디를 이미 봤는지가 보여야 순서가 실력이 된다
            var cx = Mathf.Clamp(Mathf.FloorToInt(norm.x * _cols), 0, _cols - 1);
            var cy = Mathf.Clamp(Mathf.FloorToInt(norm.y * _rows), 0, _rows - 1);
            _swept[cy * _cols + cx] = true;

            // 가장 가까운 미발견 대상까지의 거리 → 신호 세기
            _strength = 0f;
            var nearest = -1;
            for (var i = 0; i < _hidden.Length; i += 1)
            {
                if (_found[i]) continue;
                var d = Vector2.Distance(norm, _hidden[i]);
                var s = 1f - Mathf.Clamp01(d / _signal);
                if (s <= _strength) continue;
                _strength = s;
                nearest = i;
            }

            if (!input.Pressed) return;

            // 신호가 충분히 센 자리에서만 잡힌다. 난이도가 오르면 더 가까이 가야 한다
            var need = Mathf.Lerp(0.45f, 0.66f, (Difficulty - 1f) * 0.5f);
            if (nearest >= 0 && _strength >= need)
            {
                _found[nearest] = true;
                _count += 1;
                _ping = 1f;
                Fill = (float)_count / _hidden.Length;
                if (_count >= _hidden.Length) Clear();
                return;
            }

            Miss();
        }

        public override void Draw(HudTheme theme, Rect body)
        {
            _area = body;
            if (_hidden == null) return;

            theme.Fill(body, HudTheme.Rule3);

            // 훑지 않은 칸은 덮여 있다 — 수색은 덮인 것을 걷어내는 일이다
            var cw = body.width / _cols;
            var ch = body.height / _rows;
            for (var y = 0; y < _rows; y += 1)
            for (var x = 0; x < _cols; x += 1)
            {
                if (_swept[y * _cols + x]) continue;
                theme.Fill(new Rect(body.x + x * cw, body.y + y * ch, cw + 1f, ch + 1f),
                           HudTheme.Paper, 0.92f);
            }

            // 찾은 것
            for (var i = 0; i < _hidden.Length; i += 1)
            {
                if (!_found[i]) continue;
                var p = new Vector2(body.x + _hidden[i].x * body.width,
                                    body.y + _hidden[i].y * body.height);
                var mark = new Rect(p.x - 15f, p.y - 15f, 30f, 30f);
                theme.Fill(mark, HudTheme.AccentW);
                theme.Border(mark, HudTheme.Accent, 2f);
            }

            // 신호 — 커서에 붙어 세기를 말한다
            var mouse = BoardInput.Read().Mouse;
            if (body.Contains(mouse))
            {
                var r = Mathf.Lerp(46f, 20f, _strength);
                theme.Border(new Rect(mouse.x - r, mouse.y - r, r * 2f, r * 2f),
                    _strength > 0.6f ? HudTheme.Accent
                    : _strength > 0.25f ? HudTheme.Heat
                    : HudTheme.Rule, 2f);
            }

            var meter = new Rect(body.x + 16f, body.yMax - 34f, 240f, 16f);
            theme.Bar(meter, _strength,
                _strength > 0.6f ? HudTheme.Accent : HudTheme.Heat);
            GUI.Label(new Rect(meter.xMax + 12f, meter.y - 3f, 200f, 22f),
                _ping > 0f ? "찾았다" : _strength > 0.6f ? "가깝다" : _strength > 0.25f ? "무언가 있다" : "신호 없음",
                theme.At(theme.Small, 14, _ping > 0f ? HudTheme.Accent : HudTheme.Ink3));

            theme.Border(body, HudTheme.Rule);
        }
    }
}
