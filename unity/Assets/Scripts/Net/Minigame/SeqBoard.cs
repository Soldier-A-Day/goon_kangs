using UnityEngine;

namespace SoldierADay.Net
{
    /// <summary>
    /// `SEQ` 순서 입력 — 3건. 정시 교신 · 응급처치 · 암호 코드 갱신.
    ///
    /// 순서를 보여주고, 가리고, 재현하게 한다. 단발 입력 하나뿐이다.
    ///
    /// **말이 되는 순서를 쓴다.** 응급처치가 "의식 → 호흡 → 지혈 → 보고"인 것이
    /// 설계안의 지시고, 숫자 네 개를 외우는 것과 절차를 외우는 것은 다른 일이다.
    /// 암호 코드만 숫자인데, 그건 실제로 숫자를 외우는 일과다.
    ///
    /// `steps`가 2 이상이면 **자릿수가 점증한다** — 6 → 8 → 10. 한 번 외우고
    /// 끝나는 것이 아니라, 외운 것 위에 덧붙는다.
    ///
    /// ── A-5 2차 — 자리마다 고유 피치 ─────────────────────────────────────
    /// 값(0~9 또는 `_keys` 인덱스) 하나마다 계이름처럼 고유한 피치가 붙는다.
    /// **자리(순서)가 아니라 값**에 붙이는 이유는, 그래야 같은 숫자·같은 절차
    /// 단계가 언제 나오든 항상 같은 음이라 귀로 외울 수 있어서다(자리에 붙이면
    /// 매번 다른 음이 나 배울 수 없다). 노출(`_reveal`) 동안 순서대로 그
    /// 음들을 들려주고, 플레이어가 맞게 누를 때도 같은 음을 재생해 시각·청각
    /// 기억이 같은 자료를 가리키게 한다. 난이도가 올라 노출 시간(`show`)이
    /// 짧아져도 이 재생은 노출 구간에 맞춰 자동으로 빨라질 뿐 끊기지 않는다 —
    /// 짧아진 몫을 소리가 그대로 보상한다.
    ///
    /// ── 긴장감 패스 B — 속도로 조인다 ─────────────────────────────────────
    /// `steps`가 2 이상인 다단 절차는 라운드(단계)가 넘어갈 때마다 노출 재생
    /// 속도가 15%씩 빨라진다(`_stageSpeedFactor`) — 자릿수가 느는 것(암기량)과
    /// 별개의 축이다. 외울 것이 느는 동시에 "보는 시간"까지 준다는 것이
    /// 요지라, 두 축이 같은 방향으로 조여도 걷어내지 않는다(발주 §3 지시).
    /// 최저 노출 시간을 0.6초로 바닥을 둔 것은 그 아래로 가면 소리·글자
    /// 어느 쪽으로도 못 따라가는 구간이라서다.
    /// </summary>
    public sealed class SeqBoard : Board
    {
        private int[] _sequence;
        private string[] _keys;
        private int _at;
        private int _stage;
        private int _stages;
        private float _reveal;
        private float _showFor;
        private float _flash;
        private bool _wrong;
        private Rect _area;

        /// <summary>라운드(단계)마다 1.15배씩 불어나는 재생 속도 배수 — 1이 기본</summary>
        private float _stageSpeedFactor;

        /// <summary>재생 속도가 아무리 올라도 이 아래로는 못 줄어드는 노출 시간(초)</summary>
        private const float MinRevealDuration = 0.6f;

        /// <summary>마지막 3초 — 공통 하이라이트용 박동 타이머</summary>
        private float _finalBeatTimer;

        /// <summary>노출 동안 몇 번째 값까지 소리로 들려줬는가</summary>
        private int _revealSounded;

        /// <summary>지금 진행 중인 노출 구간의 전체 길이(초). 처음 노출은 `_showFor`,
        /// 오답 뒤 재노출은 그 60%라 값이 달라진다 — 슬롯 계산이 이걸 따라가야
        /// "몇 번째 값을 이미 들려줬는가"가 재노출에서도 어긋나지 않는다</summary>
        private float _revealDuration;

        public override string Instruction =>
            _reveal > 0f ? "외워라"
            : _keys == null ? "본 순서대로 눌러라"
            : "본 순서대로 눌러라 — 숫자키 1~" + _keys.Length + "도 된다";

        public override string Status =>
            _stages > 1 ? $"{_stage + 1}/{_stages}단계  ·  {_sequence?.Length ?? 0}자리"
                        : $"{_at}/{_sequence?.Length ?? 0}";

        private static readonly string[] FirstAid = { "의식", "호흡", "지혈", "보고", "체온", "기록" };
        private static readonly string[] Procedure = { "호출", "응답", "본문", "확인", "종료", "재확인", "대기", "복창" };

        protected override void Setup()
        {
            _stages = Mathf.Clamp(ParamInt("steps", 1), 1, 4);
            _stage = 0;
            _showFor = Param("show", 3f);
            _stageSpeedFactor = 1f;
            Build(ParamInt("length", 5));
        }

        private void Build(int length)
        {
            _keys = Spec?.variant switch
            {
                "firstaid" => FirstAid,
                "procedure" => Procedure,
                _ => null,
            };

            // 암호는 숫자다. 자리는 0~9이고 눌러야 할 것이 열 개다
            var pool = _keys?.Length ?? 10;
            length = Mathf.Clamp(length, 3, pool);

            _sequence = new int[length];
            if (_keys != null)
            {
                // 절차는 겹치지 않는다 — 같은 단계를 두 번 밟지 않는다
                var bag = new System.Collections.Generic.List<int>();
                for (var i = 0; i < pool; i += 1) bag.Add(i);
                for (var i = 0; i < length; i += 1)
                {
                    var j = Rng.Next(bag.Count);
                    _sequence[i] = bag[j];
                    bag.RemoveAt(j);
                }
            }
            else
            {
                for (var i = 0; i < length; i += 1) _sequence[i] = Rng.Next(10);
            }

            _at = 0;
            // 라운드마다 15%씩 빨라진 재생 속도를 여기서 반영한다 — 노출
            // 시간을 배수로 나눌 뿐이니 자릿수(`length`)가 는 것과는 별개다
            var revealDuration = Mathf.Max(MinRevealDuration, _showFor / Mathf.Max(1f, _stageSpeedFactor));
            _reveal = revealDuration;
            _revealDuration = revealDuration;
            _revealSounded = 0;
        }

        /// <summary>값(0~9 또는 `_keys` 인덱스) 하나마다 고유한 피치. 순서가 아니라
        /// 값에 붙어야 같은 숫자·같은 절차가 매번 같은 음으로 들려 귀로 외울 수 있다</summary>
        private float PitchForValue(int value)
        {
            var poolSize = _keys?.Length ?? 10;
            var t = poolSize <= 1 ? 0f : (float)value / (poolSize - 1);
            return Mathf.Lerp(0.7f, 1.9f, t);
        }

        protected override void Advance(float dt, BoardInput input)
        {
            _flash = Mathf.Max(0f, _flash - dt * 3f);
            FinalStretchTick(dt);

            if (_reveal > 0f)
            {
                _reveal -= dt;

                // 노출 구간을 자리 수만큼 나눠, 그 칸이 열리는 순간 그 값의 음을
                // 들려준다 — `show`가 난이도로 짧아져도 슬롯이 같이 줄어들 뿐이라
                // 항상 마지막 값까지 소리가 끝까지 따라간다
                var elapsed = _revealDuration - _reveal;
                var slot = _revealDuration / Mathf.Max(1, _sequence.Length);
                while (_revealSounded < _sequence.Length && elapsed >= _revealSounded * slot)
                {
                    Sfx.Play("tap", 0.7f, PitchForValue(_sequence[_revealSounded]));
                    _revealSounded += 1;
                }
                return;
            }

            var pressed = Pressed(input);
            if (pressed < 0) return;

            if (pressed == _sequence[_at])
            {
                // 노출 때 들려준 것과 같은 음을 다시 재생한다 — 시각으로 맞았다는
                // 확인이자, 그 값의 음을 한 번 더 귀에 새기는 복습이다
                Sfx.Play("tap", 1f, PitchForValue(_sequence[_at]));
                _at += 1;
                _flash = 0.5f;
                _wrong = false;

                if (_at < _sequence.Length)
                {
                    Fill = Progressed();
                    return;
                }

                _stage += 1;
                if (_stage >= _stages) { Fill = 1f; Clear(); return; }

                // 라운드마다 재생 속도 15%씩 상승 — 외울 양이 아니라 보는
                // 속도로 조인다. 자릿수 증가(6 → 8 → 10)와는 다른 축이다
                _stageSpeedFactor *= 1.15f;
                Build(_sequence.Length + 2);
                Fill = Progressed();
                return;
            }

            // 틀리면 처음부터. 절차는 중간부터 다시 하는 것이 아니다
            _wrong = true;
            _flash = 1f;
            _at = 0;
            Miss();
            // 두 번 틀리면 다시 보여준다 — 못 외운 채로 시간만 태우게 두지 않는다.
            // 지금 라운드의 빨라진 속도(`_stageSpeedFactor`)도 그대로 반영한다 —
            // 재노출이 원래 노출보다 느리게 보이면 그 자체가 보상처럼 읽힌다
            if (Mistakes % 2 == 0)
            {
                var reveal = Mathf.Max(MinRevealDuration,
                    _showFor * 0.6f / Mathf.Max(1f, _stageSpeedFactor));
                _reveal = reveal;
                _revealDuration = reveal;
                _revealSounded = 0;
            }
        }

        /// <summary>마지막 3초 — 펄스 테두리 + 저음 심박(공통 패스 B 항목)</summary>
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

        private float Progressed() =>
            _stages <= 0 ? 0f
            : Mathf.Clamp01((_stage + (float)_at / Mathf.Max(1, _sequence.Length)) / _stages);

        /// <summary>
        /// 어느 칸을 눌렀는가. 숫자는 키보드로도 받는다.
        ///
        /// H-4 — 암호 코드(`_keys == null`)는 자릿값 자체가 숫자라 0~9키가
        /// 자연스럽다. 응급처치·정시 교신처럼 글자 버튼(`_keys != null`)인
        /// 변형은 숫자로 셀 것이 없었는데, 마우스로 그 칸을 클릭하는 것 말고는
        /// 조작이 없었다 — 그래서 화면에 매겨진 순서(1번째 버튼 = 1, …)를
        /// 그대로 1~9키에 물린다. 버튼 수가 9(현재 최대 8, `Procedure`)를
        /// 넘는 변형이 생기면 그 이상은 마우스로만 닿을 수 있다.
        /// </summary>
        private int Pressed(BoardInput input)
        {
            if (_keys == null)
            {
                for (var n = 0; n <= 9; n += 1)
                {
                    if (Input.GetKeyDown(KeyCode.Alpha0 + n)) return n;
                }
            }
            else
            {
                for (var n = 0; n < _keys.Length && n < 9; n += 1)
                {
                    if (Input.GetKeyDown(KeyCode.Alpha1 + n)) return n;
                }
            }

            if (!input.Pressed || _area.width <= 0f) return -1;

            var count = _keys?.Length ?? 10;
            for (var i = 0; i < count; i += 1)
            {
                if (KeyRect(i, count).Contains(input.Mouse)) return i;
            }
            return -1;
        }

        private Rect KeyRect(int index, int count)
        {
            var perRow = count > 6 ? Mathf.CeilToInt(count / 2f) : count;
            var rows = Mathf.CeilToInt((float)count / perRow);
            var w = Mathf.Min(96f, (_area.width - 40f) / perRow);
            var h = 62f;
            var ox = _area.center.x - perRow * w * 0.5f;
            var oy = _area.yMax - rows * (h + 10f) - 8f;
            return new Rect(ox + (index % perRow) * w + 4f,
                            oy + (index / perRow) * (h + 10f),
                            w - 8f, h);
        }

        public override void Draw(HudTheme theme, Rect body)
        {
            _area = body;
            if (_sequence == null) return;

            theme.Fill(body, HudTheme.Paper3);

            // 순서 — 보여줄 때는 내용, 가린 뒤에는 자리만
            var slot = Mathf.Min(84f, (body.width - 40f) / _sequence.Length);
            for (var i = 0; i < _sequence.Length; i += 1)
            {
                var cell = new Rect(body.center.x - _sequence.Length * slot * 0.5f + i * slot + 4f,
                                    body.y + 28f, slot - 8f, 56f);
                var done = i < _at;
                theme.Fill(cell, done ? HudTheme.AccentW : HudTheme.Paper);
                theme.Border(cell, done ? HudTheme.Accent
                                 : _wrong && _flash > 0f ? HudTheme.Alert
                                 : HudTheme.Rule2, 2f);

                var text = _reveal > 0f || done ? Label(_sequence[i]) : "?";
                GUI.Label(cell, text,
                    theme.At(_keys == null ? theme.MonoBig : theme.Heading,
                        _keys == null ? 26 : 17,
                        done ? HudTheme.Accent : HudTheme.Ink, TextAnchor.MiddleCenter));
            }

            if (_reveal > 0f)
            {
                var bar = new Rect(body.center.x - 120f, body.y + 96f, 240f, 6f);
                theme.Fill(bar, HudTheme.Rule3);
                theme.Fill(new Rect(bar.x, bar.y, bar.width * (_reveal / Mathf.Max(0.01f, _revealDuration)),
                                    bar.height), HudTheme.Heat);
            }

            // 입력 칸
            var count = _keys?.Length ?? 10;
            for (var i = 0; i < count; i += 1)
            {
                var key = KeyRect(i, count);
                theme.Fill(key, HudTheme.Paper2);
                theme.Border(key, HudTheme.Rule2);
                GUI.Label(key, Label(i),
                    theme.At(_keys == null ? theme.Mono : theme.Body, 17, HudTheme.Ink,
                        TextAnchor.MiddleCenter));

                // H-4 — 글자 버튼(`_keys != null`)은 숫자로 셀 것이 없던 자리라
                // 1~9키가 이 칸을 고른다는 것이 화면에서 안 보이면 못 찾는다.
                // 암호 코드(`_keys == null`)는 라벨 자체가 이미 숫자라 안 그린다
                if (_keys != null && i < 9)
                {
                    GUI.Label(new Rect(key.x + 4f, key.y + 2f, 18f, 16f), (i + 1).ToString(),
                        theme.At(theme.Label, 11, HudTheme.Ink3));
                }
            }

            theme.Border(body, HudTheme.Rule);
            FinalStretchDraw(theme, body);
        }

        private string Label(int index) =>
            _keys != null && index < _keys.Length ? _keys[index] : index.ToString();
    }
}
