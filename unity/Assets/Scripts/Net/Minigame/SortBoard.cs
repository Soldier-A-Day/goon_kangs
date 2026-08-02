using UnityEngine;

namespace SoldierADay.Net
{
    /// <summary>
    /// `SORT` 분류 — 5건. 정문 출입 · 의약품 선입선출 · 쓰레기 3분류 · 세탁물 ·
    /// 대민지원 구호품. 사용자 지시로 분류를 걷어내고 테트리스로 갈아 끼웠다.
    ///
    /// **판단할 것이 없어졌다.** 예전에는 물건 이름을 읽고 규정을 떠올려 통을
    /// 골랐다 — "젖은 종이는 종이인가 일반인가" 같은 것. 테트리스는 그 판단을
    /// 요구하지 않는다. 사용자가 원한 것은 분류라는 소재가 아니라 좌우로 옮기고
    /// 돌려서 줄을 채우는 그 조작 자체다.
    ///
    /// **서버 파라미터는 그대로다.** `quests.json`의 `bins`·`items`·`ambiguous`·
    /// `strikes`는 안 바뀐다(ARCH-02 — 서버 프로토콜은 클라가 못 건드린다).
    /// 다만 이 판은 그중 `bins`·`items`만 재해석해 쓴다 — `bins`는 우물의 폭,
    /// `items`는 지워야 할 줄 수로 다시 읽는다. `ambiguous`·`strikes`는 더는
    /// 쓰이지 않는다 — 애매한 물건도, 오답 한도도 테트리스에는 없는 개념이다.
    /// </summary>
    public sealed class SortBoard : Board
    {
        private struct Piece
        {
            public int Type; // 0..6 — I·O·T·S·Z·J·L
            public int Rot;  // 0..3
            public int X;    // 4×4 바운딩 박스 좌상단의 열
            public int Y;    // 4×4 바운딩 박스 좌상단의 행. -1이면 판 위 여유 칸에 걸쳐 있다
        }

        /// <summary>
        /// 세로 칸 수는 고정한다. 판의 몸통(`HudMinigame.PanelH` 620에서 머리띠·
        /// 시간줄·발판·여백을 뺀 약 436px)을 13으로 나누면 칸이 33px쯤 되어
        /// 1920 기준 화면에서 또렷이 보인다. 폭(7~9칸)이 늘어도 이 계산은 그대로
        /// 유효하다 — 넓어질 뿐 좁아지지 않는다.
        /// </summary>
        private const int Rows = 13;

        private const float MoveCooldown = 0.12f;
        private const float FlashDuration = 0.18f;

        // 4×4 박스 안의 스폰 좌표(SRS 스폰 배치를 그대로 빌렸다). O는 박스
        // 중심(1.5,1.5)에 대칭이라 회전해도 제자리다 — 그래서 O만 영원히
        // 안 도는 게 아니라, 도는데 결과가 같아 보이는 것뿐이다.
        private static readonly int[][] ShapeX =
        {
            new[] { 0, 1, 2, 3 }, // I
            new[] { 1, 2, 1, 2 }, // O
            new[] { 1, 0, 1, 2 }, // T
            new[] { 1, 2, 0, 1 }, // S
            new[] { 0, 1, 1, 2 }, // Z
            new[] { 0, 0, 1, 2 }, // J
            new[] { 2, 0, 1, 2 }, // L
        };

        private static readonly int[][] ShapeY =
        {
            new[] { 1, 1, 1, 1 }, // I
            new[] { 1, 1, 2, 2 }, // O
            new[] { 0, 1, 1, 1 }, // T
            new[] { 0, 0, 1, 1 }, // S
            new[] { 0, 0, 1, 1 }, // Z
            new[] { 0, 1, 1, 1 }, // J
            new[] { 0, 1, 1, 1 }, // L
        };

        // 새 자산을 만들지 않는다 — 기존 팔레트 7색을 7종에 그대로 물려 쓴다
        private static readonly Color[] PieceColors =
        {
            HudTheme.Accent, HudTheme.Cold, HudTheme.Heat, HudTheme.Alert,
            HudTheme.Optional, HudTheme.Admin, HudTheme.White,
        };

        private int[] _grid;
        private int _cols;
        private Piece _piece;

        private int[] _bag;
        private int _bagAt;

        private int _targetLines;
        private int _linesCleared;

        private float _fallTimer;
        private float _moveCooldown;
        private float _flashTimer;

        // 다른 판들은 `_area` 필드에 `body`를 담아 두고 Advance의 마우스 판정과
        // Draw가 같이 쓴다. 이 판은 마우스를 안 쓰니(조작이 키뿐이다) 그 필드가
        // 생기면 쓰는 곳 없이 값만 담기는 죽은 필드가 된다 — 대신 Draw 안에서
        // `body`를 바로 셀 크기 계산에 쓴다(관례의 취지는 그대로 따른 것이다).

        // Collides · RotatedCells가 프레임마다 여러 번 불려도 새로 할당하지
        // 않도록 미리 잡아 둔 스크래치 버퍼다
        private int[] _cellX;
        private int[] _cellY;

        public override string Instruction => "←→ 옮기고 눌러 돌려라 — 줄을 채워 지워라";

        public override string Status => $"줄 {_linesCleared}/{_targetLines}";

        protected override void Setup()
        {
            _cols = Mathf.Clamp(ParamInt("bins", 8), 7, 9);
            _grid = new int[_cols * Rows];
            _cellX = new int[4];
            _cellY = new int[4];

            var items = Mathf.Clamp(ParamInt("items", 14), 4, 24);
            // items(물건 수, 4~24)를 "지워야 할 줄 수"로 재해석한다. 한 줄
            // 채우는 데 물건 4개어치의 물량이 있다고 보고 items/4 — 2줄
            // 아래로는 첫 조각 몇 개로 우연히 깨지고, 6줄 위로는 배정된
            // 제한 시간 안에 못 끝낸다
            _targetLines = Mathf.Clamp(Mathf.RoundToInt(items / 4f), 2, 6);
            _linesCleared = 0;
            Fill = 0f;

            _bag = new int[7];
            _bagAt = _bag.Length; // 다음에 뽑을 때 바로 새 가방을 섞는다
            _fallTimer = 0f;
            _moveCooldown = 0f;
            _flashTimer = 0f;

            SpawnNext();
        }

        protected override void Advance(float dt, BoardInput input)
        {
            _flashTimer = Mathf.Max(0f, _flashTimer - dt);

            // 좌우 이동 — Horizontal을 매 프레임 그대로 쓰면 한 번 눌러도
            // 축이 안 돌아온 사이 몇 칸씩 밀린다. 뗀 순간 쿨다운을 0으로
            // 되돌려서 다음 탭은 바로 반응한다
            _moveCooldown -= dt;
            if (Mathf.Abs(input.Horizontal) > 0.5f)
            {
                if (_moveCooldown <= 0f)
                {
                    TryMove(input.Horizontal > 0f ? 1 : -1);
                    _moveCooldown = MoveCooldown;
                }
            }
            else
            {
                _moveCooldown = 0f;
            }

            // 회전 — Tap은 이미 단발이라(Board.cs) 따로 쿨다운이 필요 없다
            if (input.Tap) TryRotate();

            _fallTimer += dt;
            if (_fallTimer < FallInterval) return;
            _fallTimer = 0f;

            var dropped = _piece;
            dropped.Y += 1;
            if (Collides(dropped)) LockPiece();
            else _piece = dropped;
        }

        /// <summary>
        /// 낙하 간격. Difficulty로 기본을 조이고, 지운 줄 수만큼 지수로
        /// 줄인다 — 선형이 아니라서 초반 몇 줄은 완만하고 뒤로 갈수록
        /// 가팔라진다(상승 곡선). 바닥을 둬서 이동 쿨다운(0.12초)보다는
        /// 항상 여유가 남게 한다 — 그 아래로 가면 옮길 시간 자체가 없다
        /// </summary>
        private float FallInterval
        {
            get
            {
                var baseInterval = Mathf.Lerp(0.95f, 0.55f, (Difficulty - 1f) / 2f);
                var accel = Mathf.Pow(0.93f, _linesCleared);
                return Mathf.Max(0.22f, baseInterval * accel);
            }
        }

        private void RefillBag()
        {
            for (var i = 0; i < _bag.Length; i += 1) _bag[i] = i;
            // 피셔-예이츠 — 시드 고정 Rng라 같은 questId는 늘 같은 가방
            // 순서가 나온다(플레이어가 판을 익힐 수 있어야 한다, Board.cs §Rng)
            for (var i = _bag.Length - 1; i > 0; i -= 1)
            {
                var j = Rng.Next(i + 1);
                (_bag[i], _bag[j]) = (_bag[j], _bag[i]);
            }

            _bagAt = 0;
        }

        private int NextBagType()
        {
            if (_bagAt >= _bag.Length) RefillBag();
            return _bag[_bagAt++];
        }

        /// <summary>다음 조각 미리보기용 — 뽑지 않고 훔쳐본다. 가방이 비어 있으면
        /// 미리 섞어 두지만, 그건 `NextBagType`이 했을 섞기와 같은 결과라
        /// 실제로 뽑을 때 순서가 어긋나지 않는다</summary>
        private int PeekNextType()
        {
            if (_bagAt >= _bag.Length) RefillBag();
            return _bag[_bagAt];
        }

        private void SpawnNext()
        {
            var type = NextBagType();
            _piece = new Piece { Type = type, Rot = 0, X = (_cols - 4) / 2, Y = -1 };

            // 스폰 자리 자체가 막혀 있다 — 천장까지 찬 것이다. 규칙대로 즉시
            // 실패는 없다: 한 번 문책(Miss, 소리는 Board.Miss가 알아서 낸다)
            // 하고 판을 통째로 비워 다시 편다. 방금 비운 판이니 같은 자리에
            // 반드시 들어가서 재확인이 필요 없다
            if (Collides(_piece))
            {
                Miss();
                System.Array.Clear(_grid, 0, _grid.Length);
            }
        }

        private bool TryMove(int dx)
        {
            var moved = _piece;
            moved.X += dx;
            if (Collides(moved)) return false;
            _piece = moved;
            return true;
        }

        private void TryRotate()
        {
            var rotated = _piece;
            rotated.Rot = (rotated.Rot + 1) % 4;
            if (!Collides(rotated))
            {
                _piece = rotated;
                return;
            }

            // 벽에 끼면 한 칸 밀어내는 월킥 한 번만 시도한다 — SRS 전체를
            // 흉내 내지 않는다(조작 최소 원칙, §새 규칙 1). 그래도 안 들어가면
            // 회전을 포기한다 — 억지로 겹치게 두는 것보다 그게 낫다
            var kicked = rotated;
            kicked.X = rotated.X + 1;
            if (!Collides(kicked))
            {
                _piece = kicked;
                return;
            }

            kicked.X = rotated.X - 1;
            if (!Collides(kicked)) _piece = kicked;
        }

        private void LockPiece()
        {
            RotatedCells(_piece, _cellX, _cellY);
            for (var i = 0; i < 4; i += 1)
            {
                var gy = _piece.Y + _cellY[i];
                if (gy < 0) continue; // 판 위 여유 칸 — 기록할 자리가 없다
                _grid[gy * _cols + (_piece.X + _cellX[i])] = _piece.Type + 1;
            }

            Sfx.Play("tap");

            var cleared = ClearLines();
            if (cleared > 0)
            {
                _linesCleared += cleared;
                Fill = Mathf.Clamp01((float)_linesCleared / _targetLines);
                Sfx.Play("tap", 1f, 1.2f);
                if (_linesCleared >= _targetLines)
                {
                    Clear();
                    return;
                }
            }

            SpawnNext();
        }

        /// <summary>
        /// 꽉 찬 줄을 걷어내고 위 칸을 그만큼 내려 채운다. 아래에서부터 한
        /// 번만 훑는 압축 방식이라 한 번에 여러 줄이 지워져도(테트리스처럼)
        /// 따로 처리할 필요가 없다.
        ///
        /// 압축을 그 자리에서 끝내 버리기 때문에 "지워진 그 줄"의 화면
        /// 좌표가 남지 않는다 — 그래서 `Draw`의 플래시는 정확한 줄이 아니라
        /// 판 전체를 짧게 반짝인다(§새 규칙 6, 한 프레임 이상 유지).
        /// </summary>
        private int ClearLines()
        {
            var cleared = 0;
            var write = Rows - 1;

            for (var read = Rows - 1; read >= 0; read -= 1)
            {
                var full = true;
                for (var x = 0; x < _cols; x += 1)
                {
                    if (_grid[read * _cols + x] != 0) continue;
                    full = false;
                    break;
                }

                if (full)
                {
                    cleared += 1;
                    _flashTimer = FlashDuration;
                    continue; // 이 줄은 버린다 — write 자리는 그대로 둔다
                }

                if (write != read)
                {
                    for (var x = 0; x < _cols; x += 1)
                        _grid[write * _cols + x] = _grid[read * _cols + x];
                }

                write -= 1;
            }

            // 위로 밀려서 남는 자리는 새 줄로 비운다
            for (var y = write; y >= 0; y -= 1)
                for (var x = 0; x < _cols; x += 1)
                    _grid[y * _cols + x] = 0;

            return cleared;
        }

        private bool Collides(Piece p)
        {
            RotatedCells(p, _cellX, _cellY);
            for (var i = 0; i < 4; i += 1)
            {
                var gx = p.X + _cellX[i];
                var gy = p.Y + _cellY[i];
                if (gx < 0 || gx >= _cols || gy >= Rows) return true;
                if (gy < 0) continue; // 판 위 여유 칸 — 아직 아무것도 없다
                if (_grid[gy * _cols + gx] != 0) return true;
            }

            return false;
        }

        /// <summary>
        /// 4×4 박스 안에서 `p.Rot`번 시계 방향으로 돌린 네 칸을 `xs`·`ys`에
        /// 채운다. 회전 공식은 `(x,y) → (3-y, x)` — 박스 중심(1.5,1.5)을
        /// 축으로 하는 표준 변환이라, 4번 적용하면 원래 모양으로 돌아온다.
        /// 호출마다 배열을 새로 만들지 않도록 버퍼를 받아 채운다(Collides가
        /// 프레임마다 여러 번 부른다, GC 회피).
        /// </summary>
        private static void RotatedCells(Piece p, int[] xs, int[] ys)
        {
            for (var i = 0; i < 4; i += 1)
            {
                var x = ShapeX[p.Type][i];
                var y = ShapeY[p.Type][i];
                for (var r = 0; r < p.Rot; r += 1)
                {
                    var nx = 3 - y;
                    var ny = x;
                    x = nx;
                    y = ny;
                }

                xs[i] = x;
                ys[i] = y;
            }
        }

        public override void Draw(HudTheme theme, Rect body)
        {
            if (_grid == null) return;

            theme.Fill(body, HudTheme.Paper3);

            const float previewW = 160f;
            const float gap = 24f;
            var wellAreaW = body.width - previewW - gap;
            var cell = Mathf.Max(18f, Mathf.Min((body.height - 8f) / Rows, wellAreaW / _cols));
            var wellW = cell * _cols;
            var wellH = cell * Rows;
            var wellX = body.x + (wellAreaW - wellW) * 0.5f;
            var wellY = body.y + (body.height - wellH) * 0.5f;
            var well = new Rect(wellX, wellY, wellW, wellH);

            theme.Fill(well, HudTheme.Rule3);

            // 쌓인 칸
            for (var y = 0; y < Rows; y += 1)
            for (var x = 0; x < _cols; x += 1)
            {
                var v = _grid[y * _cols + x];
                if (v == 0) continue;
                var r = new Rect(well.x + x * cell, well.y + y * cell, cell - 1f, cell - 1f);
                theme.Fill(r, PieceColors[v - 1]);
            }

            // 떨어지는 조각
            RotatedCells(_piece, _cellX, _cellY);
            var pieceColor = PieceColors[_piece.Type];
            for (var i = 0; i < 4; i += 1)
            {
                var gx = _piece.X + _cellX[i];
                var gy = _piece.Y + _cellY[i];
                if (gy < 0) continue;
                var r = new Rect(well.x + gx * cell, well.y + gy * cell, cell - 1f, cell - 1f);
                theme.Fill(r, pieceColor);
                theme.Border(r, HudTheme.Ink, 1f);
            }

            theme.Border(well, HudTheme.Rule, 2f);

            // 줄 삭제 플래시 — 판 전체를 짧게 반짝인다(ClearLines 주석 참고)
            if (_flashTimer > 0f)
                theme.Fill(well, HudTheme.Accent, _flashTimer / FlashDuration * 0.35f);

            // 다음 조각
            var nextBox = new Rect(body.xMax - previewW, wellY, previewW, previewW);
            theme.Fill(nextBox, HudTheme.Paper2);
            theme.Border(nextBox, HudTheme.Rule2);
            GUI.Label(new Rect(nextBox.x, nextBox.y + 6f, nextBox.width, 22f), "다음",
                theme.At(theme.Small, 14, HudTheme.Ink3, TextAnchor.MiddleCenter));

            var previewType = PeekNextType();
            var previewCell = (nextBox.width - 40f) / 4f;
            var previewColor = PieceColors[previewType];
            for (var i = 0; i < 4; i += 1)
            {
                var px = ShapeX[previewType][i];
                var py = ShapeY[previewType][i];
                var r = new Rect(nextBox.x + 20f + px * previewCell, nextBox.y + 36f + py * previewCell,
                    previewCell - 1f, previewCell - 1f);
                theme.Fill(r, previewColor);
            }

            theme.Border(body, HudTheme.Rule);
        }
    }
}
