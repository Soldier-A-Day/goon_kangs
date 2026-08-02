using UnityEngine;

namespace SoldierADay.Net
{
    /// <summary>
    /// `RHYTHM` 박자 — 3건(제식 훈련 · 태극기 게양·강하 · 불시 점호)과
    /// 최종 점호(`RANDOM.최종 점호`).
    ///
    /// **콜 앤 리스폰스로 다시 짰다.** 예전에는 화면이 일정한 템포로 원을
    /// 좁혀 오고 그 박에 맞춰 누르기만 하면 됐는데, 그건 "박자"가 아니라
    /// "메트로놈 반응 속도"였다 — 몸이 왜 그 리듬인지는 배우지 않는다.
    /// 리듬 헤븐이 하는 방식은 반대다: **먼저 보여주고, 그 다음 따라 하게
    /// 한다.** 여기서는 그걸 "시연(망치가 못을 박는다) → 따라하기(내가 같은
    /// 리듬으로 못을 박는다)"로 옮겼다. 못 박기를 고른 것은 소재 때문이 아니라
    /// — 리듬 헤븐 원곡의 "타이밍이 어긋나면 결과물이 눈에 보이게 망가진다"는
    /// 성질이 못이 삐뚤어지는 것으로 그대로 옮겨지기 때문이다.
    ///
    /// 조작은 여전히 하나, `input.Tap`뿐이다(설계안 §전제 3).
    /// </summary>
    public sealed class RhythmBoard : Board
    {
        private enum Phase { Demo, Follow }

        private const int MaxNailsPerRound = 4;
        private const int Pending = 0;
        private const int Good = 1;
        private const int Bad = 2;

        /// <summary>반박(半拍) 단위 시간(초). 온박은 이것의 2배 — 둘을 섞은 간격이 리듬이다</summary>
        private float _unit;

        private int _roundCount;
        private int[] _roundNailCounts;
        /// <summary>라운드별, 그 라운드가 시작된 시점부터 각 못까지의 목표 시각(초)</summary>
        private float[][] _roundTargets;

        private int _round;
        private Phase _phase;
        /// <summary>지금 phase(시연 또는 따라하기) 안에서 흐른 시간. dt 누적이지 OnGUI 시계가 아니다</summary>
        private float _clock;
        /// <summary>이번 phase에서 다음으로 처리할 못 인덱스</summary>
        private int _nailIdx;

        private readonly int[] _nailMark = new int[MaxNailsPerRound];
        private readonly bool[] _demoStruck = new bool[MaxNailsPerRound];

        private int _drivenTotal;
        private int _totalNails;

        /// <summary>망치 스쿼시 애니메이션 — 1에서 감쇠. `Board.FlashT`와는 다른 값이다:
        /// 저건 성공/실수를 패널 전체에 알리는 용도고, 이건 그 순간 망치가 몇 프레임
        /// 동안 짧게 내려찍혀 있는지를 그리는 순수 타격감용 타이머다</summary>
        private float _hammerT;
        private bool _hammerGood;

        public override string Instruction => "리듬을 보고, 같은 박자로 눌러 박아라";

        public override string Status => $"못 {_drivenTotal}/{_totalNails}";

        protected override void Setup()
        {
            var bpm = Mathf.Max(30f, Param("bpm", 76f));
            _unit = 60f / bpm * 0.5f;

            var beats = Mathf.Max(4, ParamInt("beats", 16));
            var combo = Mathf.Max(2, ParamInt("combo", 10));

            // 라운드 수 — beats·combo를 원래 뜻(균일 박 개수 · 필요 연속 성공 수)
            // 그대로 쓰지 않는다. 콜 앤 리스폰스에는 그 의미가 없다. 대신 "이
            // 일과가 원래 얼마나 손이 바빴는가"의 대리 지표로 재해석한다:
            // beats는 7박마다 라운드 하나로 접고(원래 박이 많을수록 절차가
            // 길었다는 뜻), combo가 높았던 일과(옛 기준 13 이상 — rifle-drill의
            // 14가 여기 걸린다)는 그만큼 반복이 잦았다는 뜻이라 라운드를 하나 더
            // 얹는다. 결과는 늘 3~4라운드 — 그 이상은 시연 하나를 다 외우기도
            // 전에 다음 시연이 겹쳐 온다.
            _roundCount = Mathf.Clamp(Mathf.RoundToInt(beats / 7f), 3, 4);
            if (combo >= 13) _roundCount = Mathf.Min(4, _roundCount + 1);

            _roundNailCounts = new int[_roundCount];
            _totalNails = 0;
            for (var r = 0; r < _roundCount; r += 1)
            {
                // 못 2개로 시작해 라운드마다 +1, 4개에서 멈춘다. 시연 한 번으로
                // 외워야 하는 리듬이라 그 이상은 리듬 헤븐도 넘기지 않는 선이다
                _roundNailCounts[r] = Mathf.Clamp(2 + r, 2, MaxNailsPerRound);
                _totalNails += _roundNailCounts[r];
            }

            // 패턴은 Rng로 딱 한 번 뽑아 시연·따라하기 양쪽이 같은 시각을 쓴다.
            // 같은 questId는 같은 Rng 시드(Board.Begin)를 받으니 같은 패턴이
            // 나온다 — 플레이어가 같은 일과를 다시 만나면 몸이 그 리듬을 기억한다
            _roundTargets = new float[_roundCount][];
            for (var r = 0; r < _roundCount; r += 1)
            {
                var n = _roundNailCounts[r];
                var t = new float[n];
                var time = _unit * 2f; // 첫 못 전에 한 박 여유 — 손을 들 틈
                t[0] = time;
                for (var i = 1; i < n; i += 1)
                {
                    // 반박(1유닛) 아니면 온박(2유닛) — 고르게 두면 그건 박자가
                    // 아니라 그냥 초시계다. 이 둘을 섞은 것이 "리듬"이다
                    var gapUnits = Rng.Next(2) == 0 ? 1 : 2;
                    time += _unit * gapUnits;
                    t[i] = time;
                }
                _roundTargets[r] = t;
            }

            _round = 0;
            _phase = Phase.Demo;
            _clock = 0f;
            _nailIdx = 0;
            _drivenTotal = 0;
            Fill = 0f;
            _hammerT = 0f;
            ResetMarks();
        }

        private void ResetMarks()
        {
            for (var i = 0; i < MaxNailsPerRound; i += 1)
            {
                _nailMark[i] = Pending;
                _demoStruck[i] = false;
            }
        }

        protected override void Advance(float dt, BoardInput input)
        {
            _hammerT = Mathf.Max(0f, _hammerT - dt * 5f);
            _clock += dt;

            var n = _roundNailCounts[_round];
            var targets = _roundTargets[_round];

            if (_phase == Phase.Demo)
            {
                // 시연 중엔 입력을 받지 않는다 — 지금은 손이 아니라 눈과 귀로
                // 외우는 차례다(입력을 씹는 게 아니라 애초에 보지 않는다)
                while (_nailIdx < n && _clock >= targets[_nailIdx])
                {
                    _demoStruck[_nailIdx] = true;
                    _hammerT = 1f;
                    _hammerGood = true;
                    Sfx.Play("tap");
                    _nailIdx += 1;
                }

                // 마지막 못을 친 뒤 한 마디 반을 쉬어 "이제 네 차례"가 분명히
                // 갈린다 — 곧바로 이어지면 시연과 따라하기가 한 리듬으로 뭉개진다
                if (_nailIdx >= n && _clock >= targets[n - 1] + _unit * 3f)
                {
                    _phase = Phase.Follow;
                    _clock = 0f;
                    _nailIdx = 0;
                    for (var i = 0; i < n; i += 1) _nailMark[i] = Pending;
                }
                return;
            }

            // ── 따라하기 ──
            // 창은 ±0.15초쯤에서 시작해 난이도가 오르면 좁아진다. TIMING의
            // 창과 이름은 같지만 여긴 창이 옮겨 다니지 않는다 — 자리는 항상
            // 같고 시각만 문제다
            var window = Mathf.Lerp(0.15f, 0.08f, (Difficulty - 1f) * 0.5f);

            // 판정 창을 그냥 흘려보낸 못은 자동으로 삐뚤게 박힌다 — 손을
            // 아예 놓친 것도 실수다, 조용히 넘어가면 다음 못과 박이 밀린다
            while (_nailIdx < n && _clock > targets[_nailIdx] + window)
            {
                _nailMark[_nailIdx] = Bad;
                _drivenTotal += 1;
                _hammerT = 1f;
                _hammerGood = false;
                Miss();
                _nailIdx += 1;
            }
            Fill = (float)_drivenTotal / _totalNails;

            if (_nailIdx >= n) { AdvanceRound(); return; }

            if (!input.Tap) return;

            var offset = Mathf.Abs(_clock - targets[_nailIdx]);
            _hammerT = 1f;
            if (offset <= window)
            {
                _nailMark[_nailIdx] = Good;
                _hammerGood = true;
                Sfx.Play("tap");
            }
            else
            {
                _nailMark[_nailIdx] = Bad;
                _hammerGood = false;
                Miss();
            }
            _drivenTotal += 1;
            _nailIdx += 1;
            Fill = (float)_drivenTotal / _totalNails;

            if (_nailIdx >= n) AdvanceRound();
        }

        private void AdvanceRound()
        {
            _round += 1;
            // 전 라운드 완료 = Clear. 시간초과는 기존 RHYTHM 그대로 실패(재시도) —
            // GradesOnTimeout을 켜지 않으므로 Board.HandleTimeout이 그렇게 처리한다
            if (_round >= _roundCount) { Clear(); return; }

            _phase = Phase.Demo;
            _clock = 0f;
            _nailIdx = 0;
            ResetMarks();
        }

        public override void Draw(HudTheme theme, Rect body)
        {
            theme.Fill(body, HudTheme.Paper3);

            var round = Mathf.Clamp(_round, 0, _roundCount - 1);
            var n = _roundNailCounts[round];
            var targets = _roundTargets[round];

            // 차례 표시 — 색과 문구가 같이 바뀐다. 귀로만 갈리면 놓친다
            var demo = _phase == Phase.Demo;
            var phaseColor = demo ? HudTheme.Ink3 : HudTheme.Accent;
            var phaseText = demo ? "듣는 차례 — 잘 보고 들어라" : "네 차례!";
            GUI.Label(new Rect(body.x, body.y + 10f, body.width, 30f), phaseText,
                theme.At(theme.Heading, 20, phaseColor, TextAnchor.MiddleCenter));

            // 못 줄 — 좁은 화면에서도 안 겹치도록 간격을 body 폭에 맞춰 줄인다
            var rowY = body.center.y + 16f;
            var maxSpan = Mathf.Max(0f, body.width - 160f);
            var spacing = n > 1 ? Mathf.Min(90f, maxSpan / (n - 1)) : 0f;
            var startX = body.center.x - spacing * (n - 1) * 0.5f;

            for (var i = 0; i < n; i += 1)
            {
                var x = startX + spacing * i;
                var slot = new Rect(x - 10f, rowY - 20f, 20f, 40f);

                // 그 못의 제 박이 가까우면 자리가 빛난다 — 시연이든 따라하기든
                // 똑같이 켜져서, 리듬을 귀가 아니라 눈으로도 외우게 한다
                var near = Mathf.Abs(_clock - targets[i]) < 0.12f;
                var glow = near ? HudTheme.Accent : HudTheme.Rule3;

                if (demo)
                {
                    var driven = _demoStruck[i];
                    var head = driven
                        ? new Rect(slot.x - 4f, slot.y + 6f, slot.width + 8f, 14f)
                        : new Rect(slot.x, slot.y, slot.width, 14f);
                    theme.Fill(head, driven ? HudTheme.Ink : HudTheme.Rule2);
                    theme.Border(slot, glow, near ? 2f : 1f);
                }
                else if (_nailMark[i] == Good)
                {
                    var head = new Rect(slot.x - 4f, slot.y + 6f, slot.width + 8f, 14f);
                    theme.Fill(head, HudTheme.Accent);
                }
                else if (_nailMark[i] == Bad)
                {
                    // 삐뚤게 박혔다 — 못만 기울여 그린다. 회전은 반드시
                    // `HudTheme.RotateAt`으로 한다(GUIUtility 버전은 배율이
                    // 1이 아닌 화면에서 피벗이 밀린다)
                    var prevMatrix = GUI.matrix;
                    HudTheme.RotateAt(16f, new Vector2(slot.center.x, slot.yMax));
                    theme.Fill(slot, HudTheme.Alert);
                    GUI.matrix = prevMatrix;
                }
                else
                {
                    theme.Fill(slot, HudTheme.Rule2);
                    theme.Border(slot, glow, near ? 2f : 1f);
                }
            }

            // 망치 — 지금 처리 중인 못 위에서 스쿼시하며 내려찍는다
            var activeIdx = Mathf.Clamp(_nailIdx, 0, n - 1);
            var hx = startX + spacing * activeIdx;
            var raiseY = rowY - 90f;
            var strikeY = rowY - 22f;
            var swing = Mathf.Clamp01(_hammerT / 0.6f); // 1=막 내려친 순간, 0=들어 올린 채 대기
            var headY = Mathf.Lerp(raiseY, strikeY, swing);
            var squash = Mathf.Lerp(1f, 0.5f, swing); // 내려칠수록 머리가 눌린다 — 타격감
            var headH = 22f * squash;
            var headColor = _hammerT > 0f ? (_hammerGood ? HudTheme.Accent : HudTheme.Alert) : HudTheme.Ink2;
            var head2 = new Rect(hx - 18f, headY, 36f, headH);
            theme.Fill(head2, headColor);
            // 손잡이 — 내려칠수록 망치 머리가 못에 파묻히므로 음수 길이가 되지 않게 막는다
            var handleH = Mathf.Max(0f, (rowY - 20f) - (headY + headH));
            theme.Fill(new Rect(hx - 3f, headY + headH, 6f, handleH), HudTheme.Rule);

            // 진행 — Fill = 박은 못(바르게든 삐뚤게든) / 전체 못
            var bar = new Rect(body.x + 60f, body.yMax - 46f, body.width - 120f, 18f);
            theme.Bar(bar, Mathf.Clamp01(Fill), HudTheme.Accent);
            GUI.Label(new Rect(bar.x, bar.y - 24f, 240f, 20f), $"못 {_drivenTotal}/{_totalNails}",
                theme.At(theme.Label, 12, HudTheme.Ink2));
            GUI.Label(new Rect(bar.xMax - 200f, bar.y - 24f, 200f, 20f),
                $"라운드 {round + 1}/{_roundCount}",
                theme.At(theme.Label, 12, HudTheme.Ink3, TextAnchor.MiddleRight));

            theme.Border(body, HudTheme.Rule);
        }
    }
}
