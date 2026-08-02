using System.Collections.Generic;
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
    /// ── 왜 이진 칸을 층으로 바꿨나(A-5 1차 · SCRUB 개편 S2) ────────────────
    /// 전수 플레이 평가가 "너무 진부하다"였다. 칸이 더럽다/깨끗하다 둘뿐이면
    /// 한 번 스치기만 해도 끝이라 문지르는 손맛이 없다. 파워워시 시뮬레이터가
    /// 준 답은 **벗겨지는 층**이다 — 칸마다 남은 층수(`_layers`)를 두고, 한
    /// 스트로크 안에서는 같은 칸이 한 층만 벗겨지게 한다(마우스를 떼거나 그
    /// 칸을 벗어났다 돌아오면 다음 층). 덩어리 중심은 두껍고 가장자리는
    /// 얇다 — 진짜 얼룩처럼 자연스러운 그러데이션을 준다.
    ///
    /// 덩어리(stain) 단위도 따로 기억해 둔다. 한 덩어리의 마지막 층이
    /// 벗겨지는 순간 그 자리를 짧게 번쩍이고 탭 소리를 낸다 — 완료가 켜켜이
    /// 쌓이는 진행률 숫자가 아니라 **사건**으로 느껴져야 한다.
    ///
    /// 등급의 실수 축이 곧 낭비였지만, 그건 `supply`(약제 잔량)가 있는
    /// 변형(모래·방역)에만 맞는 얘기다. 순수 청소(바닥·변기칸·거울·컨베이어·
    /// 세차)에서 이미 깨끗한 자리를 또 문지른다고 벌점을 주면, 파워워시가
    /// 가르쳐준 "청소는 벌점 없는 젠이어야 한다"는 원칙에 어긋난다.
    /// </summary>
    public sealed class ScrubBoard : Board
    {
        private const float Cell = 20f;

        /// <summary>덩어리 완료 강조가 유지되는 시간(초) — 번쩍이고 사라진다</summary>
        private const float StainFlashDuration = 0.35f;

        /// <summary>거울 김 서림 — 이 정도까지 닦으면 트위스트가 걸릴 자격이 생긴다</summary>
        private const float FogTriggerFill = 0.8f;

        /// <summary>김 서림 예고 깜빡임이 실제로 덮기까지 보장하는 시간(초)</summary>
        private const float FogWarnDuration = 0.5f;

        private int _cols;
        private int _rows;

        /// <summary>칸마다 남은 층수. 0이면 처음부터 안 더럽거나 다 벗겨진 칸이다</summary>
        private byte[] _layers;
        /// <summary>칸마다 원래 층수 — Draw에서 "얼마나 벗겨졌나"의 분모로 쓴다</summary>
        private byte[] _maxLayers;
        /// <summary>칸이 속한 덩어리 인덱스. -1이면 애초에 오염이 없던 칸</summary>
        private int[] _cellStain;

        /// <summary>이번 프레임에 브러시가 닿은 칸</summary>
        private bool[] _touching;
        /// <summary>직전 활성 프레임에 브러시가 닿았던 칸 — 이게 있어야 "새로 들어왔다"를 안다</summary>
        private bool[] _touchingPrev;

        private int _stainCount;
        /// <summary>덩어리별 소속 칸 목록. 완료 강조 범위와 재오염 대상 선정에 쓴다</summary>
        private List<int>[] _stainCells;
        /// <summary>덩어리별 남은 층수 합. 0이 되는 순간이 "그 덩어리를 다 닦은 순간"이다</summary>
        private int[] _stainRemaining;
        /// <summary>덩어리별 강조 경과 시간. 음수면 강조 중이 아니다</summary>
        private float[] _stainFlashAge;
        private int[] _stainMinX;
        private int[] _stainMaxX;
        private int[] _stainMinY;
        private int[] _stainMaxY;
        /// <summary>이번 프레임에 마지막 층이 벗겨진 덩어리들 — 재사용 버퍼라 매 프레임 할당하지 않는다</summary>
        private readonly List<int> _stainCompletedThisFrame = new List<int>();

        /// <summary>전체 층수(분모). 오염 총량이 아니라 "벗겨야 할 겹의 총합"이다</summary>
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

        /// <summary>
        /// H-4 — 브러시 자리를 마우스가 아니라 가상 커서에서 읽는다.
        ///
        /// 마우스로 문지를 때는 `Norm`이 매 프레임 마우스 좌표로 스냅되니
        /// 동작이 예전과 같다. `Space`로 누른 채(`input.Hold`) 화살표·WASD로
        /// 커서를 움직이면 그게 곧 문지르는 손이 된다 — 드래그 하나뿐인
        /// 조작에 손을 하나 더 얹은 것이 아니라, 같은 손을 다른 방법으로
        /// 쥔 것이다.
        /// </summary>
        private readonly VirtualCursor _cursor = new VirtualCursor();

        /* ───────────────────────────────────────────── 세면장(mirror) 김 서림 */

        /// <summary>재오염을 이미 시도했는가 — 성공 여부와 무관하게 판당 한 번뿐이다</summary>
        private bool _fogAttempted;
        /// <summary>예고 깜빡임 중인 덩어리. -1이면 예고 중이 아니다</summary>
        private int _fogWarnStain = -1;
        private float _fogWarnT;

        public override string Instruction =>
            _supplyMax > 0f
                ? "끌어서 문질러라(WASD+Space) — 약제가 모자란다"
                : "끌어서 문질러라 — 화살표+Space도 된다";

        public override string Status
        {
            get
            {
                var left = RemainingCells();
                var text = _supplyMax > 0f
                    ? $"남은 오염 {left}칸  ·  약제 {Mathf.RoundToInt(_supply / _supplyMax * 100f)}%"
                    : $"남은 오염 {left}칸";
                // 몰래 덮으면 배신이 아니라 억울함이다 — 예고가 뜨는 동안은 글로도 읽혀야 한다
                if (_fogWarnStain >= 0) text += "  ·  김이 서리려 한다";
                return text;
            }
        }

        /// <summary>
        /// 시간초과 통과 계약(A-5 1차 S1↔S2).
        ///
        /// `Board.cs`에 `protected virtual bool GradesOnTimeout => false;`와
        /// `public bool TimedOut`을 병렬 발주 S1이 추가한다. SCRUB은 시간이
        /// 다 되어도 그때까지 벗긴 층만큼을 등급에 반영해야 하므로 켠다.
        /// 이 워크트리에는 베이스 멤버가 아직 없어 컴파일이 안 맞는 것이
        /// 정상이다 — 병합 후 맞물린다.
        /// </summary>
        protected override bool GradesOnTimeout => true;

        protected override void Setup()
        {
            // 판 크기는 `Draw`가 처음 도는 순간에야 알 수 있다. 격자는 고정
            // 해상도로 잡아두고 그릴 때 늘린다 — 그래야 같은 일과가 창 크기와
            // 무관하게 같은 오염 배치를 낸다
            _cols = 34;
            _rows = 18;
            var cellCount = _cols * _rows;

            _layers = new byte[cellCount];
            _maxLayers = new byte[cellCount];
            _cellStain = new int[cellCount];
            for (var i = 0; i < cellCount; i += 1) _cellStain[i] = -1;

            _touching = new bool[cellCount];
            _touchingPrev = new bool[cellCount];

            var stainCount = ParamInt("stains", 6);
            var radius = Mathf.Lerp(3.2f, 2.2f, (Difficulty - 1f) * 0.5f);
            // 난이도 1은 두 겹까지만 — 처음 잡는 판은 끝이 금방 보여야 한다.
            // 2~3은 세 겹 — 문질러도 문질러도 나오는 것이 손맛이 된다.
            var maxLayer = Difficulty <= 1f ? 2 : 3;

            _stainCount = stainCount;
            _stainCells = new List<int>[stainCount];
            _stainRemaining = new int[stainCount];
            _stainFlashAge = new float[stainCount];
            _stainMinX = new int[stainCount];
            _stainMaxX = new int[stainCount];
            _stainMinY = new int[stainCount];
            _stainMaxY = new int[stainCount];

            for (var s = 0; s < stainCount; s += 1)
            {
                _stainCells[s] = new List<int>();
                _stainFlashAge[s] = -1f;
                _stainMinX[s] = _cols;
                _stainMaxX[s] = -1;
                _stainMinY[s] = _rows;
                _stainMaxY[s] = -1;

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
                    var distSq = dx * dx + dy * dy;
                    if (distSq > r * r) continue;

                    // 중심부는 두껍고 가장자리는 얇다 — 원 하나를 균일하게 채우면
                    // 얼룩이 아니라 스티커로 보인다. 0(중심)~1(가장자리)로 정규화해
                    // 층수를 선형으로 얇게 만든다
                    var t = r <= 0f ? 0f : Mathf.Sqrt(distSq) / r;
                    var layer = (byte)Mathf.Clamp(Mathf.CeilToInt(maxLayer * (1f - t)), 1, maxLayer);

                    var i = y * _cols + x;
                    // 덩어리끼리 겹치면 더 두꺼운 쪽이 이긴다 — 나중에 그려진 얇은
                    // 덩어리가 먼저 그려진 두꺼운 자리를 깎아내리지 않는다
                    if (layer <= _maxLayers[i]) continue;

                    _maxLayers[i] = layer;
                    _layers[i] = layer;
                    _cellStain[i] = s;
                }
            }

            // 소속이 겹침 판정으로 도중에 바뀔 수 있어 확정된 뒤에 한 번 더 훑어
            // 덩어리별 칸 목록·경계·총 층수를 모은다
            _total = 0;
            var dirtyCells = 0;
            for (var i = 0; i < cellCount; i += 1)
            {
                _total += _maxLayers[i];
                if (_maxLayers[i] > 0) dirtyCells += 1;

                var s = _cellStain[i];
                if (s < 0) continue;
                var x = i % _cols;
                var y = i / _cols;
                _stainCells[s].Add(i);
                _stainRemaining[s] += _maxLayers[i];
                if (x < _stainMinX[s]) _stainMinX[s] = x;
                if (x > _stainMaxX[s]) _stainMaxX[s] = x;
                if (y < _stainMinY[s]) _stainMinY[s] = y;
                if (y > _stainMaxY[s]) _stainMaxY[s] = y;
            }
            _done = 0;

            var supply = Param("supply", 0f);
            if (supply > 0f)
            {
                // 잔량은 **오염 칸 수 기준으로 정확히 덮는 데 필요한 거리**의
                // 배수다. 층 개수가 아니라 칸 개수로 재는 이유는, 잔량이 브러시가
                // 실제로 이동한 거리로 깎이기 때문이다 — 같은 칸을 여러 겹
                // 벗기더라도 손이 그 자리를 다시 지나가야만 소모된다
                _supplyMax = dirtyCells * Cell * 0.55f * supply;
                _supply = _supplyMax;
            }
        }

        protected override void Advance(float dt, BoardInput input)
        {
            // 강조·예고 시계는 마우스를 안 눌러도 흘러야 한다 — 아래 이른
            // return과 무관하게 먼저 갱신한다
            UpdateStainFlashes(dt);
            UpdateFog(dt);

            if (_area.width <= 0f) return;

            _cursor.Tick(dt, input, _area);

            // `input.Hold` = 마우스 down 또는 Space held(Board.cs §BoardInput) —
            // 어느 쪽으로 쥐었든 "지금 문지르고 있다"는 같은 뜻이다
            if (!input.Hold)
            {
                _dragging = false;
                // 스트로크가 끊겼다 — 다음에 브러시가 어느 칸에 들어오든
                // "새로 들어온 것"으로 쳐서 그 칸을 다시 벗길 수 있게 한다
                System.Array.Clear(_touchingPrev, 0, _touchingPrev.Length);
                return;
            }

            var brushPos = _cursor.Screen(_area);
            var moved = _dragging ? Vector2.Distance(brushPos, _last) : 0f;
            _last = brushPos;
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

            var minX = Mathf.Max(0, Mathf.FloorToInt((brushPos.x - brush - _area.x) / scaleX));
            var maxX = Mathf.Min(_cols - 1, Mathf.CeilToInt((brushPos.x + brush - _area.x) / scaleX));
            var minY = Mathf.Max(0, Mathf.FloorToInt((brushPos.y - brush - _area.y) / scaleY));
            var maxY = Mathf.Min(_rows - 1, Mathf.CeilToInt((brushPos.y + brush - _area.y) / scaleY));

            System.Array.Clear(_touching, 0, _touching.Length);

            var touched = 0;
            var useful = 0;

            for (var y = minY; y <= maxY; y += 1)
            for (var x = minX; x <= maxX; x += 1)
            {
                var cx = _area.x + (x + 0.5f) * scaleX;
                var cy = _area.y + (y + 0.5f) * scaleY;
                var dx = cx - brushPos.x;
                var dy = cy - brushPos.y;
                if (dx * dx + dy * dy > brush * brush) continue;

                touched += 1;
                var i = y * _cols + x;
                _touching[i] = true;

                if (_layers[i] == 0) continue; // 처음부터 안 더럽거나 이미 다 벗겨졌다 — 헛손질
                useful += 1;

                // **한 스트로크에 한 층뿐이다.** 이 칸이 직전 활성 프레임에도
                // 눌려 있었다면 계속 문지르는 중이라는 뜻이라 더 안 벗긴다.
                // 손을 떼거나 이 칸을 벗어났다 돌아와야(위의 `_touchingPrev`
                // 초기화) 다음 층이 벗겨진다
                if (_touchingPrev[i]) continue;

                _layers[i] -= 1;
                _done += 1;

                var stainIdx = _cellStain[i];
                if (stainIdx < 0) continue;
                _stainRemaining[stainIdx] -= 1;
                if (_stainRemaining[stainIdx] == 0) _stainCompletedThisFrame.Add(stainIdx);
            }

            var swapBuf = _touchingPrev;
            _touchingPrev = _touching;
            _touching = swapBuf;

            Fill = _total == 0 ? 1f : (float)_done / _total;

            // 낭비 벌점은 **약제가 있는 변형(모래·방역)에서만** 유지한다. 순수
            // 청소(바닥·변기칸·거울·컨베이어·세차)는 이미 깨끗한 자리를 또
            // 문질러도 벌점이 없다 — 청소는 벌점 없는 젠이어야 한다는 것이
            // 파워워시 시뮬레이터가 준 교훈이다
            if (_supplyMax > 0f && touched > 0)
            {
                _strokes += touched;
                _waste += touched - useful;

                // 낭비가 쌓이면 실수로 센다. 두 번까지만 — 그 아래는 C를 넘지 않는다
                var wasteRatio = _strokes <= 0f ? 0f : _waste / _strokes;
                var expected = wasteRatio > 0.75f ? 2 : wasteRatio > 0.55f ? 1 : 0;
                while (Mistakes < expected && Mistakes < 2) Miss();
            }

            if (_stainCompletedThisFrame.Count > 0)
            {
                var aboutToClear = Fill >= Param("coverage", 0.9f);
                for (var k = 0; k < _stainCompletedThisFrame.Count; k += 1)
                {
                    _stainFlashAge[_stainCompletedThisFrame[k]] = 0f;
                }
                // 이번 프레임에 전체 클리어까지 같이 터지면 성공 사운드가 대신
                // 울린다 — 탭 소리를 겹쳐 울리지 않는다. 강조는 그대로 보인다
                if (!aboutToClear) Sfx.Play("tap");
                _stainCompletedThisFrame.Clear();
            }

            if (Fill >= Param("coverage", 0.9f)) Clear();
        }

        private void UpdateStainFlashes(float dt)
        {
            for (var s = 0; s < _stainCount; s += 1)
            {
                if (_stainFlashAge[s] < 0f) continue;
                _stainFlashAge[s] += dt;
                if (_stainFlashAge[s] >= StainFlashDuration) _stainFlashAge[s] = -1f;
            }
        }

        /// <summary>
        /// 세면장(mirror)만의 트위스트 — 다 닦은 거울에 김이 다시 서린다.
        ///
        /// **트위스트의 철칙**: 이미 보여준 규칙 안의 배신만 재미고, 몰래 넣으면
        /// 억울함이다. 그래서 실제로 덮기 0.5초 전부터 대상 덩어리를 깜빡여
        /// 예고하고, 그 뒤에야 1층을 되돌린다. 판당 한 번뿐이고, 시드 고정
        /// `Rng`로 대상을 고른다 — 같은 일과는 재시도해도 같은 덩어리가,
        /// 같은 타이밍에 다시 흐려진다.
        /// </summary>
        private void UpdateFog(float dt)
        {
            if (Spec?.variant != "mirror" || _fogAttempted) return;

            if (_fogWarnStain >= 0)
            {
                _fogWarnT += dt;
                if (_fogWarnT >= FogWarnDuration) ApplyFog(_fogWarnStain);
                return;
            }

            if (Fill < FogTriggerFill) return;
            // 시도는 이번 한 번뿐이다 — 대상을 못 찾아도 다시 훑지 않는다.
            // 그래야 언제 소모되는지가 프레임 타이밍이 아니라 항상 같은 지점에서 정해진다
            _fogAttempted = true;

            // 이미 다 닦인 덩어리 중 하나를 저수지 표집으로 고른다 — 개수를 몰라도
            // 한 번 훑으면서 균등하게 뽑을 수 있다
            var pick = -1;
            var seen = 0;
            for (var s = 0; s < _stainCount; s += 1)
            {
                if (_stainCells[s].Count == 0 || _stainRemaining[s] != 0) continue;
                seen += 1;
                if (Rng.Next(seen) == 0) pick = s;
            }

            if (pick < 0) return; // 다 닦인 덩어리가 아직 없다 — 이번 판은 김 서림 없이 지나간다

            _fogWarnStain = pick;
            _fogWarnT = 0f;
        }

        private void ApplyFog(int stainIdx)
        {
            var cells = _stainCells[stainIdx];
            for (var k = 0; k < cells.Count; k += 1)
            {
                var i = cells[k];
                if (_layers[i] > 0) continue; // 방어적으로 — 이미 층이 남아 있으면 건드리지 않는다
                _layers[i] = 1;
                _done -= 1;
            }
            _stainRemaining[stainIdx] = cells.Count;
            Fill = _total == 0 ? 1f : (float)_done / _total;
            _fogWarnStain = -1;
        }

        private int RemainingCells()
        {
            if (_layers == null) return 0;
            var n = 0;
            for (var i = 0; i < _layers.Length; i += 1) if (_layers[i] > 0) n += 1;
            return n;
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
                if (_layers[i] <= 0) continue;
                // 층이 남을수록 진하다 — 벗길 때마다 옅어지는 것이 눈에 보여야
                // "벗겨지고 있다"가 손맛으로 읽힌다
                var maxL = Mathf.Max(1, (int)_maxLayers[i]);
                var ratio = (float)_layers[i] / maxL;
                var alpha = Mathf.Lerp(0.26f, 0.8f, ratio);
                theme.Fill(new Rect(body.x + x * scaleX, body.y + y * scaleY, scaleX + 1f, scaleY + 1f),
                           HudTheme.Heat, alpha);
            }

            // 덩어리 완료 강조 — 마지막 층이 벗겨진 자리를 짧게 번쩍인다
            for (var s = 0; s < _stainCount; s += 1)
            {
                if (_stainFlashAge[s] < 0f) continue;
                var t = Mathf.Clamp01(_stainFlashAge[s] / StainFlashDuration);
                FadeBorder(theme, StainScreenRect(s, body, scaleX, scaleY), HudTheme.Accent, 1f - t, 3f);
            }

            // 김 서림 예고 — 반드시 먼저 보인다. 몰래 덮으면 배신이 아니라 억울함이다
            if (_fogWarnStain >= 0 && HudTheme.Pulse(6f))
            {
                FadeBorder(theme, StainScreenRect(_fogWarnStain, body, scaleX, scaleY), HudTheme.Cold, 0.9f, 3f);
            }

            // 마지막 한 조각 — 남은 오염이 전체의 5% 이하면 "저기만 남았다"가
            // 화면에서 읽혀야 한다. 은은하게, 재촉하지 않게
            if (_total > 0 && 1f - Fill <= 0.05f && HudTheme.Pulse(2.5f))
            {
                for (var y = 0; y < _rows; y += 1)
                for (var x = 0; x < _cols; x += 1)
                {
                    var i = y * _cols + x;
                    if (_layers[i] <= 0) continue;
                    FadeBorder(theme,
                        new Rect(body.x + x * scaleX, body.y + y * scaleY, scaleX + 1f, scaleY + 1f),
                        HudTheme.Accent, 0.5f, 1.5f);
                }
            }

            theme.Border(body, HudTheme.Rule);

            // 브러시 — 어디를 닦고 있는지 손이 보여야 한다. 가상 커서 자리를
            // 그대로 쓰므로 마우스로 몰든 화살표+Space로 몰든 같은 원이 따라온다
            var cursorPos = _cursor.Screen(body);
            if (body.Contains(cursorPos))
            {
                var r = Param("brush", 28f) * 0.5f;
                var ring = new Rect(cursorPos.x - r, cursorPos.y - r, r * 2f, r * 2f);
                theme.Border(ring, _dry ? HudTheme.Alert : HudTheme.Accent, 2f);
            }

            if (_dry)
            {
                GUI.Label(body, "약제가 떨어졌다",
                    theme.At(theme.Heading, 22, HudTheme.Alert, TextAnchor.MiddleCenter));
            }
        }

        /// <summary>덩어리 하나의 경계를 화면 좌표로. 완료 강조·김 서림 예고가 같이 쓴다</summary>
        private Rect StainScreenRect(int stainIdx, Rect body, float scaleX, float scaleY)
        {
            var x0 = body.x + _stainMinX[stainIdx] * scaleX;
            var y0 = body.y + _stainMinY[stainIdx] * scaleY;
            var w = (_stainMaxX[stainIdx] - _stainMinX[stainIdx] + 1) * scaleX;
            var h = (_stainMaxY[stainIdx] - _stainMinY[stainIdx] + 1) * scaleY;
            return new Rect(x0, y0, w, h);
        }

        /// <summary>
        /// 알파가 있는 테두리. `HudTheme.Border`는 불투명 전용이라 여기서 직접
        /// 네 변을 `Fill(rect, color, alpha)`로 그린다 — 번쩍임의 페이드와
        /// 예고 깜빡임의 은은함 둘 다 알파 없이는 못 만든다
        /// </summary>
        private static void FadeBorder(HudTheme theme, Rect rect, Color color, float alpha, float weight)
        {
            theme.Fill(new Rect(rect.x, rect.y, rect.width, weight), color, alpha);
            theme.Fill(new Rect(rect.x, rect.yMax - weight, rect.width, weight), color, alpha);
            theme.Fill(new Rect(rect.x, rect.y, weight, rect.height), color, alpha);
            theme.Fill(new Rect(rect.xMax - weight, rect.y, weight, rect.height), color, alpha);
        }
    }
}
