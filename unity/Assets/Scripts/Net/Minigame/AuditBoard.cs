using UnityEngine;

namespace SoldierADay.Net
{
    /// <summary>
    /// `AUDIT` 대조 점검 — 10건. `PLACE`와 함께 가장 많이 쓰인다.
    ///
    /// 대장과 실물을 나란히 놓고 **어긋난 줄을 찍어낸다.** 클릭 하나뿐이다.
    ///
    /// 오답 `strikes`회면 재시도다. 이게 이 판의 긴장인데, 못 찾겠다고 전부
    /// 찍으면 그 자리에서 끝난다 — 세는 것이 아니라 **보는 것**이 일이다.
    ///
    /// 어긋남은 한 자리 차이로 만든다. 무기고 재물 조사가 "숫자 한 자리 차이"인
    /// 것이 설계안의 지시이고, 그래야 대조가 훑기가 아니라 읽기가 된다.
    /// </summary>
    public sealed class AuditBoard : Board
    {
        private sealed class Row
        {
            public string Code;
            public string Ledger;
            public string Actual;
            public bool Mismatch;
            public bool Flagged;
            public bool Wrong;
        }

        private Row[] _rows;
        private int _found;
        private int _need;
        private int _strikes;
        private int _limit;
        private int _columns = 1;
        private Rect _area;

        public override string Instruction => "대장과 다른 줄을 찍어라";

        public override string Status =>
            $"적발 {_found}/{_need}  ·  오답 {_strikes}/{_limit}";

        protected override void Setup()
        {
            var count = Mathf.Clamp(ParamInt("entries", 12), 4, 20);
            _need = Mathf.Clamp(ParamInt("mismatches", 3), 1, count);
            _limit = Mathf.Max(1, ParamInt("strikes", 3));
            _found = 0;
            _strikes = 0;
            _columns = count > 10 ? 2 : 1;

            _rows = new Row[count];
            for (var i = 0; i < count; i += 1)
            {
                var value = Rng.Next(10, 99);
                _rows[i] = new Row
                {
                    Code = $"{Prefix()}-{Rng.Next(100, 999)}",
                    Ledger = value.ToString(),
                    Actual = value.ToString(),
                };
            }

            // 어긋난 줄을 고른다
            var picked = 0;
            while (picked < _need)
            {
                var i = Rng.Next(count);
                if (_rows[i].Mismatch) continue;
                _rows[i].Mismatch = true;
                // 한 자리만 다르다. 눈에 확 띄면 대조가 아니라 술래잡기가 된다
                var shift = Rng.Next(2) == 0 ? 1 : -1;
                _rows[i].Actual = (int.Parse(_rows[i].Ledger) + shift).ToString();
                picked += 1;
            }
        }

        private string Prefix() => Spec?.variant switch
        {
            "serial" => "SN",
            "tally" => "AM",
            "pipetemp" => "PP",
            "cable" => "CB",
            "timelog" => "TX",
            "headcount" => "PS",
            "requisition" => "RQ",
            "logbook" => "GT",
            "portion" => "MS",
            _ => "CK",
        };

        protected override void Advance(float dt, BoardInput input)
        {
            if (!input.Pressed || _rows == null || _area.width <= 0f) return;

            for (var i = 0; i < _rows.Length; i += 1)
            {
                if (!RowRect(i).Contains(input.Mouse)) continue;
                var row = _rows[i];
                if (row.Flagged) return;

                row.Flagged = true;
                if (row.Mismatch)
                {
                    _found += 1;
                    Fill = (float)_found / _need;
                    if (_found >= _need) Clear();
                    return;
                }

                // 헛짚었다. 표시는 남는다 — 무엇을 잘못 봤는지 보여야 배운다
                row.Wrong = true;
                _strikes += 1;
                Miss();
                if (_strikes >= _limit) Fail();
                return;
            }
        }

        private Rect RowRect(int index)
        {
            var perColumn = Mathf.CeilToInt((float)_rows.Length / _columns);
            var column = index / perColumn;
            var line = index % perColumn;
            var width = _area.width / _columns;
            var height = Mathf.Min(34f, _area.height / perColumn);
            return new Rect(_area.x + column * width + 6f,
                            _area.y + line * height,
                            width - 12f,
                            height - 3f);
        }

        public override void Draw(HudTheme theme, Rect body)
        {
            _area = body;
            if (_rows == null) return;

            theme.Fill(body, HudTheme.Paper3);

            for (var i = 0; i < _rows.Length; i += 1)
            {
                var row = _rows[i];
                var rect = RowRect(i);

                var fill = row.Wrong ? HudTheme.AlertW
                         : row.Flagged ? HudTheme.AccentW
                         : HudTheme.Paper;
                theme.Fill(rect, fill);
                theme.Border(rect, row.Wrong ? HudTheme.Alert
                                 : row.Flagged ? HudTheme.Accent
                                 : HudTheme.Rule3);

                GUI.Label(new Rect(rect.x + 10f, rect.y, 92f, rect.height), row.Code,
                    theme.At(theme.Label, 13, HudTheme.Ink2));

                // 대장 — 실물. 가운데 화살표가 "대조"라는 것을 말한다
                GUI.Label(new Rect(rect.x + 110f, rect.y, 60f, rect.height), row.Ledger,
                    theme.At(theme.Mono, 16, HudTheme.Ink3, TextAnchor.MiddleRight));
                GUI.Label(new Rect(rect.x + 176f, rect.y, 24f, rect.height), "→",
                    theme.At(theme.Label, 13, HudTheme.Rule, TextAnchor.MiddleCenter));
                GUI.Label(new Rect(rect.x + 204f, rect.y, 60f, rect.height), row.Actual,
                    theme.At(theme.Mono, 16,
                        row.Flagged && row.Mismatch ? HudTheme.Accent : HudTheme.Ink));

                if (row.Flagged && row.Mismatch)
                {
                    GUI.Label(new Rect(rect.xMax - 60f, rect.y, 50f, rect.height), "적발",
                        theme.At(theme.Label, 12, HudTheme.Accent, TextAnchor.MiddleRight));
                }
                else if (row.Wrong)
                {
                    GUI.Label(new Rect(rect.xMax - 60f, rect.y, 50f, rect.height), "오답",
                        theme.At(theme.Label, 12, HudTheme.Alert, TextAnchor.MiddleRight));
                }
            }

            theme.Border(body, HudTheme.Rule);
        }
    }
}
