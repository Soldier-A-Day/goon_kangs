using UnityEngine;

namespace SoldierADay.Net
{
    /// <summary>
    /// `TRACE` 경로 잇기 — 5건. 단선 추적 · 배수로 · 유선 가설 · 차단기 · 급전선.
    ///
    /// 격자에 놓인 관을 돌려 시작에서 끝까지 잇는다. 클릭 하나뿐이다.
    ///
    /// **이 판만은 결과가 눈에 보인다.** 텐트 주변 배수가 "물이 실제로 흐르는지
    /// 검증"인 것이 설계안의 지시고, 그래서 이은 길에 신호가 흘러 들어가는 것을
    /// 그린다 — 다 맞췄는지 세어서 알려주는 것이 아니라 **닿는 것이 보인다**.
    ///
    /// `branches`가 있으면 갈래가 생긴다. 갈래는 전부 이어야 하고, 그래서 유선
    /// 가설(분기 3)이 초소 전화 점검(분기 0)보다 길다.
    /// </summary>
    public sealed class TraceBoard : Board
    {
        /// <summary>칸이 뚫린 방향 — 비트 0:위 1:오른쪽 2:아래 3:왼쪽</summary>
        private int[] _pipe;
        private bool[] _live;
        private bool[] _onPath;
        private int _cols;
        private int _rows;
        private int _start;
        private int _end;
        private int _turns;
        private Rect _area;

        public override string Instruction => "관을 눌러 돌리고 끝까지 이어라";

        public override string Status
        {
            get
            {
                var lit = 0;
                if (_live != null) foreach (var l in _live) if (l) lit += 1;
                return $"연결 {lit}칸  ·  돌린 횟수 {_turns}";
            }
        }

        protected override void Setup()
        {
            _cols = Mathf.Clamp(Mathf.CeilToInt(ParamInt("nodes", 10) / 2f) + 1, 4, 9);
            _rows = 4;
            _pipe = new int[_cols * _rows];
            _live = new bool[_cols * _rows];
            _onPath = new bool[_cols * _rows];
            _turns = 0;

            // 시작과 끝은 마주 보는 변에 둔다 — 어디서 어디로인지가 판을 열자마자 읽혀야 한다
            _start = Rng.Next(_rows) * _cols;
            _end = Rng.Next(_rows) * _cols + (_cols - 1);

            CarvePath(_start, _end);

            var branches = ParamInt("branches", 0);
            for (var i = 0; i < branches; i += 1) CarveBranch();

            // 다 깔고 나서 **돌려 놓는다.** 처음부터 이어져 있으면 판이 아니다
            var scramble = Mathf.Max(2, ParamInt("rotations", 5));
            var scrambled = 0;
            var guard = 0;
            while (scrambled < scramble && guard++ < 400)
            {
                var i = Rng.Next(_pipe.Length);
                if (_pipe[i] == 0) continue;
                _pipe[i] = Rotate(_pipe[i]);
                scrambled += 1;
            }

            Flow();
        }

        /// <summary>시작에서 끝까지 한 줄을 판다. 계단 모양이라 굽이가 생긴다</summary>
        private void CarvePath(int from, int to)
        {
            var x = from % _cols;
            var y = from / _cols;
            var tx = to % _cols;
            var ty = to / _cols;

            _onPath[from] = true;
            var guard = 0;
            while ((x != tx || y != ty) && guard++ < 200)
            {
                var horizontal = x != tx && (y == ty || Rng.Next(2) == 0);
                if (horizontal)
                {
                    var step = tx > x ? 1 : -1;
                    Connect(y * _cols + x, step > 0 ? 1 : 3);
                    x += step;
                    Connect(y * _cols + x, step > 0 ? 3 : 1);
                }
                else
                {
                    var step = ty > y ? 1 : -1;
                    Connect(y * _cols + x, step > 0 ? 2 : 0);
                    y += step;
                    Connect(y * _cols + x, step > 0 ? 0 : 2);
                }
                _onPath[y * _cols + x] = true;
            }
        }

        /// <summary>본선에서 갈라져 한 칸 뻗는다 — 갈래도 이어야 신호가 다 닿는다</summary>
        private void CarveBranch()
        {
            for (var attempt = 0; attempt < 60; attempt += 1)
            {
                var i = Rng.Next(_pipe.Length);
                if (!_onPath[i]) continue;

                var dir = Rng.Next(4);
                var n = Neighbor(i, dir);
                if (n < 0 || _onPath[n]) continue;

                Connect(i, dir);
                Connect(n, (dir + 2) % 4);
                _onPath[n] = true;
                return;
            }
        }

        private void Connect(int index, int dir) => _pipe[index] |= 1 << dir;

        private static int Rotate(int mask) => ((mask << 1) | (mask >> 3)) & 0xF;

        private int Neighbor(int index, int dir)
        {
            var x = index % _cols;
            var y = index / _cols;
            switch (dir)
            {
                case 0: y -= 1; break;
                case 1: x += 1; break;
                case 2: y += 1; break;
                default: x -= 1; break;
            }
            if (x < 0 || y < 0 || x >= _cols || y >= _rows) return -1;
            return y * _cols + x;
        }

        /// <summary>시작에서 신호를 흘린다. 이 결과가 곧 화면이다</summary>
        private void Flow()
        {
            for (var i = 0; i < _live.Length; i += 1) _live[i] = false;
            if (_pipe[_start] == 0) return;

            var stack = new System.Collections.Generic.Stack<int>();
            stack.Push(_start);
            _live[_start] = true;

            while (stack.Count > 0)
            {
                var at = stack.Pop();
                for (var dir = 0; dir < 4; dir += 1)
                {
                    if ((_pipe[at] & (1 << dir)) == 0) continue;
                    var n = Neighbor(at, dir);
                    if (n < 0 || _live[n]) continue;
                    // 맞은편도 뚫려 있어야 흐른다 — 반쪽만 맞으면 안 이어진다
                    if ((_pipe[n] & (1 << ((dir + 2) % 4))) == 0) continue;
                    _live[n] = true;
                    stack.Push(n);
                }
            }
        }

        protected override void Advance(float dt, BoardInput input)
        {
            if (!input.Pressed || _area.width <= 0f) return;

            for (var i = 0; i < _pipe.Length; i += 1)
            {
                if (_pipe[i] == 0) continue;
                if (!CellRect(i).Contains(input.Mouse)) continue;

                _pipe[i] = Rotate(_pipe[i]);
                _turns += 1;
                Flow();

                // 필요 이상으로 돌리면 손이 헤맨 것이다. `rotations`의 두 배까지 봐준다
                var budget = Mathf.Max(4, ParamInt("rotations", 5) * 2);
                if (_turns == budget + 1 || _turns == budget * 2 + 1) Miss();

                var lit = 0;
                foreach (var l in _live) if (l) lit += 1;
                var total = 0;
                foreach (var p in _pipe) if (p != 0) total += 1;
                Fill = total == 0 ? 1f : (float)lit / total;

                // 끝에 닿고 **모든 갈래에 신호가 갔을 때** 통과다
                if (_live[_end] && lit >= total) Clear();
                return;
            }
        }

        private Rect CellRect(int index)
        {
            var size = Mathf.Min(_area.width / _cols, _area.height / _rows);
            var ox = _area.center.x - size * _cols * 0.5f;
            var oy = _area.center.y - size * _rows * 0.5f;
            return new Rect(ox + (index % _cols) * size + 3f,
                            oy + (index / _cols) * size + 3f,
                            size - 6f, size - 6f);
        }

        public override void Draw(HudTheme theme, Rect body)
        {
            _area = body;
            theme.Fill(body, HudTheme.Paper3);

            for (var i = 0; i < _pipe.Length; i += 1)
            {
                var cell = CellRect(i);
                if (_pipe[i] == 0)
                {
                    theme.Fill(cell, HudTheme.Paper2, 0.4f);
                    continue;
                }

                var lit = _live[i];
                theme.Fill(cell, lit ? HudTheme.AccentW : HudTheme.Paper);
                theme.Border(cell, lit ? HudTheme.Accent : HudTheme.Rule2);

                // 관 — 뚫린 방향으로 가운데에서 뻗는다
                var color = lit ? HudTheme.Accent : HudTheme.Ink3;
                var c = cell.center;
                const float w = 9f;
                theme.Fill(new Rect(c.x - w * 0.5f, c.y - w * 0.5f, w, w), color);
                if ((_pipe[i] & 1) != 0)
                    theme.Fill(new Rect(c.x - w * 0.5f, cell.y, w, c.y - cell.y), color);
                if ((_pipe[i] & 2) != 0)
                    theme.Fill(new Rect(c.x, c.y - w * 0.5f, cell.xMax - c.x, w), color);
                if ((_pipe[i] & 4) != 0)
                    theme.Fill(new Rect(c.x - w * 0.5f, c.y, w, cell.yMax - c.y), color);
                if ((_pipe[i] & 8) != 0)
                    theme.Fill(new Rect(cell.x, c.y - w * 0.5f, c.x - cell.x, w), color);
            }

            // 시작과 끝 — 어디서 어디로인지
            theme.Border(CellRect(_start), HudTheme.Heat, 3f);
            theme.Border(CellRect(_end), _live[_end] ? HudTheme.Accent : HudTheme.Alert, 3f);
            GUI.Label(new Rect(CellRect(_start).x, CellRect(_start).y - 24f, 80f, 20f), "시작",
                theme.At(theme.Label, 12, HudTheme.Heat));
            GUI.Label(new Rect(CellRect(_end).x, CellRect(_end).y - 24f, 80f, 20f), "끝",
                theme.At(theme.Label, 12, _live[_end] ? HudTheme.Accent : HudTheme.Alert));

            theme.Border(body, HudTheme.Rule);
        }
    }
}
