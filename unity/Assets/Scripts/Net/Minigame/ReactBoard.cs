using UnityEngine;

namespace SoldierADay.Net
{
    /// <summary>
    /// `REACT` 즉시 반응 — 단독으로 쓰는 것은 `간부 순찰` 하나뿐이다.
    ///
    /// 쉘(패널) 하나가 색으로 신호를 준다. 평소엔 초록 — 안전하다는 뜻이고,
    /// 시드 고정 무작위 간격 뒤 빨강으로 바뀐 순간이 곧 누를 때다. 단발
    /// 입력 하나뿐이다.
    ///
    /// **나머지는 전부 인터럽트다.** 다른 원형 위에 끼워 넣는 모디파이어이고
    /// (`minigame.interrupt`), 그렇게 써야 돌발이 "일과와 같은 게임인데 이름만
    /// 다른 것"이 되지 않는다 — 진행 중인 판을 끊고 들어오는 것이 돌발의 성격이다.
    ///
    /// 미리 누르면 안 된다. 빨강으로 바뀌기 전에 누르는 것도 틀린 것이고,
    /// 그래서 **기다림이 이 판의 조작**이다. 판정 구조(`_next`/`_open`/
    /// `_window`/`Schedule`)는 원래 시야 원뿔 버전 그대로다 — 색으로
    /// 바뀐 것은 겉모습뿐이라 갈아 끼울 이유가 없었다.
    ///
    /// ── 긴장감 패스 B — 결투의 정적 ───────────────────────────────────────
    /// 기다리는 동안 그냥 "…"으로 흘려보내지 않는다 — 대기가 길어질수록
    /// 화면이 조금씩 어두워진다("정적이 짙어진다"는 것을 밝기로 옮긴 것).
    /// 판마다 딱 한 번, 진짜 신호가 아닌 **노란 페이크**("대기!" 문구, 0.5초)가
    /// 뜬다 — 색도 문구도 진짜(빨강·"지금!")와 다르니 몰래 넣는 함정이 아니라
    /// "규칙 안에서의 배신"이다(설계안 §트위스트의 철칙). 누르면 그냥 평소의
    /// 성급한 판정과 같은 값을 받는다 — 새 실패 사유를 만들지 않는다. 성공하면
    /// 반응 속도(ms)와 등급 문구를 보태 준다 — 판정 자체는 안 건드리는 보상이다.
    /// </summary>
    public sealed class ReactBoard : Board
    {
        private float _next;
        private float _open;
        private float _window;
        private int _done;
        private int _need;
        private float _flash;
        private string _verdict = "";

        /* ──────────────────────────────────────── 긴장감 패스 B — 결투의 정적 */

        /// <summary>지금 기다린 지 얼마나 됐나(초) — 화면 어둠의 기준</summary>
        private float _waitElapsed;

        /// <summary>페이크가 뜨기로 한 경과 시각(초) — 판 전체에서 딱 한 번</summary>
        private float _fakeAt;
        private bool _fakeShown;
        /// <summary>페이크가 화면에 남은 시간(초)</summary>
        private float _fakeT;

        /// <summary>이번 성공의 반응 속도(ms)와 등급 — Draw가 "경례" 밑에 덧붙인다</summary>
        private float _lastReactionMs;
        private string _lastReactionLabel = "";

        /// <summary>마지막 3초 — 공통 하이라이트용 박동 타이머</summary>
        private float _finalBeatTimer;

        public override string Instruction => "쉘이 빨갛게 바뀌는 순간 눌러라 — 노란 예비 신호에 속지 마라";

        public override string Status => $"경례 {_done}/{_need}";

        protected override void Setup()
        {
            _need = Mathf.Clamp(ParamInt("count", 3), 1, 6);
            _window = Param("window", 1.4f) * Mathf.Lerp(1f, 0.72f, (Difficulty - 1f) * 0.5f);
            _done = 0;
            _open = 0f;
            // 페이크는 판 전체에서 한 번뿐이다 — 앞쪽 절반 어딘가에 시드 고정으로 심는다
            _fakeAt = RandRange(Limit * 0.15f, Limit * 0.6f);
            _fakeShown = false;
            _fakeT = 0f;
            Schedule();
        }

        /// <summary>다음 등장까지. 간격이 일정하면 세고 있으면 되므로 흔든다</summary>
        private void Schedule()
        {
            _next = RandRange(1.1f, Mathf.Max(1.6f, Limit / (_need + 1f)));
            _waitElapsed = 0f;
        }

        protected override void Advance(float dt, BoardInput input)
        {
            _flash = Mathf.Max(0f, _flash - dt * 2.5f);
            FinalStretchTick(dt);

            if (_fakeT > 0f) _fakeT -= dt;
            if (!_fakeShown && _open <= 0f && Elapsed >= _fakeAt)
            {
                // 대기 중에만 심는다 — 진짜 신호와 겹치면 눈에 안 읽힌다
                _fakeShown = true;
                _fakeT = 0.5f;
            }

            if (_open > 0f)
            {
                _open -= dt;
                if (_open <= 0f)
                {
                    // 지나갔다. 못 본 것이다
                    _verdict = "놓쳤다";
                    _flash = 1f;
                    Miss();
                    Schedule();
                }
            }
            else
            {
                _waitElapsed += dt;
                _next -= dt;
                if (_next <= 0f) _open = _window;
            }

            if (!input.Tap) return;

            if (_open > 0f)
            {
                // 반응 속도 = 창이 열린 뒤 지난 시간. `_window - _open`이 그 값이다
                _lastReactionMs = (_window - _open) * 1000f;
                _lastReactionLabel = _lastReactionMs < 220f ? "전광석화"
                                    : _lastReactionMs < 450f ? "양호"
                                    : "굼벵이";

                _done += 1;
                _verdict = "경례";
                _flash = 1f;
                _open = 0f;
                Fill = (float)_done / _need;
                Schedule();
                if (_done >= _need) Clear();
                return;
            }

            // 아무도 없는데 경례했다 — 페이크에 속았을 때도 똑같은 값을 받는다.
            // 페이크는 색과 문구로만 유혹할 뿐, 성급 판정 자체를 바꾸지 않는다
            _verdict = "성급했다";
            _flash = 1f;
            Miss();
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

        public override void Draw(HudTheme theme, Rect body)
        {
            theme.Fill(body, HudTheme.Paper3);

            // 정적 연출 — "…" 대신 기다림이 길어질수록 화면이 조금씩 어두워진다.
            // 지수 포화라 무한정 캄캄해지지는 않는다(최대 0.4)
            if (_open <= 0f)
            {
                var dim = (1f - Mathf.Exp(-_waitElapsed * 0.35f)) * 0.4f;
                theme.Fill(body, HudTheme.Dim, dim);
            }

            var shell = new Rect(body.center.x - 150f, body.center.y - 90f, 300f, 170f);
            var live = _open > 0f;
            var fake = !live && _fakeT > 0f;

            if (fake)
            {
                // 노랑(Admin — 팔레트의 유일한 황금색) + "대기!" — 색도 문구도
                // 진짜(빨강·"지금!")와 다르다. 눌러도 판정은 그냥 성급함이다
                theme.Fill(shell, HudTheme.Admin, 0.22f);
                theme.Border(shell, HudTheme.Admin, 3f);
                GUI.Label(shell, "대기!", theme.At(theme.Display, 40, HudTheme.Admin, TextAnchor.MiddleCenter));
            }
            else
            {
                // 색만으로 안 읽히는 색각 대응 — 문구도 같이 바뀐다
                theme.Fill(shell, live ? HudTheme.AlertW : HudTheme.AccentW);
                theme.Border(shell, live ? HudTheme.Alert : HudTheme.Accent, live ? 3f : 2f);
                GUI.Label(shell, live ? "지금!" : "대기",
                    theme.At(theme.Display, live ? 48 : 30,
                        live ? HudTheme.Alert : HudTheme.Accent, TextAnchor.MiddleCenter));
            }

            // 남은 창 — 언제까지 누를 수 있는가
            if (live)
            {
                var bar = new Rect(shell.x, shell.yMax + 10f, shell.width, 8f);
                theme.Fill(bar, HudTheme.Rule3);
                theme.Fill(new Rect(bar.x, bar.y, bar.width * (_open / _window), bar.height),
                           HudTheme.Alert);
            }

            if (_flash > 0f)
            {
                GUI.Label(new Rect(body.x, shell.yMax + 30f, body.width, 32f), _verdict,
                    theme.At(theme.Heading, 20,
                        _verdict == "경례" ? HudTheme.Accent : HudTheme.Alert,
                        TextAnchor.MiddleCenter));

                // 반응 속도 — 성공했을 때만. ms와 등급 문구를 같이 보여준다
                if (_verdict == "경례")
                {
                    GUI.Label(new Rect(body.x, shell.yMax + 62f, body.width, 26f),
                        $"{Mathf.RoundToInt(_lastReactionMs)}ms · {_lastReactionLabel}",
                        theme.At(theme.Small, 15, HudTheme.Ink2, TextAnchor.MiddleCenter));
                }
            }

            for (var i = 0; i < _need; i += 1)
            {
                var dot = new Rect(body.center.x - _need * 13f + i * 26f, body.y + 26f, 18f, 18f);
                theme.Fill(dot, i < _done ? HudTheme.Accent : HudTheme.Rule2);
            }

            theme.Border(body, HudTheme.Rule);
            FinalStretchDraw(theme, body);
        }
    }
}
