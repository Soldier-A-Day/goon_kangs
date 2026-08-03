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

        /// <summary>연속으로 정답만 적발한 횟수 — 오답 한 번에 끊긴다. 도장 소리 피치를 여기서 올린다</summary>
        private int _correctStreak;

        /// <summary>공통 — 마지막 3초 심장 박동까지 남은 시간(초)</summary>
        private float _heartbeatCd;
        private const float HeartbeatInterval = 0.5f;

        /* ───────────────────────────────────────────── H-4 키보드 포커스 */

        /// <summary>지금 포커스된 줄. `_rows.Length`면 "이상 없음 보고" 버튼이다</summary>
        private int _focus;
        /// <summary>버튼에서 위로 나갈 때 돌아갈 칸 — 줄 포커스가 바뀔 때마다 갱신한다</summary>
        private int _focusColumn;
        private float _hNavCooldown;
        private float _vNavCooldown;
        private const float NavCooldown = 0.16f;

        /// <summary>포커스 테두리를 보여줄지 — 마우스를 쓰는 동안은 감춘다(발주 §공통 패턴)</summary>
        private bool _usingKeyboard = true;
        private Vector2 _lastMouseForFocus = new Vector2(-1f, -1f);

        public override string Instruction =>
            "대장과 다른 줄을 찍어라 — 정말 없으면 '이상 없음'(↑↓←→ · Space)";

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
            _focus = 0;
            _focusColumn = 0;
            _hNavCooldown = 0f;
            _vNavCooldown = 0f;
            _usingKeyboard = true;
            _lastMouseForFocus = new Vector2(-1f, -1f);
            _correctStreak = 0;

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

            // 어긋난 줄을 고른다 (`_need`가 0이면, 즉 깨끗한 판이면 그냥 안 돈다).
            //
            // A-5 벤치마크 이식 S3 — Papers, Please. 숫자 ±1 하나뿐이면 몇 판
            // 지나면 "자리만 훑으면 된다"는 요령이 생긴다. 그래서 남은 함정이
            // 둘 이상이면 시드 확률로 "군번 끝자리 교환" 변형을 섞는다 — 두 줄의
            // 실물 값이 서로 자리를 바꾼다. 각 값 자체는 멀쩡한 숫자라 대장과
            // 나란히 놓고 읽어야만 보이고, 옆 줄까지 같이 봐야 잡힌다.
            var picked = 0;
            while (picked < _need)
            {
                var pairTrap = _need - picked >= 2 && Rng.NextDouble() < 0.35;
                if (pairTrap)
                {
                    var i = Rng.Next(count);
                    var j = Rng.Next(count);
                    if (i == j || _rows[i].Mismatch || _rows[j].Mismatch) continue;
                    if (_rows[i].Ledger == _rows[j].Ledger) continue; // 값이 같으면 바꿔도 안 틀린다

                    var li = _rows[i].Ledger;
                    var lj = _rows[j].Ledger;
                    _rows[i].Mismatch = true;
                    _rows[j].Mismatch = true;
                    _rows[i].Actual = lj;
                    _rows[j].Actual = li;
                    picked += 2;
                    continue;
                }

                var k = Rng.Next(count);
                if (_rows[k].Mismatch) continue;
                _rows[k].Mismatch = true;
                // 한 자리만 다르다. 눈에 확 띄면 대조가 아니라 술래잡기가 된다
                var shift = Rng.Next(2) == 0 ? 1 : -1;
                _rows[k].Actual = (int.Parse(_rows[k].Ledger) + shift).ToString();
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
            TickHeartbeat(dt);

            if (_rows == null || _area.width <= 0f) return;

            UpdateFocusNav(dt, input);

            if (input.Pressed)
            {
                _usingKeyboard = false;
                for (var i = 0; i < _rows.Length; i += 1)
                {
                    if (!RowRect(i).Contains(input.Mouse)) continue;
                    PickRow(i);
                    return;
                }

                // 버튼과 줄은 영역이 겹치지 않으니 순서는 상관없다(§과업 5) —
                // 위 줄 순회에서 못 맞았을 때만 온다
                if (ReportRect().Contains(input.Mouse)) PickReport();
                return;
            }

            // H-4 — 클릭 대신 Space·Enter. 지금 포커스가 가리키는 줄(또는
            // "이상 없음" 버튼)을 고른다
            if (!input.Confirm) return;
            _usingKeyboard = true;
            if (_focus < _rows.Length) PickRow(_focus);
            else PickReport();
        }

        /// <summary>줄 하나를 골랐다 — 마우스로 짚었든 포커스에서 Space를 눌렀든 결과는 같다</summary>
        private void PickRow(int i)
        {
            var row = _rows[i];
            if (row.Flagged) return;

            row.Flagged = true;
            if (row.Mismatch)
            {
                _found += 1;
                _correctStreak += 1;
                Fill = (float)_found / _need;
                if (_found >= _need)
                {
                    Clear();
                    return;
                }

                // A-5 벤치마크 이식 S3 — 연속 적발 콤보. 도장을 찍을 때마다
                // 피치가 경쾌하게 올라간다(`Clear`의 success 소리와 겹치지
                // 않게 마지막 한 발은 위에서 이미 return했다). 너무 새된
                // 소리가 되지 않게 상한을 둔다
                var pitch = Mathf.Min(1.6f, 1f + _correctStreak * 0.08f);
                Sfx.Play("tap", 1f, pitch);
                return;
            }

            // 헛짚었다. 표시는 남는다 — 무엇을 잘못 봤는지 보여야 배운다
            row.Wrong = true;
            _strikes += 1;
            _correctStreak = 0;
            Miss();
            if (_strikes >= _limit) Fail();
        }

        /// <summary>"이상 없음 보고" — 깨끗한 판이면 정답, 어긋남이 남아 있으면 오답 1회다
        /// (줄을 잘못 짚은 것과 같은 대가)</summary>
        private void PickReport()
        {
            if (_clean)
            {
                Fill = 1f;
                Clear();
                return;
            }

            _strikes += 1;
            _correctStreak = 0;
            Miss();
            if (_strikes >= _limit) Fail();
        }

        /// <summary>한 칸에 실제로 들어 있는 줄 수 — 짝이 안 맞으면 마지막 칸이 더 짧을 수 있다</summary>
        private int PerColumn => Mathf.Max(1, Mathf.CeilToInt((float)_rows.Length / _columns));

        private int ColumnCount(int column)
        {
            var start = column * PerColumn;
            return Mathf.Clamp(_rows.Length - start, 0, PerColumn);
        }

        /// <summary>
        /// 화살표·WASD로 포커스를 옮긴다.
        ///
        /// 마우스가 이번 프레임에 움직이면 포커스 테두리를 감춘다 — 마우스를
        /// 쓰는 사람 화면에 낯선 테두리가 남아 있으면 안 된다(발주 §공통
        /// 패턴 "마우스 사용 중엔 숨김"). `SortBoard`의 좌우 이동 쿨다운과
        /// 같은 방식으로 축마다 따로 눌림을 뗀다.
        /// </summary>
        private void UpdateFocusNav(float dt, BoardInput input)
        {
            if (_lastMouseForFocus.x >= 0f && input.Mouse != _lastMouseForFocus) _usingKeyboard = false;
            _lastMouseForFocus = input.Mouse;

            _vNavCooldown -= dt;
            if (Mathf.Abs(input.Vertical) > 0.5f)
            {
                if (_vNavCooldown <= 0f)
                {
                    // 화면 y는 아래로 갈수록 커진다(BoardInput.Mouse) — 위쪽
                    // 화살표(Vertical +1)는 줄을 하나 앞으로(-1) 옮긴다
                    MoveFocusVertical(input.Vertical > 0f ? -1 : 1);
                    _vNavCooldown = NavCooldown;
                    _usingKeyboard = true;
                }
            }
            else _vNavCooldown = 0f;

            _hNavCooldown -= dt;
            if (Mathf.Abs(input.Horizontal) > 0.5f)
            {
                if (_hNavCooldown <= 0f)
                {
                    MoveFocusHorizontal(input.Horizontal > 0f ? 1 : -1);
                    _hNavCooldown = NavCooldown;
                    _usingKeyboard = true;
                }
            }
            else _hNavCooldown = 0f;
        }

        /// <summary>공통 — 마지막 3초 하이라이트. 심장 박동처럼 0.5초 간격으로 저음 tap을 운다</summary>
        private void TickHeartbeat(float dt)
        {
            if (Remaining > 3f || Remaining <= 0f)
            {
                _heartbeatCd = 0f;
                return;
            }

            _heartbeatCd -= dt;
            if (_heartbeatCd > 0f) return;
            Sfx.Play("tap", 0.85f, 0.55f);
            _heartbeatCd = HeartbeatInterval;
        }

        private void MoveFocusVertical(int delta)
        {
            if (_rows == null || _rows.Length == 0) return;
            var perColumn = PerColumn;

            if (_focus >= _rows.Length)
            {
                // 버튼에 있다 — 위로만 나갈 수 있고, 마지막으로 있던 칸의
                // 맨 아래 줄로 돌아간다
                if (delta < 0)
                {
                    var lastLine = Mathf.Max(0, ColumnCount(_focusColumn) - 1);
                    _focus = _focusColumn * perColumn + lastLine;
                }
                return;
            }

            var column = _focus / perColumn;
            var line = _focus % perColumn + delta;
            var count = ColumnCount(column);

            if (line >= count)
            {
                // 이 칸의 마지막 줄 아래로 내려갔다 — 보고 버튼으로 넘어간다
                _focusColumn = column;
                _focus = _rows.Length;
                return;
            }

            if (line < 0) line = 0;
            _focus = column * perColumn + line;
        }

        private void MoveFocusHorizontal(int delta)
        {
            if (_rows == null || _rows.Length == 0 || _focus >= _rows.Length) return; // 버튼에선 좌우 이동이 없다

            var perColumn = PerColumn;
            var column = Mathf.Clamp(_focus / perColumn + delta, 0, _columns - 1);
            var count = ColumnCount(column);
            if (count <= 0) return; // 짝이 안 맞아 그 칸엔 줄이 아예 없다

            var line = Mathf.Min(_focus % perColumn, count - 1);
            _focus = column * perColumn + line;
        }

        /// <summary>줄 영역. 하단 `ReportAreaH`만큼은 버튼 몫이라 여기서 뺀다</summary>
        private Rect RowRect(int index)
        {
            var perColumn = PerColumn;
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

                // H-4 — 키보드 포커스 테두리. 마우스를 쓰는 동안은 감춘다
                if (_usingKeyboard && _focus == i) theme.Border(rect, HudTheme.Heat, 3f);
            }

            DrawReportButton(theme);
            theme.Border(body, HudTheme.Rule);

            // 공통 — 마지막 3초 하이라이트. 본문 위 가장자리를 심장 박동에 맞춰 깜빡인다
            if (Remaining <= 3f && Remaining > 0f && HudTheme.Pulse(2f))
            {
                theme.Fill(new Rect(body.x, body.y, body.width, 4f), HudTheme.Alert, 0.85f);
            }
        }

        /// <summary>
        /// "이상 없음 보고" 버튼. 깨끗한 판에만 그리면 버튼의 존재 자체가
        /// 답을 누설하므로 전 판에 똑같이 그린다. 마우스는 여기서 직접
        /// 읽는다 — `HudMinigame`의 결과창 버튼(`Retry`·`Quit`)과 같은
        /// 방식이다. 실제 클릭 판정은 `Advance`가 `input.Pressed`로 한다.
        /// 키보드로 포커스가 여기 와 있으면(`_focus >= _rows.Length`) 마우스가
        /// 안 얹혀 있어도 같은 강조를 켠다.
        /// </summary>
        private void DrawReportButton(HudTheme theme)
        {
            var rect = ReportRect();
            var focused = _usingKeyboard && _focus >= _rows.Length;
            var hot = rect.Contains(BoardInput.Read().Mouse) || focused;
            theme.Fill(rect, hot ? HudTheme.AccentW : HudTheme.Paper3);
            theme.Border(rect, hot ? HudTheme.Accent : HudTheme.Rule3, focused ? 3f : 1f);
            GUI.Label(rect, "이상 없음 보고",
                theme.At(theme.Body, 16, hot ? HudTheme.Accent : HudTheme.Ink2,
                    TextAnchor.MiddleCenter));
        }
    }
}
