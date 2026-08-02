using UnityEngine;

namespace SoldierADay.Net
{
    /// <summary>
    /// `PLACE` 정돈 · 배치 — 10건. 가장 많이 쓰이는 원형이다.
    ///
    /// 실루엣 자리에 물건을 끌어다 놓는다. 드래그 하나뿐이다.
    ///
    /// **각이 있는 것과 없는 것이 갈린다**(`snapDeg`).
    ///
    ///   `0`    자리만 맞추면 된다 — 복도 정돈 · 건조대 간격 · 의자 밀어넣기
    ///   `90`   각을 맞춰야 한다 — 관물대 정돈 · 섀도보드
    ///   `180`  **반대로 꽂으면 즉시 실패** — 무전기 배터리 극성
    ///
    /// 각을 조작으로 따로 받지 않는다. 끄는 동안 물건이 천천히 돌고, **놓는
    /// 순간의 각**이 스냅된다 — 조작은 여전히 드래그 하나이고, 언제 놓느냐가
    /// 각이 된다.
    /// </summary>
    public sealed class PlaceBoard : Board
    {
        private sealed class Piece
        {
            public Vector2 Home;
            public Vector2 At;
            public Rect Slot;
            public float Angle;
            public bool Placed;
            public bool Crooked;
        }

        private Piece[] _pieces;
        private int _held = -1;
        private int _placed;
        private float _snap;
        private Rect _area;

        private const float Size = 54f;

        public override string Instruction => _snap > 0f
            ? "끌어다 놓아라 — 놓는 순간의 각이 남는다"
            : "실루엣 자리로 끌어다 놓아라";

        public override string Status =>
            $"{_placed}/{_pieces?.Length ?? 0} 배치" + (Mistakes > 0 ? $"  ·  어긋남 {Mistakes}" : "");

        /// <summary>
        /// 자리만 맞추면 되는 자유 배치(`snapDeg` 0)만 시간초과로 통과한다.
        ///
        /// 각·극성 변형(90°·180°)은 다르다 — 각을 놓치거나 극성을 반대로
        /// 꽂는 판단이 이 변형의 본질이라 시간이 압박으로 남아야 한다. 하드를
        /// 그대로 유지한다.
        /// </summary>
        protected override bool GradesOnTimeout => _snap <= 0f;

        protected override void Setup()
        {
            var count = Mathf.Clamp(ParamInt("pieces", 6), 2, 12);
            _snap = Param("snapDeg", 0f);
            _pieces = new Piece[count];
            _placed = 0;
            _held = -1;

            for (var i = 0; i < count; i += 1)
            {
                _pieces[i] = new Piece
                {
                    // 자리는 `Draw`가 판 크기를 알려줄 때 잡힌다. 여기서는 비율만 정한다
                    Home = new Vector2((i + 0.5f) / count, 0.82f),
                    At = new Vector2((i + 0.5f) / count, 0.82f),
                    Angle = _snap > 0f ? RandRange(0f, 360f) : 0f,
                };
            }

            // 자리를 섞는다. 왼쪽 물건이 왼쪽 자리로 가면 배치가 아니라 줄 세우기다
            var order = new int[count];
            for (var i = 0; i < count; i += 1) order[i] = i;
            for (var i = count - 1; i > 0; i -= 1)
            {
                var j = Rng.Next(i + 1);
                (order[i], order[j]) = (order[j], order[i]);
            }
            for (var i = 0; i < count; i += 1)
            {
                _pieces[i].Slot = new Rect((order[i] + 0.5f) / count, 0.26f, 0f, 0f);
            }
        }

        protected override void Advance(float dt, BoardInput input)
        {
            if (_area.width <= 0f || _pieces == null) return;

            if (input.Pressed && _held < 0)
            {
                for (var i = 0; i < _pieces.Length; i += 1)
                {
                    if (_pieces[i].Placed) continue;
                    if (!Box(_pieces[i].At).Contains(input.Mouse)) continue;
                    _held = i;
                    break;
                }
            }

            if (_held >= 0)
            {
                var piece = _pieces[_held];
                piece.At = Norm(input.Mouse);

                // 끄는 동안 돈다. 난이도가 오르면 빨리 돌아 놓을 창이 좁아진다
                if (_snap > 0f) piece.Angle = Mathf.Repeat(piece.Angle + 78f * Difficulty * dt, 360f);

                if (input.Released) Drop(piece);
            }

            Fill = _pieces.Length == 0 ? 1f : (float)_placed / _pieces.Length;
            if (_placed >= _pieces.Length) Clear();
        }

        private void Drop(Piece piece)
        {
            _held = -1;

            var tolerance = Param("tolerance", 0.12f) * _area.width;
            var slot = Point(piece.Slot.x, piece.Slot.y);
            if (Vector2.Distance(Point(piece.At.x, piece.At.y), slot) > tolerance)
            {
                // 빗나가면 제자리로. 감점은 없다 — 잃은 것은 시간이다
                piece.At = piece.Home;
                return;
            }

            piece.Placed = true;
            piece.At = piece.Slot.position;
            _placed += 1;

            if (_snap <= 0f) return;

            // 놓는 순간의 각을 스냅한다. 0°가 아니면 어긋난 것이다
            var snapped = Mathf.Round(piece.Angle / _snap) * _snap;
            piece.Angle = Mathf.Repeat(snapped, 360f);
            if (Mathf.Abs(Mathf.DeltaAngle(piece.Angle, 0f)) < 1f) return;

            piece.Crooked = true;
            // 극성은 되돌릴 수 없다 — 반대로 꽂으면 그 자리에서 끝난다
            if (_snap >= 180f) { Fail(); return; }
            Miss();
        }

        private Vector2 Norm(Vector2 point) => new Vector2(
            Mathf.Clamp01((point.x - _area.x) / _area.width),
            Mathf.Clamp01((point.y - _area.y) / _area.height));

        private Vector2 Point(float nx, float ny) =>
            new Vector2(_area.x + nx * _area.width, _area.y + ny * _area.height);

        private Rect Box(Vector2 norm)
        {
            var p = Point(norm.x, norm.y);
            return new Rect(p.x - Size * 0.5f, p.y - Size * 0.5f, Size, Size);
        }

        public override void Draw(HudTheme theme, Rect body)
        {
            _area = body;
            if (_pieces == null) return;

            theme.Fill(body, HudTheme.Paper3);

            // 실루엣 자리 — 어디에 놓아야 하는지 먼저 읽힌다
            foreach (var piece in _pieces)
            {
                var slot = Box(piece.Slot.position);
                theme.Fill(slot, HudTheme.Paper);
                theme.Border(slot, piece.Placed ? HudTheme.Accent : HudTheme.Rule2);
            }

            // 바닥 선반 — 물건이 놓여 있던 자리
            var shelf = new Rect(body.x, body.y + body.height * 0.74f, body.width, 2f);
            theme.Fill(shelf, HudTheme.Rule3);

            for (var i = 0; i < _pieces.Length; i += 1)
            {
                var piece = _pieces[i];
                var box = Box(piece.At);
                var color = piece.Crooked ? HudTheme.Alert
                          : piece.Placed ? HudTheme.Accent
                          : i == _held ? HudTheme.Heat
                          : HudTheme.Ink3;

                if (_snap > 0f)
                {
                    // 각을 실제로 돌려 그린다 — 숫자로 말하면 각이 안 읽힌다
                    var matrix = GUI.matrix;
                    GUIUtility.RotateAroundPivot(piece.Angle, box.center);
                    theme.Fill(box, HudTheme.Paper2);
                    theme.Border(box, color, 2f);
                    // 위쪽을 표시하는 눈금. 이게 없으면 90° 회전이 안 보인다
                    theme.Fill(new Rect(box.x + 6f, box.y + 6f, box.width - 12f, 5f), color);
                    GUI.matrix = matrix;
                }
                else
                {
                    theme.Fill(box, HudTheme.Paper2);
                    theme.Border(box, color, 2f);
                    theme.Fill(new Rect(box.x + 8f, box.center.y - 2f, box.width - 16f, 4f), color);
                }
            }

            theme.Border(body, HudTheme.Rule);
        }
    }
}
