using System.Collections.Generic;
using UnityEngine;

namespace SoldierADay.Net
{
    /// <summary>
    /// `PLACE` 정돈 · 배치 — 10건. 가장 많이 쓰이는 원형이다.
    ///
    /// ── 왜 전면 교체했나(A-5 진단 · 사용자 결정) ────────────────────────────
    /// 예전 판은 실루엣 자리로 물건을 끌어다 놓는 방식이었는데, 조각과 슬롯이
    /// 전부 같은 정사각형(`Size` 54px)이었다. 그러면 "보고 판단"할 것이
    /// 없다 — 아무 자리에나 끌어다 대보면 맞는지 틀리는지가 시행착오로만
    /// 드러난다. 사용자 결정은 명확했다: **타이밍 맞춰 날아오는 타일을 점점
    /// 위로 쌓는 것** — Stack 계열 타워 쌓기다. 조작이 단발 하나(`input.Tap`)로
    /// 줄고, 대신 "지금이 몇 시인가"가 아니라 "지금 폭이 얼마나 남았나"를
    /// 매 순간 눈으로 재야 하는 판이 됐다.
    ///
    /// ── 규칙 한 줄 ───────────────────────────────────────────────────────
    /// 첫 타일은 바닥 중앙에 이미 놓여 있다. 다음 타일이 탑 꼭대기 한 층
    /// 위에서 좌우로 미끄러지고, 탭하면 짧게 떨어져 앉는다. 아래 타일과
    /// 완벽히 겹치면 그대로, 부분만 겹치면 삐져나온 만큼 폭이 잘려 나가며,
    /// 아예 안 겹치면 실수(`Miss`)이거나 — 극성(`snapDeg 180`)이면 되돌릴 수
    /// 없는 실패(`Fail`)다.
    ///
    /// ── 파라미터 재해석(프로토콜·quests.json은 그대로) ──────────────────────
    /// `pieces`는 쌓을 총 층수(바닥 타일 포함)다. `tolerance`는 완벽 창의
    /// 폭과 잘림의 관대함을 같이 정한다 — 값이 클수록 두 쪽 다 후해진다.
    /// `snapDeg`는 더 이상 각을 재지 않는다. 대신 "완전히 빗나갔을 때"의
    /// 심각도만 가른다 — 0 · 90은 다시 시도(`Miss`), 180은 무전기 배터리
    /// 극성처럼 그 자리에서 끝난다(`Fail`). `GradesOnTimeout`은 그 축과
    /// 그대로 묶여 있다 — 극성·각 변형은 압박이 본질이라 시간초과가 여전히
    /// 실패고, 자리만 맞추면 되는 0 변형만 시간이 다 돼도 그때까지 쌓은
    /// 높이로 통과한다(A-5 1차 결정 존중).
    /// </summary>
    public sealed class PlaceBoard : Board
    {
        private struct Tile
        {
            public float CenterX;
            public float Width;
        }

        /// <summary>삐져나와 떨어져 나가는 조각 — 폭이 줄어든 이유를 눈으로 보여주는 연출용</summary>
        private struct Scrap
        {
            public float CenterX;
            public float Width;
            public int Row;
            public float Age;
        }

        /// <summary>첫 타일 폭 — 판 너비의 몇 %로 시작하는가</summary>
        private const float BaseWidthRatio = 0.46f;

        /// <summary>타일 폭 하한(px). 이 밑으로는 안 좁아진다 — 영영 못 끝내는 잠금 방지</summary>
        private const float MinWidthPx = 20f;

        /// <summary>탭한 뒤 탑 위에 앉기까지의 낙하 연출 시간(초)</summary>
        private const float DropDuration = 0.08f;

        /// <summary>완벽 착지 강조가 유지되는 시간(초)</summary>
        private const float PerfectFlashDuration = 0.3f;

        /// <summary>잘려나간 조각이 보이는 시간(초) — 한 프레임이라도 보이면 되므로 짧다</summary>
        private const float ScrapLifetime = 0.3f;

        /// <summary>슬라이드 기본 속도 — 판 너비 대비 초당 비율</summary>
        private const float SlideSpeedNorm = 0.30f;

        /// <summary>쌓일수록 붙는 속도 가산 — "상승 곡선"의 기울기</summary>
        private const float RampPerPiece = 0.07f;

        private List<Tile> _tower;
        private List<Scrap> _scraps;

        /// <summary>쌓을 총 층수(바닥 타일 포함) — Fill의 분모</summary>
        private int _count;
        /// <summary>지금까지 쌓은 층수 — 바닥 타일이 1로 시작한다</summary>
        private int _placed;
        private float _snap;
        private float _tolerance;
        private Rect _area;
        /// <summary>`_area`가 처음 채워진 뒤에야 픽셀 단위 레이아웃을 잡을 수 있다(관례 §9)</summary>
        private bool _initialized;

        /// <summary>지금 미끄러지는 타일의 중심 x(px, `_area` 기준 로컬 좌표)</summary>
        private float _curX;
        /// <summary>지금 다루는 타일의 폭(px) — 잘릴 때마다 줄고, 다음 타일도 이 폭에서 시작한다</summary>
        private float _curWidth;
        private float _dir;
        /// <summary>0 이상이면 낙하 연출 중. -1은 슬라이드 중</summary>
        private float _dropT = -1f;
        /// <summary>낙하가 끝나면 `_tower`에 편입될 타일</summary>
        private Tile _pending;

        /// <summary>완벽 착지 강조 — -1이면 꺼져 있다</summary>
        private float _perfectFlashAge = -1f;
        private int _perfectFlashRow = -1;

        public override string Instruction => "타이밍에 맞춰 눌러 쌓아라 — 어긋난 만큼 좁아진다";

        public override string Status =>
            $"{_placed}/{_count} 쌓음" + (Mistakes > 0 ? $"  ·  실수 {Mistakes}" : "");

        /// <summary>
        /// 자리만 맞추면 되는 변형(`snapDeg` 0)만 시간초과로 통과한다.
        ///
        /// 극성(180)·정렬(90) 변형은 다르다 — 완전히 빗나갔을 때의 대가(즉시
        /// 실패 혹은 재시도)가 이 변형의 본질이라 시간이 압박으로 남아야
        /// 한다. `_snap` 필드는 옛 각도 스냅과 이름만 같을 뿐 지금은 "완전히
        /// 빗나간 순간의 심각도"를 고르는 값이라는 것이 바뀐 점이다.
        /// </summary>
        protected override bool GradesOnTimeout => _snap <= 0f;

        protected override void Setup()
        {
            _count = Mathf.Clamp(ParamInt("pieces", 6), 2, 14);
            _snap = Param("snapDeg", 0f);
            _tolerance = Mathf.Clamp(Param("tolerance", 0.12f), 0.04f, 0.3f);

            _placed = 0;
            _initialized = false;
            _tower = new List<Tile>(_count);
            _scraps = new List<Scrap>();
            _dropT = -1f;
            _perfectFlashAge = -1f;
            _perfectFlashRow = -1;
        }

        /// <summary>
        /// `_area`가 처음 알려진 프레임에 한 번만 돈다.
        ///
        /// `Setup`은 `Draw`가 판 크기를 알려주기 전에 불려서 픽셀 폭을 잡을 수
        /// 없다(관례 §9 — 원래 `PlaceBoard`도 `Slot`을 비율로만 두고 `Draw`가
        /// 돈 뒤에야 `Point()`로 픽셀화했다). 첫 타일을 바닥 중앙에 실제
        /// 픽셀 폭으로 앉히고, 두 번째 타일을 미끄러뜨리기 시작하는 것도 여기서 한다.
        /// </summary>
        private void InitLayout()
        {
            _initialized = true;

            var baseWidth = Mathf.Max(MinWidthPx, _area.width * BaseWidthRatio);
            _tower.Add(new Tile { CenterX = _area.width * 0.5f, Width = baseWidth });
            _curWidth = baseWidth;
            _placed = 1;

            SpawnNextTile();
        }

        /// <summary>
        /// 다음 타일을 탑 꼭대기 한 층 위에서 미끄러지게 한다.
        ///
        /// 시작 방향과 위상은 `Rng`에서 뽑는다(§8) — 같은 questId면 항상 같은
        /// 지점 · 같은 방향에서 시작해 같은 판이 나온다. 매번 왼쪽 끝에서만
        /// 시작하면 곧바로 패턴이 읽혀 재미가 없어, 시작 위치도 같이 흔든다.
        /// </summary>
        private void SpawnNextTile()
        {
            _dir = Rng.Next(2) == 0 ? 1f : -1f;
            var half = _curWidth * 0.5f;
            var span = Mathf.Max(0f, _area.width - _curWidth);
            _curX = half + (float)Rng.NextDouble() * span;
            _dropT = -1f;
        }

        protected override void Advance(float dt, BoardInput input)
        {
            // 강조·조각 낙하 시계는 판정과 무관하게 계속 흘러야 한다(ScrubBoard의
            // 얼룩 강조·김 서림 시계와 같은 이유) — 아래 이른 return과 무관하게 먼저 갱신한다
            TickScraps(dt);
            TickPerfectFlash(dt);

            if (_area.width <= 0f || _tower == null) return;
            if (!_initialized) InitLayout();
            if (_tower.Count == 0) return;

            if (_dropT >= 0f)
            {
                _dropT += dt;
                if (_dropT >= DropDuration) CommitDrop();
            }
            else if (_placed < _count)
            {
                var half = _curWidth * 0.5f;
                // 난이도가 오르고 탑이 높아질수록 빠르다 — 상승 곡선이 곧 이 판의 긴장이다
                var speed = _area.width * SlideSpeedNorm * Difficulty * (1f + _placed * RampPerPiece);
                _curX += _dir * speed * dt;

                var minX = half;
                var maxX = _area.width - half;
                if (_curX < minX) { _curX = minX; _dir = 1f; }
                else if (_curX > maxX) { _curX = maxX; _dir = -1f; }

                if (input.Tap) TryDrop();
            }

            Fill = _count <= 0 ? 1f : (float)_placed / _count;
        }

        /// <summary>
        /// 지금 위치에서 손을 뗀다 — 겹침 판정 전부가 여기서 갈린다.
        ///
        /// `tolerance`가 두 가지를 동시에 정한다. 완벽 창(그대로 앉는 허용
        /// 오차)과 잘림 관대함(삐져나와도 얼마나 덜 잘리는가)이다 — 값이
        /// 클수록 두 쪽 다 후하다. 창을 벗어나도 조금이라도 겹쳤으면 그
        /// 겹친 만큼(+관대함)이 다음 타일의 새 폭이 된다 — Stack 특유의
        /// "깎이며 이어지는" 손맛이다.
        /// </summary>
        private void TryDrop()
        {
            var below = _tower[_tower.Count - 1];
            var halfCur = _curWidth * 0.5f;
            var left = _curX - halfCur;
            var right = _curX + halfCur;
            var belowLeft = below.CenterX - below.Width * 0.5f;
            var belowRight = below.CenterX + below.Width * 0.5f;

            var overlapLeft = Mathf.Max(left, belowLeft);
            var overlapRight = Mathf.Min(right, belowRight);
            var overlapWidth = overlapRight - overlapLeft;

            if (overlapWidth <= 0f)
            {
                // 완전히 빗나갔다 — 극성(180)만 되돌릴 수 없는 실수로 취급한다.
                // 90 · 0은 같은 타일이 같은 폭으로 다시 날아온다(규칙 §3)
                if (_snap >= 180f) { Fail(); return; }
                Miss();
                SpawnNextTile();
                return;
            }

            var perfectWindow = _tolerance * _curWidth;
            var misalign = _curWidth - overlapWidth;
            var perfect = misalign <= perfectWindow;

            float landedWidth;
            float landedCenter;

            if (perfect)
            {
                // 완벽 창 안 — 폭 손실 없이 그대로 앉는다. 아래 타일 경계 안쪽으로만
                // 살짝 당겨 붙인다(허용 오차만큼의 오차가 시각적으로 튀어나오지 않게)
                landedWidth = _curWidth;
                landedCenter = Mathf.Clamp(_curX, belowLeft + landedWidth * 0.5f, belowRight - landedWidth * 0.5f);
            }
            else
            {
                // 창 밖 — 삐져나온 만큼 잘린다. `grace`만큼은 tolerance가 봐준다
                var grace = _tolerance * _curWidth * 0.5f;
                landedWidth = Mathf.Max(MinWidthPx, Mathf.Min(_curWidth, overlapWidth + grace));
                var center = (overlapLeft + overlapRight) * 0.5f;
                landedCenter = Mathf.Clamp(center, belowLeft + landedWidth * 0.5f, belowRight - landedWidth * 0.5f);

                SpawnScraps(left, right, landedCenter - landedWidth * 0.5f, landedCenter + landedWidth * 0.5f,
                    _tower.Count);
            }

            _pending = new Tile { CenterX = landedCenter, Width = landedWidth };
            _dropT = 0f;
            // 다음 타일도 이 폭에서 시작한다 — 좁아진 채로 계속 이어진다
            _curWidth = landedWidth;

            if (perfect)
            {
                _perfectFlashAge = 0f;
                _perfectFlashRow = _tower.Count;
                Sfx.Play("tap", 1f, 1.2f);
            }
            else
            {
                // 잘렸다는 것이 소리로도 구분되게 낮은 피치로 튕긴다
                Sfx.Play("tap", 1f, 0.75f);
            }
        }

        /// <summary>낙하 연출이 끝난 타일을 탑에 편입한다</summary>
        private void CommitDrop()
        {
            _dropT = -1f;
            _tower.Add(_pending);
            _placed += 1;

            if (_placed >= _count)
            {
                Clear();
                return;
            }

            SpawnNextTile();
        }

        /// <summary>
        /// 삐져나온 조각을 짧게 보여준다. 판정 자체(폭이 줄어드는 것)는 이미
        /// `TryDrop`에서 끝났고, 이건 순전히 "잘려 나갔다"가 눈에 보이기
        /// 위한 연출이다 — 폭이 왜 줄었는지가 숫자가 아니라 장면으로 읽혀야 한다.
        /// </summary>
        private void SpawnScraps(float left, float right, float landedLeft, float landedRight, int row)
        {
            if (left < landedLeft - 0.5f)
            {
                var w = landedLeft - left;
                _scraps.Add(new Scrap { CenterX = left + w * 0.5f, Width = w, Row = row, Age = 0f });
            }
            if (right > landedRight + 0.5f)
            {
                var w = right - landedRight;
                _scraps.Add(new Scrap { CenterX = landedRight + w * 0.5f, Width = w, Row = row, Age = 0f });
            }
        }

        private void TickScraps(float dt)
        {
            for (var i = _scraps.Count - 1; i >= 0; i -= 1)
            {
                var s = _scraps[i];
                s.Age += dt;
                if (s.Age >= ScrapLifetime) { _scraps.RemoveAt(i); continue; }
                _scraps[i] = s;
            }
        }

        private void TickPerfectFlash(float dt)
        {
            if (_perfectFlashAge < 0f) return;
            _perfectFlashAge += dt;
            if (_perfectFlashAge >= PerfectFlashDuration) _perfectFlashAge = -1f;
        }

        private float RowTopLocal(int row, float rowH) => _area.height - (row + 1) * rowH;

        private Rect TileRect(Tile tile, int row, float rowH) => new Rect(
            _area.x + tile.CenterX - tile.Width * 0.5f,
            _area.y + RowTopLocal(row, rowH),
            tile.Width,
            rowH - 2f); // 1px씩 여백을 둬 층이 눈으로 갈린다

        public override void Draw(HudTheme theme, Rect body)
        {
            _area = body;
            if (_tower == null) return;

            theme.Fill(body, HudTheme.Paper3);

            // 타일 높이 = 판 높이 / (pieces + 2) — 다 쌓아도 스크롤 없이 들어가고,
            // 위에 두 층만큼 미끄러지는 타일의 여유가 남는다(규칙 §9)
            var rowH = _area.height / (_count + 2);

            for (var i = 0; i < _tower.Count; i += 1)
            {
                var tile = _tower[i];
                var rect = TileRect(tile, i, rowH);
                var accent = _perfectFlashAge >= 0f && _perfectFlashRow == i;

                theme.Fill(rect, HudTheme.Paper2);
                theme.Border(rect, accent ? HudTheme.Accent : HudTheme.Rule2, accent ? 3f : 2f);
                if (accent)
                {
                    var t = 1f - Mathf.Clamp01(_perfectFlashAge / PerfectFlashDuration);
                    theme.Fill(rect, HudTheme.Accent, 0.35f * t);
                }
            }

            // 잘려나간 조각 — 떨어지며 옅어진다
            for (var i = 0; i < _scraps.Count; i += 1)
            {
                var s = _scraps[i];
                var t = Mathf.Clamp01(s.Age / ScrapLifetime);
                var rect = new Rect(
                    _area.x + s.CenterX - s.Width * 0.5f,
                    _area.y + RowTopLocal(s.Row, rowH) + t * rowH * 1.6f,
                    s.Width, rowH - 2f);
                theme.Fill(rect, HudTheme.Alert, 0.5f * (1f - t));
            }

            // 지금 미끄러지는(또는 막 떨어지는) 타일
            if (_initialized && _placed < _count)
            {
                var row = _tower.Count;
                var isPerfectFlash = _perfectFlashAge >= 0f && _perfectFlashRow == row;

                Rect rect;
                if (_dropT >= 0f)
                {
                    var t = Mathf.Clamp01(_dropT / DropDuration);
                    var eased = 1f - (1f - t) * (1f - t);
                    var raise = Mathf.Min(rowH * 0.6f, 24f) * (1f - eased);
                    rect = TileRect(_pending, row, rowH);
                    rect.y -= raise;
                }
                else
                {
                    rect = TileRect(new Tile { CenterX = _curX, Width = _curWidth }, row, rowH);
                }

                theme.Fill(rect, isPerfectFlash ? HudTheme.Accent : HudTheme.Heat);
                theme.Border(rect, HudTheme.Ink, 2f);
            }

            theme.Border(body, HudTheme.Rule);
        }
    }
}
