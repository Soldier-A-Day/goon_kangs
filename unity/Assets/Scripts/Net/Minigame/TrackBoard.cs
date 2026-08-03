using UnityEngine;

namespace SoldierADay.Net
{
    /// <summary>
    /// `TRACK` — 2건(사주경계 · 경계근무), 풍선 터트리기.
    ///
    /// 원래는 표적 위에 커서를 얹고 붙잡고 있는 "조준 유지"였다. 유지형 조작은
    /// 마우스를 붙박아 두는 것 말고 할 게 없어서 반응이 얇다 — 사용자 지시로
    /// 조준 유지를 걷어내고, 판 안 무작위 자리에 **한 번에 하나씩** 뜨는 풍선을
    /// 클릭으로 터트리는 판으로 통째로 갈아 끼운다. 조작은 커서 + 클릭 하나뿐.
    ///
    /// 풍선은 뜨고 나서 일정 시간(난이도가 오르면 짧아진다) 안에 못 터트리면
    /// 쪼그라들며 사라지고 Miss가 하나 쌓인다 — 그리고 바로 다음 풍선이 뜬다.
    /// **놓쳐도 목표 개수는 줄지 않는다.** 터트린 개수가 목표(`targets`)에
    /// 닿아야 끝나므로, 놓칠수록 뒤에 보충이 나와 제한 시간만 더 빠듯해진다 —
    /// 잃는 것은 시간이지 기회가 아니다.
    ///
    /// 빗나간 클릭(풍선 밖을 짚은 것)은 벌점이 없다. 여기서 벌을 주면 "일단
    /// 눌러 보기"가 위험해져서, 반응해야 하는 판에서 오히려 조심스러워진다.
    ///
    /// 옛 조준 유지의 `speed`·`holdRatio`·`variant`(졸음 겹침)는 이 설계에는
    /// 쓸 자리가 없어 더 이상 읽지 않는다 — 사용자 지시가 지목한 범위는
    /// `targets` 하나뿐이다.
    ///
    /// ── 긴장감 패스 B — Fruit Ninja 블리츠 ────────────────────────────────
    /// 연속으로 터뜨릴수록(콤보) 1~3단 티어가 오른다. 티어가 오르면 다음
    /// 풍선까지 뜸을 들이던 간격이 짧아지고 터짐음 피치가 올라가 "몰아친다"는
    /// 감각을 준다 — 그리고 풍선 자체의 수명도 짧아진다(2.0 → 0.8초). 3~4개
    /// 중 하나는 색·무늬가 분명히 다른 **지뢰 풍선**이라 터뜨리면 콤보가
    /// 통째로 날아간다 — 몰아치는 손이 오히려 발목을 잡는 순간이다. 목표
    /// 개수(`targets`)와 통과 조건은 손대지 않는다 — 조여지는 것은 리듬뿐이다.
    /// </summary>
    public sealed class TrackBoard : Board
    {
        /// <summary>지금 뜬 풍선의 판 안 정규 좌표(0~1)</summary>
        private Vector2 _pos;

        /// <summary>지금 뜬 풍선이 나온 지 얼마나 지났나(초)</summary>
        private float _age;

        private int _popped;
        private int _need;
        private int _missed;
        private Rect _area;

        /// <summary>터짐 연출 — 남은 세기(1→0). 위치·반지름은 터진 순간 값을 그대로 쓴다</summary>
        private float _burstT;
        private Vector2 _burstPos;
        private float _burstRadius;
        /// <summary>터진 것이 지뢰였는가 — 터짐 링 색을 가른다</summary>
        private bool _burstMine;

        /// <summary>풍선이 다 차오르는 시간 — 뜨자마자 꽉 차면 팝이 아니라 그냥 나타나는 것이다</summary>
        private const float SpawnGrow = 0.15f;

        /// <summary>수명 마지막 이만큼(초)은 깜빡이며 경고한다</summary>
        private const float WarnWindow = 0.4f;

        /// <summary>터짐 연출이 지속되는 시간(초) — 한두 프레임 느낌만 나면 된다</summary>
        private const float BurstDuration = 0.18f;

        /// <summary>H-4 — 커서 자리를 마우스 대신 가상 커서에서 읽는다(Board.cs §VirtualCursor)</summary>
        private readonly VirtualCursor _cursor = new VirtualCursor();

        /* ──────────────────────────────────────── 긴장감 패스 B — 블리츠 */

        /// <summary>지금 화면에 풍선이 떠 있는가 — false면 다음 풍선을 기다리는 사이(스폰 간격)다</summary>
        private bool _hasBalloon;
        /// <summary>다음 풍선까지 남은 대기(초) — 티어가 오르면 짧아진다</summary>
        private float _pendingSpawnT;
        /// <summary>연속 명중 콤보 — 지뢰를 터뜨리거나 놓치면 0으로 꺾인다</summary>
        private int _combo;
        /// <summary>지금 뜬 풍선이 지뢰인가 — 색·무늬로 분명히 다르게 그린다</summary>
        private bool _isMine;

        /// <summary>3~4개 중 1개꼴 — 대략 28%</summary>
        private const float MineChance = 0.28f;

        /// <summary>마지막 3초 — 공통 하이라이트용 박동 타이머</summary>
        private float _finalBeatTimer;

        public override string Instruction => "뜨는 풍선을 눌러 터트려라 — 지뢰 풍선(줄무늬)은 피한다";

        public override string Status
        {
            get
            {
                var text = $"격파 {_popped}/{_need}  ·  콤보 {_combo}(티어{ComboTier(_combo)})";
                if (_missed > 0) text += $"  ·  놓침 {_missed}";
                return text;
            }
        }

        /// <summary>풍선 하나의 수명(초). 난이도 축(2.0→1.2)에 콤보 티어 축(→0.8)을 더 얹는다 —
        /// 몰아칠수록 판단할 시간이 더 준다</summary>
        private float Life
        {
            get
            {
                var baseLife = Mathf.Lerp(2f, 1.2f, (Difficulty - 1f) * 0.5f);
                var tierT = (ComboTier(_combo) - 1) / 2f;
                return Mathf.Lerp(baseLife, 0.8f, tierT);
            }
        }

        /// <summary>풍선 반지름(px). 난이도 1→28, 3→20 — 24px쯤을 가운데 두고 좁힌다</summary>
        private float BaseRadius => Mathf.Lerp(28f, 20f, (Difficulty - 1f) * 0.5f);

        protected override void Setup()
        {
            // `targets`는 조준 유지 시절엔 안 쓰던 파라미터였다. quests.json의
            // 두 TRACK 항목(사주경계·경계근무) 모두 난이도3에서 3을 내려주므로
            // 그 값을 "터트려야 할 풍선 개수"로 그대로 재해석한다. 하한 2는
            // "하나만 터뜨리면 끝"이라는 밋밋함을 막고, 상한 6은 풍선 하나당
            // 수명 0.8~2초 + 다음 풍선까지 이동하는 시간을 감안해 제한 시간
            // 안에 무리 없이 닿을 수 있는 값이다
            _need = Mathf.Clamp(ParamInt("targets", 3), 2, 6);
            _popped = 0;
            _missed = 0;
            _combo = 0;
            _burstT = 0f;
            _hasBalloon = false;
            _pendingSpawnT = 0f;
            Spawn();
        }

        /// <summary>새 풍선을 무작위 자리에 띄운다. 3~4개 중 하나는 지뢰다</summary>
        private void Spawn()
        {
            // 가장자리 12%는 금지 — SearchBoard 선례. 구석에 몰리면 클릭 여유가
            // 줄고, "구석만 노려본다"가 최적 전략이 되어버린다
            _pos = new Vector2(RandRange(0.12f, 0.88f), RandRange(0.14f, 0.86f));
            _age = 0f;
            _hasBalloon = true;
            _isMine = Rng.NextDouble() < MineChance;
        }

        /// <summary>다음 풍선까지 뜸을 스케줄한다 — 티어가 오를수록 간격이 짧아져 몰아치는 느낌을 준다</summary>
        private void ScheduleNext()
        {
            _hasBalloon = false;
            var tierT = (ComboTier(_combo) - 1) / 2f;
            _pendingSpawnT = Mathf.Lerp(0.35f, 0.05f, tierT);
        }

        /// <summary>연속 명중 1~3단. 3개마다 한 단씩(0~2:1단, 3~5:2단, 6+:3단)</summary>
        private static int ComboTier(int combo) => combo >= 6 ? 3 : combo >= 3 ? 2 : 1;

        /// <summary>등장 팝 + 경고 수축을 반영한 지금 반지름</summary>
        private float CurrentRadius()
        {
            var life = Life;
            var growK = Mathf.Clamp01(_age / SpawnGrow);
            var scale = Mathf.Sin(growK * Mathf.PI * 0.5f); // 뜰 때 커지는 팝 — ease-out

            var remain = life - _age;
            if (remain < WarnWindow)
            {
                // 수명 막판엔 쪼그라든다 — 곧 사라진다는 신호를 크기로도 준다
                scale *= Mathf.Lerp(0.35f, 1f, Mathf.Clamp01(remain / WarnWindow));
            }
            return BaseRadius * scale;
        }

        protected override void Advance(float dt, BoardInput input)
        {
            if (_burstT > 0f) _burstT = Mathf.Max(0f, _burstT - dt / BurstDuration);
            FinalStretchTick(dt);

            // Draw가 한 번도 안 돌아 판 좌표계를 아직 모른다
            if (_area.width <= 0f) return;

            // 클릭 여부와 무관하게 매 프레임 커서를 굴린다 — Draw의 크로스헤어가
            // 이걸로 따라오므로 클릭 안 한 프레임에도 갱신돼야 한다
            _cursor.Tick(dt, input, _area);

            if (!_hasBalloon)
            {
                // 풍선 사이 뜸 — 티어가 오를수록 짧아진다("블리츠")
                _pendingSpawnT -= dt;
                if (_pendingSpawnT <= 0f) Spawn();
                return;
            }

            _age += dt;

            if (_age >= Life)
            {
                // 수명이 다했다 — 못 터트렸다. 벌점만 받고 콤보가 꺾인 채 다음으로 넘어간다
                Miss();
                _missed += 1;
                _combo = 0;
                ScheduleNext();
                return;
            }

            // 클릭 대신 Space·Enter도 받는다(`Confirm` — Board.cs §BoardInput)
            if (!input.Confirm) return;

            var center = new Vector2(_area.x + _pos.x * _area.width, _area.y + _pos.y * _area.height);
            var radius = CurrentRadius();
            if (Vector2.Distance(_cursor.Screen(_area), center) > radius)
            {
                // 풍선 밖을 짚었다 — 벌점 없다. 잃는 것은 시간뿐이다
                return;
            }

            _burstPos = center;
            _burstRadius = radius;
            _burstT = 1f;

            if (_isMine)
            {
                // 지뢰 — 콤보가 통째로 날아간다. 몰아치던 손이 발목을 잡는 순간이다
                _burstMine = true;
                _combo = 0;
                Miss();
                Sfx.Play("miss", 1f, 0.8f);
                ScheduleNext();
                return;
            }

            _burstMine = false;
            _popped += 1;
            _combo += 1;
            Fill = (float)_popped / _need;

            // 티어가 오를수록 터짐음이 높아진다 — 몰아칠수록 손맛도 커진다
            var tierT = (ComboTier(_combo) - 1) / 2f;
            Sfx.Play("tap", 1f, Mathf.Lerp(1.15f, 1.7f, tierT));

            if (_popped >= _need)
            {
                Clear();
                return;
            }
            ScheduleNext();
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
            _area = body;
            theme.Fill(body, HudTheme.Paper3);

            if (_hasBalloon)
            {
                var center = new Vector2(body.x + _pos.x * body.width, body.y + _pos.y * body.height);
                var radius = CurrentRadius();
                var remain = Life - _age;
                var warning = remain < WarnWindow;
                // 마지막 0.4초는 깜빡인다 — 안 그려지는 프레임은 그냥 건너뛴다
                var hidden = warning && !HudTheme.Pulse(6f);

                if (radius > 0.5f && !hidden)
                {
                    var box = new Rect(center.x - radius, center.y - radius, radius * 2f, radius * 2f);
                    if (_isMine)
                    {
                        // 지뢰 — 색(Alert)과 무늬(사선 해칭)로 분명히 다르게. 착각으로 터뜨리면 안 된다
                        theme.Fill(box, HudTheme.AlertW);
                        theme.Hatch(box, HudTheme.Alert);
                        theme.Border(box, HudTheme.Alert, 2f);
                    }
                    else
                    {
                        theme.Fill(box, warning ? HudTheme.AlertW : HudTheme.AccentW);
                        theme.Border(box, warning ? HudTheme.Heat : HudTheme.Accent, 2f);
                    }
                }
            }

            // 터지는 연출 — 짧게 번지고 사라지는 사각 링 한두 프레임. 지뢰는 경고색이다
            if (_burstT > 0f)
            {
                var k = 1f - _burstT;
                var r = _burstRadius * Mathf.Lerp(1f, 1.9f, k);
                var ring = new Rect(_burstPos.x - r, _burstPos.y - r, r * 2f, r * 2f);
                theme.Fill(ring, _burstMine ? HudTheme.Alert : HudTheme.Accent, _burstT * 0.55f);
            }

            // 커서 — 가상 커서 자리라 마우스든 화살표+Space든 같은 십자선이 따라온다
            var cursorPos = _cursor.Screen(body);
            if (body.Contains(cursorPos))
            {
                theme.Fill(new Rect(cursorPos.x - 10f, cursorPos.y - 1f, 20f, 2f), HudTheme.Ink);
                theme.Fill(new Rect(cursorPos.x - 1f, cursorPos.y - 10f, 2f, 20f), HudTheme.Ink);
            }

            // 격파율 — Fill = 터트린 수 / 목표
            var bar = new Rect(body.x + 40f, body.yMax - 40f, body.width - 80f, 18f);
            theme.Bar(bar, Fill, HudTheme.Accent);

            // 콤보 티어 — 오를수록 존재감을 키운다
            if (_combo > 0)
            {
                var tier = ComboTier(_combo);
                var comboText = $"콤보 {_combo} · 티어 {tier}";
                var style = theme.At(theme.Label, tier == 3 ? 16 : 13,
                    tier == 3 ? HudTheme.Heat : HudTheme.Ink2, TextAnchor.MiddleLeft);
                GUI.Label(new Rect(body.x + 12f, body.y + 10f, 200f, 22f), comboText, style);
            }

            theme.Border(body, HudTheme.Rule);
            FinalStretchDraw(theme, body);
        }
    }
}
