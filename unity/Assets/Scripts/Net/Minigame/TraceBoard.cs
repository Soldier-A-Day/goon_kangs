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
    ///
    /// ── 긴장감 패스 B — Pipe Mania 물 ─────────────────────────────────────
    /// 판을 열고 `_waterDelay`(기본 4초)가 지나면 시작 칸부터 **진짜 물이
    /// 찬다** — 지금 이어진(`_live`) 칸을 따라 초당 `_waterSpeed`칸씩. 물은
    /// `Flow`가 매 회전마다 다시 재는 "이어진 끝"(`_frontierDist`)을 넘어설
    /// 수 없다 — 거기가 아직 안 이은 막다른 관이라는 뜻이다. 따라잡히면
    /// 실수 하나가 붙고 2초 멈췄다 같은 자리에서 다시 흐른다(자리를 처음으로
    /// 되돌리지 않는다 — 그사이 관을 돌려 물길을 열어주면 다음 틱에 바로
    /// 이어서 나간다). **회전 횟수로 실수를 세던 예전 규칙은 걷어냈다** —
    /// 물이 같은 자리(손이 헤맨 것)를 이미 더 정확하게 벌하므로, 둘을
    /// 같이 두면 이중 처벌이다(발주 §1-②).
    ///
    /// ── B-1 정보 비대칭 — 정답 스냅샷 ────────────────────────────────────
    /// 합동판에서 정답 역할이 볼 화면은 이 판을 돌리는 것(`Tick`)이 아니라
    /// `DrawSolved`를 부르는 것으로 만든다. 스크램블 전(=완성) 배치를
    /// `_solvedPipe`에 미리 복제해 두고, 그걸 회전 없이 전부 lit로 그린다 —
    /// 조작 역할이 실제로 돌리는 `_pipe`는 절대 안 건드리는 별도 경로다
    /// (`JointBoard`가 역할에 따라 `Draw` 대신 이걸 부른다).
    ///
    /// ── B-1 재수리 — 조작 역할도 정답을 보면 안 된다(`NoReveal`) ──────────
    /// 위 문단은 정답 역할 쪽만 막았다. 조작 역할은 여전히 이 판의 일반
    /// `Draw`를 그대로 받았는데, 거기엔 관 방향·연결(lit) 색·물이 다 있어서
    /// 정답 역할 없이 혼자 다 보고 혼자 풀 수 있었다. `NoReveal`이 켜지면
    /// `Draw`가 `DrawBlind`로 빠져 그 셋을 전부 뺀다 — 남는 것은 칸이
    /// 있다는 것과 포커스뿐이다. `JointBoard.OpenRound`가 `SeqBoard.Locked`/
    /// `NoReveal`과 같은 자리에서 `trace.NoReveal = IsOperate`를 꽂는다.
    /// </summary>
    public sealed class TraceBoard : Board
    {
        /// <summary>
        /// 사용자 지시 — "trace 게임에서 협동할 때 조작하는 사람은 타일이
        /// 안 보여야지." 지금까지 조작(`operate`) 역할은 `JointBoard.Draw`가
        /// `_inner.Draw`(이 클래스의 일반 `Draw`)를 그대로 불러서, 관 방향·
        /// 이어졌는지(lit)·물 찬 자리가 전부 보였다 — 정답 역할 없이 혼자
        /// 다 보고 혼자 풀 수 있었다.
        ///
        /// `JointBoard.OpenRound`가 `SeqBoard.Locked`/`NoReveal`과 같은 자리에
        /// `trace.NoReveal = IsOperate`를 꽂는다(그 파일의 유일한 변경점 —
        /// `TraceBoard.cs` 하나만으로는 조작 역할이 이 값을 켜 줄 방법이 없다.
        /// 아래 참고). 이 값이 켜지면 `Draw`는 `DrawBlind`로 빠진다: 칸이
        /// 있다/없다와 지금 포커스가 어디인지만 보이고, 관 십자선·lit 색·물은
        /// 하나도 그리지 않는다. **판정(`Advance`·`Flow`·`UpdateWater`·
        /// `Miss`)은 그대로 돈다** — 가리는 것은 순전히 그림뿐이다.
        ///
        /// 단독판(비대칭 꺼짐, `JointBoard`를 안 거치는 `BoardLab` 등)에서는
        /// `IsOperate`가 존재하지 않으니 이 값이 항상 `false`고, 지금까지와
        /// 완전히 같다.
        /// </summary>
        public bool NoReveal { get; set; }

        /// <summary>칸이 뚫린 방향 — 비트 0:위 1:오른쪽 2:아래 3:왼쪽</summary>
        private int[] _pipe;

        /// <summary>
        /// B-1 — 스크램블 전(=정답) 배치. `Setup`이 길을 다 판 직후, 돌려
        /// 놓기(스크램블) 전에 한 번 복제해 둔다. `DrawSolved`가 이걸 그대로
        /// 그린다 — `_pipe`(조작 역할이 실제로 돌리는 판)는 절대 안 건드린다
        /// </summary>
        private int[] _solvedPipe;

        private bool[] _live;
        private bool[] _onPath;
        private int _cols;
        private int _rows;
        private int _start;
        private int _end;
        private int _turns;
        private Rect _area;

        /// <summary>칸마다 마지막으로 돌린 시각(`Time.time`) — 회전 트윈의 기준</summary>
        private float[] _rotSince;

        /// <summary>회전 트윈 길이(초). 즉시 스냅되면 손맛이 없다</summary>
        private const float RotateDuration = 0.1f;

        /* ───────────────────────────────────────────── H-4 키보드 포커스 */

        /// <summary>지금 포커스된 칸(0.._pipe.Length-1). 빈 칸이어도 올라갈 수 있다 —
        /// 회전은 `RotateCellAt`이 알아서 무시한다(클릭이 빈 칸을 짚는 것과 같다)</summary>
        private int _focus;
        private float _hNavCooldown;
        private float _vNavCooldown;
        private const float NavCooldown = 0.14f;
        private bool _usingKeyboard = true;
        private Vector2 _lastMouseForFocus = new Vector2(-1f, -1f);

        /* ──────────────────────────────────────── 긴장감 패스 B — Pipe Mania 물 */

        private enum WaterState
        {
            Idle,
            Flowing,
            Blocked,
        }

        /// <summary>시작에서부터 BFS로 잰 칸 거리. `_live`가 아니면 -1</summary>
        private int[] _dist;
        /// <summary>지금 이어진 칸 중 가장 먼 거리 — 물이 넘어설 수 없는 경계다</summary>
        private int _frontierDist;

        private WaterState _waterState;
        /// <summary>Idle이면 물이 차기까지 남은 시간, Blocked면 다시 흐르기까지 남은 시간</summary>
        private float _waterTimer;
        /// <summary>물이 시작에서부터 차오른 거리(칸). 소수점은 칸 사이 진행률이다</summary>
        private float _waterDist;
        private float _waterDelay;
        private float _waterSpeed;

        private const float WaterBlockPause = 2f;

        /// <summary>마지막 3초 — 공통 하이라이트용 박동 타이머</summary>
        private float _finalBeatTimer;

        public override string Instruction =>
            "관을 눌러 돌리고 끝까지 이어라 — 화살표로 옮기고 Space로 돌린다(물이 차오른다)";

        public override string Status
        {
            get
            {
                // 연결 칸 수·수위·"물이 막혔다"는 전부 지금 이어졌는지(=정답에
                // 얼마나 가까운지)를 그대로 흘리는 채널이다 — 조작 역할에게는
                // 화면(`DrawBlind`)뿐 아니라 이 한 줄도 정답을 새면 안 된다.
                // 돌린 횟수는 자기 행동의 기록이라 정답이 아니므로 남긴다.
                if (NoReveal) return $"돌린 횟수 {_turns}  ·  정답은 다른 사람 화면에 있다";

                var lit = 0;
                if (_live != null) foreach (var l in _live) if (l) lit += 1;
                var water = _waterState switch
                {
                    WaterState.Idle => $"물까지 {Mathf.CeilToInt(_waterTimer)}초",
                    WaterState.Blocked => "물이 막혔다",
                    _ => $"수위 {Mathf.FloorToInt(_waterDist)}칸",
                };
                return $"연결 {lit}칸  ·  돌린 횟수 {_turns}  ·  {water}";
            }
        }

        protected override void Setup()
        {
            _cols = Mathf.Clamp(Mathf.CeilToInt(ParamInt("nodes", 10) / 2f) + 1, 4, 9);
            _rows = 4;
            _pipe = new int[_cols * _rows];
            _live = new bool[_cols * _rows];
            _onPath = new bool[_cols * _rows];
            _dist = new int[_cols * _rows];
            _turns = 0;

            // 트윈이 끝난 상태(각 0°)로 시작한다 — `Time.time - RotateDuration`은
            // 항상 과거이므로 `Draw`가 처음 그릴 때부터 경과율이 1이다
            _rotSince = new float[_cols * _rows];
            for (var i = 0; i < _rotSince.Length; i += 1) _rotSince[i] = -RotateDuration;

            // 시작과 끝은 마주 보는 변에 둔다 — 어디서 어디로인지가 판을 열자마자 읽혀야 한다
            _start = Rng.Next(_rows) * _cols;
            _end = Rng.Next(_rows) * _cols + (_cols - 1);

            CarvePath(_start, _end);

            var branches = ParamInt("branches", 0);
            for (var i = 0; i < branches; i += 1) CarveBranch();

            // B-1 — 돌려 놓기 전, 즉 정답 그대로인 배치를 복제해 둔다.
            // 정답 역할은 이걸(`DrawSolved`) 그대로 보고, 조작 역할이 돌리는
            // `_pipe`는 아래에서 스크램블된다
            _solvedPipe = (int[])_pipe.Clone();

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

            // 물 — 시작 4초 뒤부터 차오른다. 난이도가 오르면 유예가 줄고 유속이 빨라진다
            _waterDelay = Mathf.Lerp(4f, 2.4f, (Difficulty - 1f) * 0.5f);
            _waterSpeed = Mathf.Lerp(1f, 1.6f, (Difficulty - 1f) * 0.5f);
            _waterState = WaterState.Idle;
            _waterTimer = _waterDelay;
            _waterDist = 0f;

            // 키보드 포커스는 "시작" 칸에서 출발한다 — 첫 Space가 곧바로
            // 뜻있는 자리를 돌리게 한다
            _focus = _start;
            _hNavCooldown = 0f;
            _vNavCooldown = 0f;
            _usingKeyboard = true;
            _lastMouseForFocus = new Vector2(-1f, -1f);
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

        /// <summary>
        /// 시작에서 신호를 흘린다. 이 결과가 곧 화면이다.
        ///
        /// BFS로 바꾼 이유는 물 연출(`UpdateWater`)이 "시작에서부터 몇 칸째인가"
        /// (`_dist`)를 알아야 해서다 — 예전 DFS(스택)도 `_live` 집합 자체는
        /// 똑같이 만들지만 거리는 안 준다. 도달 가능한 칸 집합은 순회 순서와
        /// 무관하므로 이 교체는 판정에 아무 영향이 없다.
        /// </summary>
        private void Flow()
        {
            for (var i = 0; i < _live.Length; i += 1) { _live[i] = false; _dist[i] = -1; }
            _frontierDist = 0;
            if (_pipe[_start] == 0) return;

            var queue = new System.Collections.Generic.Queue<int>();
            queue.Enqueue(_start);
            _live[_start] = true;
            _dist[_start] = 0;

            while (queue.Count > 0)
            {
                var at = queue.Dequeue();
                if (_dist[at] > _frontierDist) _frontierDist = _dist[at];

                for (var dir = 0; dir < 4; dir += 1)
                {
                    if ((_pipe[at] & (1 << dir)) == 0) continue;
                    var n = Neighbor(at, dir);
                    if (n < 0 || _live[n]) continue;
                    // 맞은편도 뚫려 있어야 흐른다 — 반쪽만 맞으면 안 이어진다
                    if ((_pipe[n] & (1 << ((dir + 2) % 4))) == 0) continue;
                    _live[n] = true;
                    _dist[n] = _dist[at] + 1;
                    queue.Enqueue(n);
                }
            }
        }

        protected override void Advance(float dt, BoardInput input)
        {
            if (_area.width <= 0f) return;

            UpdateFocusNav(dt, input);
            UpdateWater(dt);
            FinalStretchTick(dt);

            if (input.Pressed)
            {
                _usingKeyboard = false;
                for (var i = 0; i < _pipe.Length; i += 1)
                {
                    if (_pipe[i] == 0) continue;
                    if (!CellRect(i).Contains(input.Mouse)) continue;
                    RotateCellAt(i);
                    return;
                }
                return;
            }

            // H-4 — 클릭 대신 Space·Enter. 포커스가 있는 칸을 돌린다(빈 칸이면
            // `RotateCellAt`이 아무 일도 하지 않는다 — 클릭으로 빈 칸을 짚는 것과 같다)
            if (!input.Confirm) return;
            _usingKeyboard = true;
            RotateCellAt(_focus);
        }

        /// <summary>
        /// 물의 흐름. Idle → 유예가 끝나면 Flowing. Flowing 중엔 `_frontierDist`를
        /// 따라잡으면(=아직 안 이은 막다른 관에 닿으면) 실수 하나를 물리고 그
        /// 자리에서 2초 멈춘다(Blocked) — 자리를 처음으로 되돌리지 않으므로
        /// 그사이 관을 돌려 물길을 열어주면 다음 틱에 바로 이어서 나간다.
        /// </summary>
        private void UpdateWater(float dt)
        {
            switch (_waterState)
            {
                case WaterState.Idle:
                    _waterTimer -= dt;
                    if (_waterTimer <= 0f) _waterState = WaterState.Flowing;
                    break;

                case WaterState.Flowing:
                    _waterDist += _waterSpeed * dt;
                    if (_waterDist > _frontierDist + 0.001f)
                    {
                        _waterDist = _frontierDist;
                        Miss();
                        _waterState = WaterState.Blocked;
                        _waterTimer = WaterBlockPause;
                    }
                    break;

                case WaterState.Blocked:
                    _waterTimer -= dt;
                    if (_waterTimer <= 0f) _waterState = WaterState.Flowing;
                    break;
            }
        }

        /// <summary>칸 하나를 90도 돌린다. 판정 로직은 여기 하나뿐이라 마우스 클릭과
        /// 키보드 포커스 확정이 정확히 같은 결과를 낸다</summary>
        private void RotateCellAt(int i)
        {
            if (_pipe[i] == 0) return; // 빈 칸

            _pipe[i] = Rotate(_pipe[i]);
            _turns += 1;
            // 논리는 즉시 바뀐다 — 트윈은 `Draw`가 이 시각을 기준으로
            // 그려 보이는 것일 뿐, 판정은 이번 프레임에 이미 끝나 있다
            _rotSince[i] = Time.time;
            Sfx.Play("tap", 0.8f, 1.15f);
            Flow();

            var lit = 0;
            foreach (var l in _live) if (l) lit += 1;
            var total = 0;
            foreach (var p in _pipe) if (p != 0) total += 1;
            Fill = total == 0 ? 1f : (float)lit / total;

            // 끝에 닿고 **모든 갈래에 신호가 갔을 때** 통과다
            if (_live[_end] && lit >= total) Clear();
        }

        /// <summary>
        /// 화살표·WASD로 포커스 칸을 옮긴다. 마우스가 이번 프레임에 움직이면
        /// 포커스 테두리를 감춘다(발주 §공통 패턴 "마우스 사용 중엔 숨김") —
        /// `AuditBoard`와 같은 방식이다.
        /// </summary>
        private void UpdateFocusNav(float dt, BoardInput input)
        {
            if (_lastMouseForFocus.x >= 0f && input.Mouse != _lastMouseForFocus) _usingKeyboard = false;
            _lastMouseForFocus = input.Mouse;

            _vNavCooldown -= dt;
            if (Mathf.Abs(input.Vertical) > 0.5f)
            {
                if (_vNavCooldown <= 0f)
                {
                    // 화면 y는 아래로 갈수록 커진다 — 위쪽 화살표(Vertical +1)는
                    // 행을 하나 줄인다
                    MoveFocus(0, input.Vertical > 0f ? -1 : 1);
                    _vNavCooldown = NavCooldown;
                    _usingKeyboard = true;
                }
            }
            else _vNavCooldown = 0f;

            _hNavCooldown -= dt;
            if (Mathf.Abs(input.Horizontal) > 0.5f)
            {
                if (_hNavCooldown <= 0f)
                {
                    MoveFocus(input.Horizontal > 0f ? 1 : -1, 0);
                    _hNavCooldown = NavCooldown;
                    _usingKeyboard = true;
                }
            }
            else _hNavCooldown = 0f;
        }

        private void MoveFocus(int dx, int dy)
        {
            var x = Mathf.Clamp(_focus % _cols + dx, 0, _cols - 1);
            var y = Mathf.Clamp(_focus / _cols + dy, 0, _rows - 1);
            _focus = y * _cols + x;
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

        /// <summary>마지막 3초 — 펄스 테두리 + 저음 심박(공통 패스 B 항목). 남은 시간이
        /// 짧아질수록 박동이 빨라진다</summary>
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
            _area = body;

            if (NoReveal)
            {
                DrawBlind(theme, body);
                return;
            }

            theme.Fill(body, HudTheme.Paper3);

            // 물 — 이어진 칸을 따라 차오른 것을 옅은 파랑으로 겹쳐 그린다. 관
            // 그림보다 먼저 그려서 위에 관 아이콘이 또렷하게 남는다
            for (var i = 0; i < _dist.Length; i += 1)
            {
                if (_dist[i] < 0 || _dist[i] > _waterDist) continue;
                theme.Fill(CellRect(i), HudTheme.Cold, 0.35f);
            }

            for (var i = 0; i < _pipe.Length; i += 1)
            {
                var cell = CellRect(i);
                if (_pipe[i] == 0)
                {
                    theme.Fill(cell, HudTheme.Paper2, 0.4f);
                    continue;
                }

                var lit = _live[i];

                // **회전 무게감.** 논리(`_pipe`·`Flow`)는 클릭 즉시 바뀌었지만,
                // 화면은 −90°에서 0°로 `RotateDuration`초에 걸쳐 이징한다 —
                // 즉시 스냅되면 관에 무게가 없다. `OnGUI`는 한 프레임에 여러 번
                // (Layout·Repaint) 불리므로 여기서 델타를 누적하지 않고, 돌린
                // 시각(`_rotSince[i]`, `Time.time` 기록)을 기준으로 매번 다시
                // 계산한다 — `PlaceBoard`가 각을 도는 물건을 그리는 것과 같은
                // `GUIUtility.RotateAroundPivot` 자리다.
                var t = Mathf.Clamp01((Time.time - _rotSince[i]) / RotateDuration);
                var eased = 1f - (1f - t) * (1f - t) * (1f - t);
                var angle = Mathf.Lerp(-90f, 0f, eased);

                var matrix = GUI.matrix;
                // `GUIUtility.RotateAroundPivot`은 배율 걸린 GUI.matrix 아래서
                // 피벗이 밀린다 — 관이 제자리가 아니라 궤도를 돈다(HudTheme.RotateAt)
                HudTheme.RotateAt(angle, cell.center);
                DrawPipeVisual(theme, cell, _pipe[i], lit);
                GUI.matrix = matrix;
            }

            // 시작과 끝 — 어디서 어디로인지
            theme.Border(CellRect(_start), HudTheme.Heat, 3f);
            theme.Border(CellRect(_end), _live[_end] ? HudTheme.Accent : HudTheme.Alert, 3f);
            GUI.Label(new Rect(CellRect(_start).x, CellRect(_start).y - 24f, 80f, 20f), "시작",
                theme.At(theme.Label, 12, HudTheme.Heat));
            GUI.Label(new Rect(CellRect(_end).x, CellRect(_end).y - 24f, 80f, 20f), "끝",
                theme.At(theme.Label, 12, _live[_end] ? HudTheme.Accent : HudTheme.Alert));

            // H-4 — 키보드 포커스 테두리. 마우스를 쓰는 동안은 감춘다
            if (_usingKeyboard) theme.Border(CellRect(_focus), HudTheme.Cold, 3f);

            // 물이 막혔을 때·차오르기 전일 때의 안내
            if (_waterState == WaterState.Blocked)
            {
                GUI.Label(new Rect(body.x, body.y + 6f, body.width, 24f), "물이 막혔다! 곧 다시 흐른다",
                    theme.At(theme.Small, 14, HudTheme.Alert, TextAnchor.MiddleCenter));
            }
            else if (_waterState == WaterState.Idle)
            {
                GUI.Label(new Rect(body.x, body.y + 6f, body.width, 24f),
                    $"물이 {Mathf.CeilToInt(_waterTimer)}초 뒤 찬다",
                    theme.At(theme.Small, 14, HudTheme.Cold, TextAnchor.MiddleCenter));
            }

            theme.Border(body, HudTheme.Rule);
            FinalStretchDraw(theme, body);
        }

        /// <summary>
        /// 조작 역할 전용 화면(`NoReveal`). 정답으로 이어지는 채널 — 관 방향
        /// 십자선 · lit 색 구분 · 물 오버레이 · "물이 막혔다" 안내 · `_end`
        /// 테두리의 도달 여부(Accent/Alert) — 을 전부 뺀다. 남기는 것은
        /// 칸이 있는지(빈 칸은 원래처럼 흐리게), 시작/끝이 어딘지, 지금
        /// 키보드 포커스가 어딘지뿐이다 — 전부 "어디를 누를 수 있는가"에
        /// 필요한 정보고, "지금 맞는가"는 하나도 없다.
        ///
        /// 회전 트윈(`_rotSince`)도 굳이 태우지 않는다 — 돌아가는 방향을
        /// 보여주는 애니메이션 자체가 방향 정보이므로, 트윈 없이 즉시
        /// 칸을 그대로 둔다.
        /// </summary>
        private void DrawBlind(HudTheme theme, Rect body)
        {
            theme.Fill(body, HudTheme.Paper3);

            for (var i = 0; i < _pipe.Length; i += 1)
            {
                var cell = CellRect(i);
                if (_pipe[i] == 0)
                {
                    theme.Fill(cell, HudTheme.Paper2, 0.4f);
                    continue;
                }
                // 방향·연결 여부를 알 수 없는 중립 칸 — "여기 관이 있고 누를 수
                // 있다"만 보인다. 실제 판(lit 상태)과 같은 팔레트를 쓰되 항상
                // 미연결(Rule2·Paper) 쪽 색으로 고정해 lit이 정답을 새지 않는다
                theme.Fill(cell, HudTheme.Paper);
                theme.Border(cell, HudTheme.Rule2);
            }

            // 시작/끝 칸 위치는 두 화면(조작·정답) 모두 같은 판 구조라 이미
            // 공유된 정보다 — 어디가 시작인지는 숨길 정답이 아니다. 다만 `_end`
            // 테두리 색을 도달 여부로 바꾸던 것(Accent/Alert)은 "지금 다
            // 맞았는가"를 그대로 알려주므로 고정색 하나로 통일한다
            theme.Border(CellRect(_start), HudTheme.Heat, 3f);
            theme.Border(CellRect(_end), HudTheme.Rule2, 3f);
            GUI.Label(new Rect(CellRect(_start).x, CellRect(_start).y - 24f, 80f, 20f), "시작",
                theme.At(theme.Label, 12, HudTheme.Heat));
            GUI.Label(new Rect(CellRect(_end).x, CellRect(_end).y - 24f, 80f, 20f), "끝",
                theme.At(theme.Label, 12, HudTheme.Ink3));

            if (_usingKeyboard) theme.Border(CellRect(_focus), HudTheme.Cold, 3f);

            GUI.Label(new Rect(body.x, body.y + 6f, body.width, 24f),
                "정답은 안 보인다 — 불러주는 대로 눌러라",
                theme.At(theme.Small, 14, HudTheme.Ink3, TextAnchor.MiddleCenter));

            theme.Border(body, HudTheme.Rule);
            FinalStretchDraw(theme, body);
        }

        /// <summary>
        /// 칸 하나의 시각(채움·테두리·관 십자 선). 회전 트윈을 씌우는지는
        /// 호출부 몫이다 — 실전 판(`Draw`)은 `GUI.matrix`로 각도를 감싸 부르고,
        /// 정답 스냅샷(`DrawSolved`)은 각도 없이 그대로 부른다. 관 그림
        /// 코드를 두 벌 두지 않으려고 뗐다(B-1)
        /// </summary>
        private void DrawPipeVisual(HudTheme theme, Rect cell, int mask, bool lit)
        {
            theme.Fill(cell, lit ? HudTheme.AccentW : HudTheme.Paper);
            theme.Border(cell, lit ? HudTheme.Accent : HudTheme.Rule2);

            var color = lit ? HudTheme.Accent : HudTheme.Ink3;
            var c = cell.center;
            const float w = 9f;
            theme.Fill(new Rect(c.x - w * 0.5f, c.y - w * 0.5f, w, w), color);
            if ((mask & 1) != 0)
                theme.Fill(new Rect(c.x - w * 0.5f, cell.y, w, c.y - cell.y), color);
            if ((mask & 2) != 0)
                theme.Fill(new Rect(c.x, c.y - w * 0.5f, cell.xMax - c.x, w), color);
            if ((mask & 4) != 0)
                theme.Fill(new Rect(c.x - w * 0.5f, c.y, w, cell.yMax - c.y), color);
            if ((mask & 8) != 0)
                theme.Fill(new Rect(cell.x, c.y - w * 0.5f, c.x - cell.x, w), color);
        }

        /// <summary>
        /// B-1 — 정답 역할의 화면(`JointBoard`가 `Draw` 대신 이걸 부른다).
        ///
        /// 스크램블 전 배치(`_solvedPipe`)를, 회전 없이, 전부 신호가 흐르는
        /// 것으로 그린다 — 완성된 정답 배치 그 자체다. 물·회전 트윈·키보드
        /// 포커스 같은 조작 역할의 진행 상태는 아무것도 안 쓴다(`_pipe`·
        /// `_live`·`_waterState`는 조작 역할의 판이 따로 갖고 있고, 이 메서드는
        /// 그것들을 건드리지 않는다). `_area`만 여기서 맞춰 준다 — `Advance`가
        /// 안 불리므로(정답 역할은 Tick 자체를 받지 않는다) 그게 아니면
        /// `CellRect`가 쓸 좌표가 없다.
        /// </summary>
        public void DrawSolved(HudTheme theme, Rect body)
        {
            _area = body;
            theme.Fill(body, HudTheme.Paper3);

            for (var i = 0; i < _solvedPipe.Length; i += 1)
            {
                var cell = CellRect(i);
                if (_solvedPipe[i] == 0)
                {
                    theme.Fill(cell, HudTheme.Paper2, 0.4f);
                    continue;
                }
                // 정답이니 전부 lit — 이 모양대로 맞추면 끝까지 흐른다는 뜻이다
                DrawPipeVisual(theme, cell, _solvedPipe[i], true);
            }

            theme.Border(CellRect(_start), HudTheme.Heat, 3f);
            theme.Border(CellRect(_end), HudTheme.Accent, 3f);
            GUI.Label(new Rect(CellRect(_start).x, CellRect(_start).y - 24f, 80f, 20f), "시작",
                theme.At(theme.Label, 12, HudTheme.Heat));
            GUI.Label(new Rect(CellRect(_end).x, CellRect(_end).y - 24f, 80f, 20f), "끝",
                theme.At(theme.Label, 12, HudTheme.Accent));

            theme.Border(body, HudTheme.Rule);
        }
    }
}
