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
    ///
    /// ── 약 12%는 깨끗한 판이다 ────────────────────────────────────────────
    /// 예전에는 어긋난 줄이 판마다 반드시 있었다. 그러면 "정말 이상이 없는가"라는
    /// 질문 자체가 없어진다 — 찾을 때까지 찍으면 언젠가는 맞는다. `Papers,
    /// Please` 계열의 진짜 긴장은 "없다"고 스스로 판단해 서명하는 순간에 있다.
    /// 그래서 시드 고정 `Rng`로 약 12% 확률로 mismatch 0건인 판을 만들고, 그
    /// 판에도 "이상 없음 보고" 버튼을 상시 둔다(깨끗한 판에만 버튼을 두면 버튼의
    /// 존재 자체가 답을 누설한다 — §과업 2).
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

        /// <summary>"이상 없음 보고" 버튼 한 줄 높이 + 위아래 여백</summary>
        private const float ReportAreaH = 52f;
        private const float ReportBtnH = 40f;

        private Row[] _rows;
        private bool _clean;
        private int _found;
        private int _need;
        private int _strikes;
        private int _limit;
        private int _columns = 1;
        private Rect _area;

        public override string Instruction =>
            "대장과 다른 줄을 찍어라 — 정말 없으면 '이상 없음'";

        // **총수는 어디에도 새지 않는다.** 예전 `적발 {_found}/{_need}`는 어긋난
        // 판에서도 목표 총수를 미리 알려줬다 — 그러면 다 찾기 전에도 "몇 개
        // 남았다"가 세어져서 깨끗한 판과 구분이 됐다. 깨끗한 판·어긋난 판이
        // 화면에서 똑같이 보이도록 목표 수는 빼고 지금까지 찍은 수만 보여준다.
        public override string Status =>
            $"적발 {_found}  ·  오답 {_strikes}/{_limit}";

        protected override void Setup()
        {
            var count = Mathf.Clamp(ParamInt("entries", 12), 4, 20);
            _limit = Mathf.Max(1, ParamInt("strikes", 3));
            _found = 0;
            _strikes = 0;
            _columns = count > 10 ? 2 : 1;

            // 깨끗한 판인지 먼저 굴린다 — 같은 questId는 재시도에도 같은 값이
            // 나온다(`Rng`가 questId로 시드 고정된 것을 `Board.Begin`이 보장한다).
            _clean = Rng.NextDouble() < 0.12;
            _need = _clean ? 0 : Mathf.Clamp(ParamInt("mismatches", 3), 1, count);

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

            // 어긋난 줄을 고른다 (`_need`가 0이면, 즉 깨끗한 판이면 그냥 안 돈다)
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

            // "이상 없음 보고" — 깨끗한 판이면 정답, 어긋남이 남아 있으면 오답
            // 1회다(줄을 잘못 짚은 것과 같은 대가). 버튼과 줄은 영역이 겹치지
            // 않으니 순서는 상관없다(§과업 5) — 위 줄 순회에서 못 맞았을 때만 온다
            if (!ReportRect().Contains(input.Mouse)) return;

            if (_clean)
            {
                Fill = 1f;
                Clear();
                return;
            }

            _strikes += 1;
            Miss();
            if (_strikes >= _limit) Fail();
        }

        /// <summary>줄 영역. 하단 `ReportAreaH`만큼은 버튼 몫이라 여기서 뺀다</summary>
        private Rect RowRect(int index)
        {
            var perColumn = Mathf.CeilToInt((float)_rows.Length / _columns);
            var column = index / perColumn;
            var line = index % perColumn;
            var width = _area.width / _columns;
            var rowsHeight = Mathf.Max(0f, _area.height - ReportAreaH);
            var height = Mathf.Min(34f, rowsHeight / perColumn);
            return new Rect(_area.x + column * width + 6f,
                            _area.y + line * height,
                            width - 12f,
                            height - 3f);
        }

        /// <summary>판 하단에 상시 고정된 "이상 없음 보고" 버튼 자리</summary>
        private Rect ReportRect() =>
            new Rect(_area.x + 6f, _area.yMax - ReportBtnH - 6f,
                     _area.width - 12f, ReportBtnH);

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

            DrawReportButton(theme);
            theme.Border(body, HudTheme.Rule);
        }

        /// <summary>
        /// "이상 없음 보고" 버튼. 깨끗한 판에만 그리면 버튼의 존재 자체가
        /// 답을 누설하므로 전 판에 똑같이 그린다. 마우스는 여기서 직접
        /// 읽는다 — `HudMinigame`의 결과창 버튼(`Retry`·`Quit`)과 같은
        /// 방식이다. 실제 클릭 판정은 `Advance`가 `input.Pressed`로 한다.
        /// </summary>
        private void DrawReportButton(HudTheme theme)
        {
            var rect = ReportRect();
            var hot = rect.Contains(BoardInput.Read().Mouse);
            theme.Fill(rect, hot ? HudTheme.AccentW : HudTheme.Paper3);
            theme.Border(rect, hot ? HudTheme.Accent : HudTheme.Rule3);
            GUI.Label(rect, "이상 없음 보고",
                theme.At(theme.Body, 16, hot ? HudTheme.Accent : HudTheme.Ink2,
                    TextAnchor.MiddleCenter));
        }
    }
}
