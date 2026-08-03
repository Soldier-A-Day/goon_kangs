using System.Collections.Generic;
using UnityEngine;

namespace SoldierADay.Net
{
    /// <summary>
    /// `RHYTHM` 박자 — 3건(제식 훈련 · 태극기 게양·강하 · 최종 점호).
    ///
    /// **전면 재설계(리듬 헤븐 리서치 반영).** 예전엔 시연 라운드를 통째로
    /// 끝내고 나서야 따라하기 라운드가 열렸다 — 화면이 갈리고 판이 멈췄다.
    /// 그건 "리듬"이 아니라 "암기 시험 → 재현 시험"이었다. 리듬 헤븐이 하는
    /// 방식은 다르다: **부르고 답하는 것이 한 곡 안에서 끊이지 않는다.**
    /// 여기서는 그걸 "선임이 못 하나를 박는다(마디 1) → 내가 같은 자리를
    /// 똑같이 박는다(마디 2)"의 반복으로 옮겼다. 화면 전환도, 정지도 없다 —
    /// 강조(문구·색·망치 위치)만 왼쪽(선임)에서 오른쪽(나) 사이를 오간다.
    ///
    /// ── 한 쌍의 구조(4/4박) ───────────────────────────────────────────────
    /// 마디 1(선임) — 준비 3박(카운트 소리, 피치 상승) + 4박째 타격("깡").
    /// 마디 2(나)   — 완전히 같은 프레이즈, 4박째에 Tap. 여기서부터 판정.
    /// 변주는 딱 하나, "타격 박을 한 박 뒤로 미루는 것"뿐이다(페이크는 규칙
    /// 위반이 아니라 리듬의 변주다) — 선임이 먼저 보여주고 내가 그대로
    /// 재현한다. 마지막 1~2쌍은 템포만 +8% — 가속은 이것 하나뿐이다.
    ///
    /// ── 판정과 흐름 ───────────────────────────────────────────────────────
    /// Perfect(±0.08초쯤) / Early·Late(±0.5박 안, 어긋난 피치) / Miss(입력
    /// 없음 또는 완전히 어긋남, 불협음). **무엇을 실수해도 흐름은 멈추지
    /// 않는다** — 다음 쌍이 바로 이어진다. Miss는 실수 1회, Early·Late는
    /// 2회를 모아 실수 1회로 접는다(스치듯 어긋난 것까지 Miss와 같은 무게로
    /// 세면 박자 감을 만드는 과정 자체가 벌점이 된다). 전부 소화하면
    /// `Clear()` — 등급은 베이스가 여유와 실수로 가른다. 판 자체의 즉시
    /// 실패는 없다, 시간초과만 기존대로 하드 실패다.
    ///
    /// 첫 쌍의 첫 오입력 1회는 판정에서 뺀다 — 직전 판의 박자 관성이다
    /// (리듬 헤븐 Fever 리믹스 선례). 조작은 여전히 `input.Tap`뿐이다.
    ///
    /// ── 쌍 수는 제한 시간에서 거꾸로 셈한다 ───────────────────────────────
    /// 마디 하나가 최소 4박(3박 카운트 + 1박 타격)을 쓰므로, 옛 판처럼 박마다
    /// 못 하나를 박을 수는 없다. `beats`는 더 이상 "박 개수"로 읽지 않는다
    /// (콜 앤 리스폰스엔 그 뜻이 없다) — 쌍 수의 상한(이 일과가 얼마나 손이
    /// 바빴는가의 대리 지표)으로만 남고, `combo`는 페이크가 얼마나 자주
    /// 나오는가에 관여한다. 실제 쌍 수는 이 상한과 제한 시간(`Limit`)의
    /// 88% 예산을 동시에 만족하는 가장 큰 값이다 — `BuildPattern` 참고.
    /// </summary>
    public sealed class RhythmBoard : Board
    {
        private const int MaxPairs = 8;

        /// <summary>마지막 가속 구간의 템포 배수 — 요구사항의 가속은 이것 하나뿐이다</summary>
        private const float FastFactor = 1.08f;

        /// <summary>계산된 총 길이가 이 비율을 넘는 쌍 수는 후보에서 뺀다 — 타임아웃과
        /// 정면으로 맞닿는 마지막 쌍을 만들지 않기 위한 여유다</summary>
        private const float MarginFactor = 0.88f;

        private const int Pending = 0;
        private const int Perfect = 1;
        private const int OffBeat = 2;
        private const int Popped = 3;

        private float _secPerBeat;

        private int _pairCount;
        /// <summary>쌍별 — 마지막 가속 구간인가</summary>
        private bool[] _fast;
        private float[] _npcStart;
        private float[] _npcStrike;
        private float[] _playerStart;
        private float[] _playerStrike;
        /// <summary>그 쌍이 끝나는(=다음 쌍이 시작되는) 시각</summary>
        private float[] _pairEnd;
        /// <summary>쌍별 판정 결과 — 진행 띠와 현재 못을 이걸로 그린다</summary>
        private int[] _mark;

        private int _pairIdx;
        private bool _npcTurn;
        /// <summary>이번 마디에서 울린 준비 카운트 수(0~3)</summary>
        private int _cueCount;
        /// <summary>선임이 이번 마디에서 이미 타격했는가 — 중복 재생 방지</summary>
        private bool _struckThisMeasure;

        private int _driven;
        /// <summary>Early·Late 누적 — 2에 닿으면 실수 1회로 접고 0으로 되돌린다</summary>
        private int _softMistakes;
        /// <summary>첫 쌍의 첫 오입력을 이미 봐줬는가</summary>
        private bool _graceUsed;

        /// <summary>망치 스쿼시 애니메이션 — 1에서 감쇠. `Board.FlashT`와는 다른 값이다:
        /// 저건 성공/실수를 패널 전체에 알리는 용도고, 이건 그 순간 망치가 몇 프레임
        /// 동안 짧게 내려찍혀 있는지를 그리는 순수 타격감용 타이머다</summary>
        private float _hammerT;
        private bool _hammerGood;

        public override string Instruction => "박자를 듣고, 네 차례에 [SPACE] 또는 클릭으로 같은 자리를 박아라";

        public override string Status =>
            $"못 {_driven}/{_pairCount}" + (_npcTurn ? "  ·  선임 차례" : "  ·  내 차례");

        protected override void Setup()
        {
            var bpm = Mathf.Max(30f, Param("bpm", 76f));
            _secPerBeat = 60f / bpm;

            var beats = Mathf.Max(4, ParamInt("beats", 16));
            var combo = Mathf.Max(2, ParamInt("combo", 10));
            BuildPattern(beats, combo);

            _pairIdx = 0;
            _npcTurn = true;
            _cueCount = 0;
            _struckThisMeasure = false;
            _driven = 0;
            _softMistakes = 0;
            _graceUsed = false;
            _hammerT = 0f;
            Fill = 0f;
        }

        /// <summary>
        /// 쌍(마디 2개) 스케줄을 통째로 미리 계산한다.
        ///
        /// 쌍 수 후보를 `beats` 상한부터 1까지 내려가며 시도해, 총 길이가
        /// `Limit`의 88% 예산 안에 드는 가장 큰 값을 고른다(1은 예산을
        /// 넘더라도 무조건 채택 — 판이 아예 못 열리는 것보단 낫다). 후보의
        /// 총 길이는 실제 배치(어느 쌍이 페이크인지)와 무관하게 "몇 개가
        /// 페이크·몇 개가 가속인가"만으로 정확히 계산된다 — 마디 하나의
        /// 길이는 페이크·가속 여부에만 좌우되고 자리에는 좌우되지 않기 때문이다.
        /// </summary>
        private void BuildPattern(int beats, int combo)
        {
            var cap = Mathf.Clamp(Mathf.RoundToInt(beats / 6f), 3, MaxPairs);
            // 반복이 잦았던 일과(옛 기준 13 이상)일수록 페이크를 더 자주 보여준다
            var delayEvery = combo >= 13 ? 2 : 3;
            var budget = Limit * MarginFactor;

            var count = 1;
            for (var p = cap; p >= 1; p -= 1)
            {
                var fastPairs = FastPairsFor(p);
                var normalPairs = p - fastPairs;
                var delayCount = normalPairs > 1 ? (normalPairs - 1) / delayEvery : 0;
                var seconds = (normalPairs - delayCount) * PairSeconds(false, false)
                            + delayCount * PairSeconds(true, false)
                            + fastPairs * PairSeconds(false, true);

                if (seconds <= budget || p == 1)
                {
                    count = p;
                    break;
                }
            }

            _pairCount = count;
            _mark = new int[_pairCount];
            _fast = new bool[_pairCount];
            _npcStart = new float[_pairCount];
            _npcStrike = new float[_pairCount];
            _playerStart = new float[_pairCount];
            _playerStrike = new float[_pairCount];
            _pairEnd = new float[_pairCount];

            var fastN = FastPairsFor(_pairCount);
            var normalN = _pairCount - fastN;
            for (var i = _pairCount - fastN; i < _pairCount; i += 1) _fast[i] = true;

            var delayN = normalN > 1 ? (normalN - 1) / delayEvery : 0;
            var delay = PickDelaySlots(normalN, delayN);

            var clock = 0f;
            for (var i = 0; i < _pairCount; i += 1)
            {
                var spb = _fast[i] ? _secPerBeat / FastFactor : _secPerBeat;
                var strikeBeat = 3 + (delay[i] ? 1 : 0);
                var measureLen = (delay[i] ? 5 : 4) * spb;

                // 마디 1(선임) — 준비 3박 + 타격
                _npcStart[i] = clock;
                _npcStrike[i] = clock + strikeBeat * spb;
                clock += measureLen;

                // 마디 2(나) — 완전히 같은 프레이즈
                _playerStart[i] = clock;
                _playerStrike[i] = clock + strikeBeat * spb;
                clock += measureLen;

                _pairEnd[i] = clock;
            }
        }

        private float PairSeconds(bool delay, bool fast)
        {
            var spb = fast ? _secPerBeat / FastFactor : _secPerBeat;
            var measureBeats = delay ? 5 : 4;
            return 2f * measureBeats * spb; // 마디 2개(선임+나)
        }

        /// <summary>마지막 몇 쌍이 가속 구간인가 — 너무 짧은 곡(2쌍 이하)엔 끼워 넣지
        /// 않는다, 가속을 느낄 여유조차 없는 길이에 새 규칙을 얹지 않기 위해서다</summary>
        private static int FastPairsFor(int pairCount) => pairCount >= 6 ? 2 : pairCount >= 3 ? 1 : 0;

        /// <summary>
        /// 페이크(타격 박 한 박 밀림)를 넣을 쌍의 자리를 고른다.
        ///
        /// 후보는 [1, normalPairCount) — 첫 쌍(0)은 항상 뺀다(몸이 기본
        /// 프레이즈부터 익혀야 한다)과 가속 구간(별도로 언제나 페이크 없음)도
        /// 자동으로 빠져 있다. 같은 questId는 같은 `Rng` 시드를 받으므로
        /// 같은 일과는 늘 같은 자리에서 페이크가 나온다 — 몸이 그 자리를 기억한다.
        /// </summary>
        private bool[] PickDelaySlots(int normalPairCount, int delayCount)
        {
            var flags = new bool[_pairCount];
            if (delayCount <= 0 || normalPairCount <= 1) return flags;

            var candidates = new List<int>(normalPairCount - 1);
            for (var i = 1; i < normalPairCount; i += 1) candidates.Add(i);

            for (var i = candidates.Count - 1; i > 0; i -= 1)
            {
                var j = Rng.Next(i + 1);
                var tmp = candidates[i];
                candidates[i] = candidates[j];
                candidates[j] = tmp;
            }

            for (var i = 0; i < delayCount && i < candidates.Count; i += 1) flags[candidates[i]] = true;
            return flags;
        }

        protected override void Advance(float dt, BoardInput input)
        {
            _hammerT = Mathf.Max(0f, _hammerT - dt * 5f);

            // 준비 3박 카운트 — 선임 마디든 내 마디든 같은 큐다. 오디오가
            // 리드하고 박자 표시등(Draw)이 보강한다
            var beatIdx = CurrentBeatIndex();
            while (_cueCount <= beatIdx && _cueCount < 3)
            {
                Sfx.Play("tap", 0.35f, Mathf.Lerp(0.75f, 1.05f, _cueCount / 2f));
                _cueCount += 1;
            }

            if (_npcTurn)
            {
                if (!_struckThisMeasure && Elapsed >= _npcStrike[_pairIdx])
                {
                    _struckThisMeasure = true;
                    _hammerT = 1f;
                    _hammerGood = true;
                    Sfx.Play("tap");
                }

                if (Elapsed >= _playerStart[_pairIdx])
                {
                    _npcTurn = false;
                    _cueCount = 0;
                    _struckThisMeasure = false;
                }
                return;
            }

            // ── 내 차례 — 여기부터 판정 ──
            if (_mark[_pairIdx] == Pending)
            {
                if (input.Tap)
                {
                    Judge();
                }
                else if (Elapsed > _playerStrike[_pairIdx] + HalfBeat(_pairIdx))
                {
                    // 판정 창을 그냥 흘려보냈다 — 입력이 아예 없었던 것도 Miss다
                    ApplyGrade(Popped, false);
                }
            }

            if (Elapsed >= _pairEnd[_pairIdx])
            {
                if (_mark[_pairIdx] == Pending) ApplyGrade(Popped, false);
                _pairIdx += 1;
                if (_pairIdx >= _pairCount) { Clear(); return; }
                _npcTurn = true;
                _cueCount = 0;
                _struckThisMeasure = false;
            }
        }

        private int CurrentBeatIndex()
        {
            var measureStart = _npcTurn ? _npcStart[_pairIdx] : _playerStart[_pairIdx];
            var spb = _fast[_pairIdx] ? _secPerBeat / FastFactor : _secPerBeat;
            return Mathf.Clamp(Mathf.FloorToInt((Elapsed - measureStart) / spb), 0, 3);
        }

        private float HalfBeat(int i) => 0.5f * (_fast[i] ? _secPerBeat / FastFactor : _secPerBeat);

        private void Judge()
        {
            // 난이도가 오를수록 Perfect 창만 좁아진다 — spec의 "±0.08초쯤"은
            // 난이도 2(원형 기본값)에서 정확히 그 값이 되도록 잡은 중간점이다
            var perfectWindow = Mathf.Lerp(0.095f, 0.065f, (Difficulty - 1f) * 0.5f);
            var half = HalfBeat(_pairIdx);
            var offset = Elapsed - _playerStrike[_pairIdx];
            var abs = Mathf.Abs(offset);

            var grade = abs <= perfectWindow ? Perfect : abs <= half ? OffBeat : Popped;

            if (grade != Perfect && _pairIdx == 0 && !_graceUsed)
            {
                // 전환 관용 — 첫 쌍의 첫 오입력은 판정하지 않는다. 직전 판의
                // 박자 관성이라 보고 다시 노려볼 기회를 준다(리듬 헤븐 Fever
                // 리믹스 선례). 못은 아직 Pending이라 다음 입력을 계속 받는다
                _graceUsed = true;
                return;
            }

            ApplyGrade(grade, offset < 0f);
        }

        /// <summary>
        /// 판정 확정 — 마크·못 카운트·소리·플래시를 한 곳에서 맺는다.
        ///
        /// `early`는 OffBeat일 때만 의미가 있다(이르게 어긋났나 늦게
        /// 어긋났나로 피치를 가른다). Popped는 실제 오입력이든 자동 Miss든
        /// 여기로 합류한다 — 원인이 달라도 결과(불협음+실수 1회)는 같다.
        /// </summary>
        private void ApplyGrade(int grade, bool early)
        {
            _mark[_pairIdx] = grade;
            _driven += 1;
            Fill = (float)_driven / _pairCount;
            _hammerT = 1f;
            _hammerGood = grade == Perfect;

            if (grade == Perfect)
            {
                Sfx.Play("tap");
                return;
            }

            if (grade == OffBeat)
            {
                // 어긋난 피치 — 이르면 급하게 높이, 늦으면 처지듯 낮게
                Sfx.Play("tap", 1f, early ? 1.15f : 0.85f);
                _softMistakes += 1;
                // Early·Late 두 번을 실수 한 번으로 접는다(§클래스 요약) —
                // 매번 Miss()를 부르면 스치듯 어긋난 것까지 Miss와 같은
                // 무게가 된다
                if (_softMistakes < 2) return;
                _softMistakes = 0;
                Miss();
                return;
            }

            // Popped(Miss) — Board.Miss()의 "miss" 클립이 이미 저역 비팅음이라
            // 그 자체로 불협음이다. 따로 경고음을 더 얹지 않는다
            Miss();
        }

        public override void Draw(HudTheme theme, Rect body)
        {
            theme.Fill(body, HudTheme.Paper3);

            var demo = _pairIdx < _pairCount && _npcTurn;
            var phaseColor = demo ? HudTheme.Ink3 : HudTheme.Accent;
            var phaseText = demo ? "선임 차례 — 잘 보고 들어라" : "네 차례!";
            GUI.Label(new Rect(body.x, body.y + 8f, body.width, 30f), phaseText,
                theme.At(theme.Heading, 20, phaseColor, TextAnchor.MiddleCenter));

            DrawProgressStrip(theme, body);
            DrawHammerRig(theme, body);
            DrawBeatLights(theme, body);

            var bar = new Rect(body.x + 60f, body.yMax - 46f, body.width - 120f, 18f);
            theme.Bar(bar, Mathf.Clamp01(Fill), HudTheme.Accent);
            GUI.Label(new Rect(bar.x, bar.y - 24f, 240f, 20f), $"못 {_driven}/{_pairCount}",
                theme.At(theme.Label, 12, HudTheme.Ink2));

            theme.Border(body, HudTheme.Rule);
        }

        /// <summary>전체 진행 띠 — 지금까지 박은 못을 등급 색으로 보여준다.
        /// 옛 판의 "못 줄"과 같은 역할이지만, 이번엔 한 쌍에 못이 하나뿐이라
        /// 줄 전체가 곧 전체 진행이다</summary>
        private void DrawProgressStrip(HudTheme theme, Rect body)
        {
            var n = _pairCount;
            var y = body.y + 46f;
            var maxSpan = Mathf.Max(0f, body.width - 160f);
            var spacing = n > 1 ? Mathf.Min(56f, maxSpan / (n - 1)) : 0f;
            var startX = body.center.x - spacing * (n - 1) * 0.5f;

            for (var i = 0; i < n; i += 1)
            {
                var cell = new Rect(startX + spacing * i - 8f, y, 16f, 16f);
                if (_mark[i] == Perfect) theme.Fill(cell, HudTheme.Accent);
                else if (_mark[i] == OffBeat) theme.Fill(cell, HudTheme.Alert, 0.7f);
                else if (_mark[i] == Popped) theme.Fill(cell, HudTheme.Alert);
                else theme.Fill(cell, HudTheme.Rule2);
            }
        }

        /// <summary>망치·못 — 강조가 왼쪽(선임)·오른쪽(나) 사이를 오간다. 화면은
        /// 절대 바뀌지 않는다, 같은 자리에서 위치만 옮겨 어느 차례인지를 말한다</summary>
        private void DrawHammerRig(HudTheme theme, Rect body)
        {
            var idx = Mathf.Clamp(_pairIdx, 0, _pairCount - 1);
            var npcTurn = _pairIdx < _pairCount && _npcTurn;
            var rowY = body.center.y + 16f;
            var leftX = body.center.x - 150f;
            var rightX = body.center.x + 150f;
            var hx = npcTurn ? leftX : rightX;

            GUI.Label(new Rect(leftX - 40f, rowY - 118f, 80f, 22f), "선임",
                theme.At(theme.Label, 13, npcTurn ? HudTheme.Ink2 : HudTheme.Rule3, TextAnchor.MiddleCenter));
            GUI.Label(new Rect(rightX - 40f, rowY - 118f, 80f, 22f), "나",
                theme.At(theme.Label, 13, npcTurn ? HudTheme.Rule3 : HudTheme.Ink2, TextAnchor.MiddleCenter));

            var slot = new Rect(hx - 10f, rowY - 20f, 20f, 40f);
            DrawNail(theme, slot, idx, npcTurn);

            var raiseY = rowY - 90f;
            var strikeY = rowY - 22f;
            var swing = Mathf.Clamp01(_hammerT / 0.6f); // 1=막 내려친 순간, 0=들어 올린 채 대기
            var headY = Mathf.Lerp(raiseY, strikeY, swing);
            var squash = Mathf.Lerp(1f, 0.5f, swing); // 내려칠수록 머리가 눌린다 — 타격감
            var headH = 22f * squash;
            var headColor = _hammerT > 0f ? (_hammerGood ? HudTheme.Accent : HudTheme.Alert) : HudTheme.Ink2;
            theme.Fill(new Rect(hx - 18f, headY, 36f, headH), headColor);
            var handleH = Mathf.Max(0f, (rowY - 20f) - (headY + headH));
            theme.Fill(new Rect(hx - 3f, headY + headH, 6f, handleH), HudTheme.Rule);
        }

        private void DrawNail(HudTheme theme, Rect slot, int idx, bool npcTurn)
        {
            var mark = _mark[idx];

            if (mark == Perfect)
            {
                var head = new Rect(slot.x - 4f, slot.y + 6f, slot.width + 8f, 14f);
                theme.Fill(head, HudTheme.Accent);
                return;
            }

            if (mark == OffBeat)
            {
                // 살짝 삐뚤다 — 박자가 어긋났지 완전히 놓친 건 아니다. 회전은
                // 반드시 `HudTheme.RotateAt`으로 한다(GUIUtility 버전은 배율이
                // 1이 아닌 화면에서 피벗이 밀린다)
                var prevMatrix = GUI.matrix;
                HudTheme.RotateAt(14f, new Vector2(slot.center.x, slot.yMax));
                theme.Fill(slot, HudTheme.Alert, 0.7f);
                GUI.matrix = prevMatrix;
                return;
            }

            if (mark == Popped)
            {
                // 완전히 튕겨 나왔다 — 각도도 크고 자리에서도 들떠 있다
                var prevMatrix = GUI.matrix;
                var popped = new Rect(slot.x, slot.y - 10f, slot.width, slot.height);
                HudTheme.RotateAt(34f, new Vector2(popped.center.x, popped.yMax));
                theme.Fill(popped, HudTheme.Alert);
                GUI.matrix = prevMatrix;
                return;
            }

            // 아직 안 박혔다 — 이번 마디의 타격 박이 가까우면 자리가 빛난다
            var spb = _fast[idx] ? _secPerBeat / FastFactor : _secPerBeat;
            var strike = npcTurn ? _npcStrike[idx] : _playerStrike[idx];
            var near = Mathf.Abs(Elapsed - strike) < spb * 0.3f;
            theme.Fill(slot, HudTheme.Rule2);
            theme.Border(slot, near ? HudTheme.Accent : HudTheme.Rule3, near ? 2f : 1f);
        }

        /// <summary>박자 표시등 — 4칸, 지금 박에만 불이 들어온다. 오디오가 리드하고
        /// 이건 보강이다(청각 없이도 카운트가 눈으로 읽혀야 한다)</summary>
        private void DrawBeatLights(HudTheme theme, Rect body)
        {
            var idx = _pairIdx >= _pairCount ? 3 : CurrentBeatIndex();
            var y = body.yMax - 100f;
            const float w = 24f;
            const float gap = 12f;
            var totalW = 4 * w + 3 * gap;
            var startX = body.center.x - totalW * 0.5f;

            for (var b = 0; b < 4; b += 1)
            {
                var cell = new Rect(startX + b * (w + gap), y, w, w);
                theme.Fill(cell, b == idx ? HudTheme.Accent : HudTheme.Rule2);
                theme.Border(cell, HudTheme.Rule3);
            }
        }
    }
}
