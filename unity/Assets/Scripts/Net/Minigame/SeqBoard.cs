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

        public override string Instruction =>
            _reveal > 0f ? "외워라" : "본 순서대로 눌러라";

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
            _reveal = _showFor;
        }

        protected override void Advance(float dt, BoardInput input)
        {
            _flash = Mathf.Max(0f, _flash - dt * 3f);

            if (_reveal > 0f)
            {
                _reveal -= dt;
                return;
            }

            var pressed = Pressed(input);
            if (pressed < 0) return;

            if (pressed == _sequence[_at])
            {
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

                // 자릿수가 는다. 6 → 8 → 10
                Build(_sequence.Length + 2);
                Fill = Progressed();
                return;
            }

            // 틀리면 처음부터. 절차는 중간부터 다시 하는 것이 아니다
            _wrong = true;
            _flash = 1f;
            _at = 0;
            Miss();
            // 두 번 틀리면 다시 보여준다 — 못 외운 채로 시간만 태우게 두지 않는다
            if (Mistakes % 2 == 0) _reveal = _showFor * 0.6f;
        }

        private float Progressed() =>
            _stages <= 0 ? 0f
            : Mathf.Clamp01((_stage + (float)_at / Mathf.Max(1, _sequence.Length)) / _stages);

        /// <summary>어느 칸을 눌렀는가. 숫자는 키보드로도 받는다</summary>
        private int Pressed(BoardInput input)
        {
            if (_keys == null)
            {
                for (var n = 0; n <= 9; n += 1)
                {
                    if (Input.GetKeyDown(KeyCode.Alpha0 + n)) return n;
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
                theme.Fill(new Rect(bar.x, bar.y, bar.width * (_reveal / Mathf.Max(0.01f, _showFor)),
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
            }

            theme.Border(body, HudTheme.Rule);
        }

        private string Label(int index) =>
            _keys != null && index < _keys.Length ? _keys[index] : index.ToString();
    }
}
