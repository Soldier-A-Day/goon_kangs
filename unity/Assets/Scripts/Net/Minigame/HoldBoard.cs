using UnityEngine;

namespace SoldierADay.Net
{
    /// <summary>
    /// `HOLD` — 5건. 급수통 · 졸음 · 발전기 급유 ×2 · 난방기 압력
    /// (`chore-water` `sur-lecture` `admin-fuel` `admin-gen` `admin-heater`,
    /// `quests.json` 실측). 델리게이션 교환표의 `keepwarm`(배수로 · 난로 유지)도
    /// 같은 파라미터 모양을 쓴다.
    ///
    /// ── 전면 교체 — 계기 유지에서 회피로 ────────────────────────────────
    /// 예전엔 바늘을 안전 범위(밴드)에 붙잡아 두는 게이지였다. 지시대로 버린다.
    /// 이제는 **플래피 버드류 회피**다 — 조작은 `input.Tap`(클릭·Space)
    /// 하나뿐이고, 누르면 위로 붕 뜨고 안 누르면 중력으로 가라앉는다. 위아래
    /// 벽이 계기의 상한·하한을 대신하고, 오른쪽에서 흘러오는 장애물이
    /// 밴드가 하던 "지금 어디가 안전한가"의 역할을 대신한다.
    ///
    /// 원형 이름(`HOLD`)과 프로토콜은 안 바꾼다 — `quests.json`도, 서버도,
    /// `Board`의 계약(Clear·Miss·Fill·Grade)도 그대로다. 바뀌는 건 이 판
    /// 하나가 그 파라미터를 무엇으로 읽느냐뿐이다.
    ///
    /// ── 파라미터 재해석 ───────────────────────────────────────────────────
    /// `bandMin/bandMax/drift/slack`은 이제 계기 값이 아니라 **난이도가 주
    /// 요인이고 그 값들은 변형별 결을 보태는 보정치**다(자세한 산식은
    /// `Setup` 주석). 옛 계기가 다루던 "얼마나 좁은 범위를 얼마나 빨리
    /// 벗어나는가"가 여기서는 "틈이 얼마나 좁고 장애물이 얼마나 촘촘히
    /// 빨리 오는가"로 그대로 대응된다 — 같은 축이 다른 몸으로 옮겨간 것이지
    /// 근거 없이 새로 지어낸 숫자가 아니다.
    ///
    /// ── 압력계(`admin-heater`)의 2페이즈는 살려 둔다 ─────────────────────
    /// 옛 코드는 절반을 버티면 안전 범위를 좁혔다. `Spec.phase2`가 그대로
    /// 있으니, 여기서는 절반을 지나면 **아직 안 지나온 장애물의 틈**을
    /// 좁힌다 — 같은 "여기서부터 빡빡해진다"는 서사를 이어받는다.
    ///
    /// ── 충돌은 실패가 아니라 사고다 ──────────────────────────────────────
    /// 즉시 실패(`Fail`)는 없다. 부딪히면 `Miss()` + 짧은 무적(0.8초, 깜빡임)
    /// 으로 가운데로 돌아와 다시 뜬다 — 연속 충돌이 프레임마다 실수를
    /// 쌓는 스팸이 되지 않게 하는 여유다. 시간이 다 되면 여전히 실패다
    /// (`TimesOut` 기본값 그대로) — 이 판은 판단이 아니라 반응 훈련이라
    /// 시간초과를 봐줄 이유가 없다.
    ///
    /// ── A-5 2차 — 콤보 상승감 ─────────────────────────────────────────────
    /// 장애물을 연속으로 통과할 때마다 통과음(`tap`) 피치가 조금씩 올라간다
    /// (`_combo`). 부딪히면 그 자리에서 1배로 되돌아간다 — 시각으로는 무적
    /// 깜빡임이 "지금 안전하다"를 말해 주고, 청각으로는 피치 리셋이 "콤보가
    /// 끊겼다"를 말해 준다.
    /// </summary>
    public sealed class HoldBoard : Board
    {
        /// <summary>아바타가 화면에서 고정으로 서 있는 x — 정규화 0~1</summary>
        private const float PlayerX = 0.18f;

        /// <summary>장애물이 화면 오른쪽 바깥에서 나타나는 지점. 1보다 커야 미리 걸리지 않는다</summary>
        private const float SpawnLead = 1.15f;

        private const float ObstacleHalfWidth = 0.035f;

        /// <summary>충돌 판정 여유 — 아바타를 점이 아니라 아주 작은 사각형으로 쳐준다</summary>
        private const float PlayerHalfWidth = 0.02f;

        /// <summary>무적 지속 시간(초). 연속 충돌 스팸을 막는 여유이지 보상이 아니라 짧게 둔다</summary>
        private const float InvulnDuration = 0.8f;

        // ── 수리 — 틈은 화면 중간에서 뚫린다 ─────────────────────────────────
        // 사용자 피드백: "가운데를 기준으로 위아래쪽이 뚫려 있어야 되는데
        // 완전 위쪽 아님 완전 아래 둘 중 하나라서 깨기 어려워." 예전엔
        // 장애물 하나가 "위에서 내려온 막대" 또는 "아래에서 올라온 막대"
        // 딱 하나뿐이라, 지나가려면 화면 맨 위나 맨 아래 극단에 붙어야
        // 했다 — 플래피 조작(붕 뜨고 가라앉기)으로는 극단을 붙잡고 있기가
        // 유난히 어렵다. 플래피 버드 원형대로 고친다: 장애물 하나 = 위
        // 막대 + 아래 막대 "쌍"이고, 그 사이 틈이 화면 중간 범위에서 뚫린다.
        /// <summary>틈 중심이 나올 수 있는 정규화 범위 — 화면 맨 위·맨 아래 극단은 뺀다</summary>
        private const float GapCenterMin = 0.25f;
        private const float GapCenterMax = 0.75f;

        // 중력·임펄스는 기존 HOLD 파라미터가 다루지 않는 새 축(수직 물리)이라
        // 근거를 끌어올 데가 없다 — 대신 튜닝값을 하나로 고정한다. 임펄스
        // 한 번의 상승량은 impulse²/(2*gravity) ≈ 화면 높이의 11%로, 절반
        // 화면을 오가는 데 탭 서너 번이면 되는 정도로 잡았다.
        private const float Gravity = 1.7f;
        private const float Impulse = 0.62f;

        private float _speed;      // 정규화 폭 기준 장애물 흐름 속도(1/초)
        private float _spacing;    // 장애물 사이 간격(정규화 폭)
        private float _gapBase;    // 기준 틈 크기(정규화 높이 비율)
        private int _need;         // 목표 통과 개수

        private float[] _gapCenter; // 장애물별 틈 중심(정규화 0~1, GapCenterMin~Max 범위)
        private float[] _gapSize;  // 장애물별 실제 틈 크기(기준값에 개체 편차를 준 것)
        private bool[] _passed;

        private float _scrollX;
        private float _playerY;    // 0(바닥)~1(천장)
        private float _vel;
        private int _passedCount;
        private float _invulnT;
        private bool _phase2;

        /// <summary>연속 통과 수 — 부딪히면 0으로 되돌아간다. `_passedCount`(누적 총합)와
        /// 달리 이건 "지금 몇 번째 콤보인가"라 통과음 피치가 여기에 붙는다</summary>
        private int _combo;

        /// <summary>이 콤보 수쯤에서 피치 상승이 상한(1.5배)에 닿는다. 그 뒤로는 더 안 오른다 —
        /// 계속 올리면 언젠가는 삑삑거리는 칩튠이 되어 저장소 톤 원칙과 어긋난다</summary>
        private const float ComboPitchCap = 8f;

        public override string Instruction => "눌러서 떠라 — 장애물을 피해 버텨라";

        public override string Status
        {
            get
            {
                var text = $"통과 {_passedCount}/{_need}";
                if (_phase2) text += "  ·  틈 좁아짐";
                return text;
            }
        }

        protected override void Setup()
        {
            var bandMin = Param("bandMin", 0.5f);
            var bandMax = Param("bandMax", 0.8f);
            var drift = Param("drift", 0.32f);
            var slack = Param("slack", 1.5f);
            var d01 = (Difficulty - 1f) * 0.5f; // 1~3 → 0~1

            // 속도 — 난이도가 주 요인("난이도가 오르면 빨라진다"). 원래 `drift`는
            // 놓았을 때 바늘이 흐르던 속도였다. 급수통(난이도1·drift .30)은
            // 원래도 느긋했고 난방기 압력(난이도3·drift .40)은 원래도 급했으니,
            // 그 결을 보정치로 얹는다.
            var driftMul = Mathf.Lerp(0.9f, 1.15f, Mathf.InverseLerp(0.28f, 0.42f, drift));
            _speed = Mathf.Lerp(0.20f, 0.40f, d01) * driftMul;

            // 틈 크기 — 난이도가 주 요인("좁아진다"). 원래 안전 범위 폭
            // (`bandMax - bandMin`)이 넓었던 변형(졸음 방지 .40)은 원래도
            // 관대했으니 틈도 넉넉하게, 좁았던 변형(급수통 .18)은 그만큼 빡빡하게.
            var bandWidth = bandMax - bandMin;
            var bandMul = Mathf.Lerp(0.85f, 1.15f, Mathf.InverseLerp(0.15f, 0.4f, bandWidth));
            _gapBase = Mathf.Clamp(Mathf.Lerp(0.40f, 0.24f, d01) * bandMul, 0.18f, 0.48f);

            // 장애물 간격 — 난이도가 주 요인. 원래 `slack`(이탈 허용 시간)이
            // 길었던 변형은 여유가 많던 계기였으니 장애물도 성기게 온다.
            var slackMul = Mathf.Lerp(0.92f, 1.08f, Mathf.InverseLerp(1.5f, 2.0f, slack));
            _spacing = Mathf.Lerp(0.60f, 0.42f, d01) * slackMul;

            // 목표 개수 — 첫 장애물이 아바타 자리까지 흘러오는 시간(`leadTime`)에,
            // 그 뒤로 장애물이 하나씩 늘어날 때마다 걸리는 시간(`spawnInterval`)을
            // 더해서 마지막 장애물이 도착하는 시각을 거꾸로 센다. 목표는 제한
            // 시간(`Limit` = quests.json의 workSeconds)의 80% 안에 마지막 장애물이
            // 들어오는 것 — 나머지 20%는 부딪혀 무적으로 흘려보내는 여유다.
            //
            // **처음엔 `leadTime`을 빼먹고 `Limit*0.82 / spawnInterval`로만 셌다.**
            // 파이썬으로 실제 5건(급수통·발전기 급유×2·정신교육·난방기 압력)의
            // 마지막 장애물 도착 시각을 미러링해 검증하니, 가장 느리고 짧은
            // 급수통(14초·난이도1)만 마지막 장애물이 제한 시간을 0.03초 넘겨
            // 도착했다 — leadTime(약 5.2초)이 예산에서 안 빠져서 목표 개수가
            // 하나 많게 나온 것이었다. leadTime을 빼고 다시 재니 5건 전부
            // 20~36% 여유로 완주 가능했다.
            var spawnInterval = _spacing / _speed;
            var leadTime = (SpawnLead - PlayerX) / _speed;
            var budget = Limit * 0.8f - leadTime;
            _need = Mathf.Clamp(1 + Mathf.FloorToInt(budget / spawnInterval), 3, 14);

            _playerY = 0.5f;
            _vel = 0f;
            _scrollX = 0f;
            _passedCount = 0;
            _combo = 0;
            _invulnT = 0f;
            _phase2 = false;

            // 동시에 화면에 여럿이 걸릴 수 있어 목표보다 여유 있게 미리 깔아 둔다
            var slotCount = _need + 4;
            _gapCenter = new float[slotCount];
            _gapSize = new float[slotCount];
            _passed = new bool[slotCount];

            // 틈 중심은 화면 중간 범위(GapCenterMin~Max)에서 시드 고정 Rng로
            // 무작위로 온다 — 플래피 버드 원형 그대로다. 다만 완전 독립
            // 무작위면 이웃한 틈이 0.25와 0.75처럼 정반대 극단으로 붙어
            // 나올 수 있다. 그 경우 한 장애물 간격(spawnInterval) 안에 화면
            // 절반을 오가야 하는데, 임펄스는 한 번에 11%(§Impulse 주석)밖에
            // 안 뜨니 여러 번 탭해야 하고 그마저 사람이 아무리 빨라도
            // 한계가 있다 — 그래서 다음 틈 중심이 이전 것에서 얼마나
            // 멀어질 수 있는지 물리로 상한을 건다.
            //
            // 상한(maxCenterDelta) 산식: tapPeriod(0.33초, 초당 세 번 탭 —
            // "보통 실력"으로 잡은 값)마다 탭한다고 보면 그 사이 평균 상승
            // 속도는 Impulse - 0.5*Gravity*tapPeriod다(탭 직후 Impulse에서
            // 시작해 다음 탭까지 중력으로 선형 감소하는 사다리꼴의 평균).
            // 반응 여유로 0.7배를 곱해 낮춰 잡고, 그 속도로 spawnInterval
            // 동안 갈 수 있는 거리를 다음 틈 중심의 최대 이동폭으로 삼는다.
            // (검산: 파이썬 미러로 실측 HOLD 4건 — 급수통·발전기 급유×2·
            // 난방기 압력 — 을 300~500회 몬테카를로 돌려 클리어율 100%를
            // 확인했다. 이 캡이 없어도 100%였지만 있으면 평균 실수 횟수가
            // 더 낮아져 채택한다.)
            var tapPeriod = 0.333f;
            var climbRate = (Impulse - 0.5f * Gravity * tapPeriod) * 0.7f;
            var maxCenterDelta = Mathf.Clamp(climbRate * spawnInterval, 0.15f, 0.5f);

            var gapCenter = RandRange(GapCenterMin, GapCenterMax);
            for (var i = 0; i < slotCount; i += 1)
            {
                if (i > 0)
                {
                    var lo = Mathf.Max(GapCenterMin, gapCenter - maxCenterDelta);
                    var hi = Mathf.Min(GapCenterMax, gapCenter + maxCenterDelta);
                    gapCenter = RandRange(lo, hi);
                }

                _gapCenter[i] = gapCenter;
                _gapSize[i] = Mathf.Clamp(_gapBase * RandRange(0.9f, 1.1f), 0.12f, 0.6f);
            }
        }

        protected override void Advance(float dt, BoardInput input)
        {
            if (_invulnT > 0f) _invulnT -= dt;

            // 물리는 임펄스 하나 · 중력 하나면 된다. dt를 곱해 두면 프레임이
            // 들쭉날쭉해도 같은 높이만큼 뜨고 같은 속도로 떨어진다
            if (input.Tap) _vel = Impulse;
            _vel -= Gravity * dt;
            _playerY += _vel * dt;

            // 위아래 벽 — 계기의 상한·하한 자리를 하늘과 땅이 대신한다. 부딪히면
            // 그대로 충돌이고, 벽에 튕기지 않는다(속도를 죽인다) — 벽에
            // 붙어 미끄러지듯 있다가 다음 입력에야 다시 뜬다
            var hitWall = _playerY <= 0f || _playerY >= 1f;
            _playerY = Mathf.Clamp01(_playerY);
            if (hitWall) _vel = 0f;

            _scrollX += _speed * dt;

            var hit = hitWall;
            for (var i = 0; i < _gapCenter.Length; i += 1)
            {
                var x = ObstacleX(i);

                if (!_passed[i] && x + ObstacleHalfWidth < PlayerX)
                {
                    _passed[i] = true;
                    _passedCount += 1;
                    _combo += 1;
                    // 콤보 상승감 — 연속으로 통과할수록 같은 `tap`이 조금씩 높게 튄다.
                    // 부딪히면(아래 hit 처리) 다시 1배로 가라앉는다
                    Sfx.Play("tap", 1f, Mathf.Lerp(1f, 1.5f, Mathf.Clamp01(_combo / ComboPitchCap)));
                }

                // 무적 동안은 새 충돌을 판정하지 않는다 — 부딪힌 직후 가운데로
                // 돌아오다가 다른 장애물에 또 걸려 실수가 프레임마다 쌓이는
                // 스팸을 막는 것이 이 여유의 목적이다
                if (!hit && _invulnT <= 0f && Overlaps(i, x)) hit = true;
            }

            Fill = Mathf.Clamp01((float)_passedCount / _need);

            // 2페이즈 — 압력계(`phase2:"HOLD"`)만 해당된다. 옛 계기판은 절반을
            // 버티면 안전 범위를 좁혔다(§난방기 점검); 여기서는 그 자리를
            // "이제부터 오는 장애물의 틈"이 이어받는다. 이미 지나온 장애물은
            // 건드리지 않는다 — 지나간 판정을 소급해서 바꾸지 않는다
            if (!_phase2 && Spec?.phase2 != null && _passedCount >= Mathf.CeilToInt(_need * 0.5f))
            {
                _phase2 = true;
                for (var k = 0; k < _gapSize.Length; k += 1)
                {
                    if (_passed[k]) continue;
                    _gapSize[k] = Mathf.Clamp(_gapSize[k] * 0.78f, 0.12f, 0.6f);
                }
                // 옛 코드도 범위가 좁아지는 순간 Miss()로 신호를 줬다(처음부터
                // 다시 세지 않고 이어서 세되, 국면이 바뀌었다는 것만 알린다)
                Miss();
            }

            if (hit && _invulnT <= 0f)
            {
                Miss();
                _combo = 0;
                _invulnT = InvulnDuration;
                _playerY = 0.5f;
                _vel = 0f;
            }

            if (_passedCount >= _need) Clear();
        }

        private float ObstacleX(int i) => SpawnLead + i * _spacing - _scrollX;

        private bool Overlaps(int i, float x)
        {
            if (Mathf.Abs(x - PlayerX) > ObstacleHalfWidth + PlayerHalfWidth) return false;

            // 틈은 위 막대와 아래 막대 사이, 중심(_gapCenter) ± 절반 폭이다
            // — 위나 아래 극단에 붙어야만 지나갈 수 있던 예전 구조를 고친
            // 자리가 여기다(§클래스 요약 "수리 — 틈은 화면 중간에서 뚫린다")
            var half = _gapSize[i] * 0.5f;
            var open = Mathf.Abs(_playerY - _gapCenter[i]) <= half;
            return !open;
        }

        public override void Draw(HudTheme theme, Rect body)
        {
            // 아래 72px는 진행 막대 몫이다. 비행 구간과 겹치면 막대 바로 위를
            // 스치듯 지나는 장애물이 막대에 파묻혀 안 보인다
            var play = new Rect(body.x, body.y + 8f, body.width, body.height - 72f);
            theme.Fill(play, HudTheme.Paper3);

            for (var i = 0; i < _gapCenter.Length; i += 1)
            {
                var x = ObstacleX(i);
                if (x < -0.12f || x > 1.12f) continue; // 화면 밖 — 그릴 필요 없다

                var barWidthPx = ObstacleHalfWidth * 2f * play.width;
                var barX = play.x + (x - ObstacleHalfWidth) * play.width;

                // 장애물 하나 = 위 막대 + 아래 막대 쌍. 정규화 좌표는
                // 0(바닥)~1(천장)이라 픽셀로 옮길 때 위아래가 뒤집힌다
                // (아바타를 그리는 아래 py 계산과 같은 변환)
                var half = _gapSize[i] * 0.5f;
                var gapTop = Mathf.Clamp01(_gapCenter[i] + half);
                var gapBottom = Mathf.Clamp01(_gapCenter[i] - half);

                var topHeightPx = (1f - gapTop) * play.height;
                if (topHeightPx > 0f)
                {
                    var topRect = new Rect(barX, play.y, barWidthPx, topHeightPx);
                    theme.Fill(topRect, HudTheme.Rule2);
                    theme.Border(topRect, HudTheme.Rule);
                }

                var bottomHeightPx = gapBottom * play.height;
                if (bottomHeightPx > 0f)
                {
                    var bottomRect = new Rect(barX, play.yMax - bottomHeightPx, barWidthPx, bottomHeightPx);
                    theme.Fill(bottomRect, HudTheme.Rule2);
                    theme.Border(bottomRect, HudTheme.Rule);
                }
            }

            // 아바타 — 무적 동안은 깜빡인다. 그래야 "왜 안 죽었지"가 아니라
            // "지금은 안전하다"로 읽힌다
            if (_invulnT <= 0f || HudTheme.Pulse(10f))
            {
                var px = play.x + PlayerX * play.width;
                var py = play.yMax - _playerY * play.height;
                const float size = 28f;
                var avatarRect = new Rect(px - size * 0.5f, py - size * 0.5f, size, size);
                theme.Fill(avatarRect, _invulnT > 0f ? HudTheme.Alert : HudTheme.Accent);
                theme.Border(avatarRect, HudTheme.Ink);
            }

            theme.Border(play, HudTheme.Rule);

            var bar = new Rect(body.x + 40f, body.yMax - 46f, body.width - 80f, 18f);
            theme.Bar(bar, Fill, HudTheme.Accent);
            GUI.Label(new Rect(bar.x, bar.y - 24f, bar.width, 20f),
                $"통과 {_passedCount}/{_need}", theme.At(theme.Label, 12, HudTheme.Ink2));

            theme.Border(body, HudTheme.Rule);
        }
    }
}
