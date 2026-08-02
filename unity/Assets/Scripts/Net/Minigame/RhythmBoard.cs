using UnityEngine;

namespace SoldierADay.Net
{
    /// <summary>
    /// `RHYTHM` 박자 — 3건. 태극기 게양·강하 · 제식 훈련 · 불시 점호.
    ///
    /// 박이 다가오면 맞춰 누른다. 단발 입력 하나뿐이다.
    ///
    /// **`TIMING`과 다른 점은 템포가 고정이라는 것이다.** 타이밍 바는 창이
    /// 옮겨 다녀서 매번 새로 겨눠야 하지만, 여기는 박이 일정해서 **몸이 외운다** —
    /// 국기 하강식의 템포가 고정인 것도, 제식이 옆 사람과 맞아야 하는 것도
    /// 그래서다.
    ///
    /// `combo`만큼 연속으로 맞춰야 통과한다. 중간에 놓치면 콤보가 끊기고,
    /// 남은 박 안에서 다시 쌓아야 한다.
    /// </summary>
    public sealed class RhythmBoard : Board
    {
        private float _period;
        private float _clock;
        private int _beat;
        private int _beats;
        private int _combo;
        private int _best;
        private int _need;
        private bool _armed;
        private float _flash;
        private bool _good;

        public override string Instruction => "[SPACE] 또는 클릭 — 박에 맞춰라";

        public override string Status =>
            $"콤보 {_combo}/{_need}  ·  최고 {_best}  ·  박 {_beat}/{_beats}";

        protected override void Setup()
        {
            var bpm = Mathf.Max(30f, Param("bpm", 72f));
            _period = 60f / bpm;
            _beats = Mathf.Max(4, ParamInt("beats", 16));
            _need = Mathf.Clamp(ParamInt("combo", 10), 2, _beats);
            _clock = 0f;
            _beat = 0;
            _combo = 0;
            _best = 0;
            _armed = true;
        }

        protected override void Advance(float dt, BoardInput input)
        {
            _flash = Mathf.Max(0f, _flash - dt * 4f);
            _clock += dt;

            // 창은 박 앞뒤로 열린다. 난이도가 오르면 좁아진다
            var window = Mathf.Lerp(0.17f, 0.10f, (Difficulty - 1f) * 0.5f);

            if (_clock >= _period)
            {
                _clock -= _period;
                _beat += 1;

                // 못 누른 채로 박이 지나갔다
                if (_armed)
                {
                    _combo = 0;
                    _flash = 1f;
                    _good = false;
                    Miss();
                }
                _armed = true;

                if (_beat >= _beats) { Fail(); return; }
            }

            if (!input.Tap) return;

            // 박까지 남은 시간 — 앞뒤 어느 쪽이든 가까우면 맞은 것이다
            var offset = Mathf.Min(_clock, _period - _clock);
            if (!_armed || offset > window * _period)
            {
                _combo = 0;
                _flash = 1f;
                _good = false;
                Miss();
                return;
            }

            _armed = false;
            _combo += 1;
            _best = Mathf.Max(_best, _combo);
            _flash = 1f;
            _good = true;
            Fill = (float)_combo / _need;
            if (_combo >= _need) Clear();
        }

        public override void Draw(HudTheme theme, Rect body)
        {
            theme.Fill(body, HudTheme.Paper3);

            // 다가오는 박 — 고리가 좁혀 들어와 가운데 원에 겹치는 순간이 박이다
            var center = new Vector2(body.center.x, body.center.y - 10f);
            var core = new Rect(center.x - 30f, center.y - 30f, 60f, 60f);
            theme.Fill(core, HudTheme.Paper);
            theme.Border(core, HudTheme.Accent, 2f);

            var t = _clock / _period;
            var r = Mathf.Lerp(150f, 30f, t);
            theme.Border(new Rect(center.x - r, center.y - r, r * 2f, r * 2f),
                _armed ? HudTheme.Ink : HudTheme.Rule2, 2f);
            // 다음 박도 미리 보인다 — 템포가 몸에 붙는다
            var r2 = Mathf.Lerp(150f, 30f, Mathf.Repeat(t + 1f, 2f) * 0.5f);
            theme.Border(new Rect(center.x - r2, center.y - r2, r2 * 2f, r2 * 2f),
                HudTheme.Rule3, 1f);

            if (_flash > 0f)
            {
                GUI.Label(new Rect(body.x, center.y + 66f, body.width, 32f),
                    _good ? "맞았다" : "어긋났다",
                    theme.At(theme.Heading, 20, _good ? HudTheme.Accent : HudTheme.Alert,
                        TextAnchor.MiddleCenter));
            }

            // 콤보 — 이 판이 세는 것
            var bar = new Rect(body.x + 60f, body.yMax - 46f, body.width - 120f, 18f);
            theme.Bar(bar, Mathf.Clamp01((float)_combo / _need), HudTheme.Accent);
            GUI.Label(new Rect(bar.x, bar.y - 24f, 200f, 20f), $"콤보 {_combo}/{_need}",
                theme.At(theme.Label, 12, HudTheme.Ink2));

            // 남은 박
            GUI.Label(new Rect(bar.xMax - 200f, bar.y - 24f, 200f, 20f),
                $"남은 박 {Mathf.Max(0, _beats - _beat)}",
                theme.At(theme.Label, 12, HudTheme.Ink3, TextAnchor.MiddleRight));

            theme.Border(body, HudTheme.Rule);
        }
    }
}
