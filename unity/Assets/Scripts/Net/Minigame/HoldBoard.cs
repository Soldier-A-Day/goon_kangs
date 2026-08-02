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
    /// </summary>
    public sealed class HoldBoard : Board
    {
        private enum Side { Top, Bottom }

        /// <summary>아바타가 화면에서 고정으로 서 있는 x — 정규화 0~1</summary>
        private const float PlayerX = 0.18f;

        /// <summary>장애물이 화면 오른쪽 바깥에서 나타나는 지점. 1보다 커야 미리 걸리지 않는다</summary>
        private const float SpawnLead = 1.15f;

        private const float ObstacleHalfWidth = 0.035f;

        /// <summary>충돌 판정 여유 — 아바타를 점이 아니라 아주 작은 사각형으로 쳐준다</summary>
        private const float PlayerHalfWidth = 0.02f;

        /// <summary>무적 지속 시간(초). 연속 충돌 스팸을 막는 여유이지 보상이 아니라 짧게 둔다</summary>
        private const float InvulnDuration = 0.8f;

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

        private Side[] _side;
        private float[] _gapSize;  // 장애물별 실제 틈 크기(기준값에 개체 편차를 준 것)
        private bool[] _passed;

        private float _scrollX;
        private float _playerY;    // 0(바닥)~1(천장)
        private float _vel;
        private int _passedCount;
        private float _invulnT;
        private bool _phase2;

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
            _invulnT = 0f;
            _phase2 = false;

            // 동시에 화면에 여럿이 걸릴 수 있어 목표보다 여유 있게 미리 깔아 둔다
            var slotCount = _need + 4;
            _side = new Side[slotCount];
            _gapSize = new float[slotCount];
            _passed = new bool[slotCount];

            // 위·아래가 시드 고정 Rng로 섞인다. 완전 무작위면 같은 쪽이 네다섯
            // 번 연속으로 나올 수 있는데, 그러면 반대쪽은 사실상 "누르지 마라"
            // 구간이 되어 회피의 긴장이 죽는다 — 두 번 연속되면 다음은 바뀔
            // 확률을 올려 흐름을 고르게 섞는다
            var last = Rng.NextDouble() < 0.5 ? Side.Top : Side.Bottom;
            var run = 1;
            for (var i = 0; i < slotCount; i += 1)
            {
                if (i > 0)
                {
                    var flipChance = run >= 2 ? 0.75 : 0.5;
                    if (Rng.NextDouble() < flipChance)
                    {
                        last = last == Side.Top ? Side.Bottom : Side.Top;
                        run = 1;
                    }
                    else
                    {
                        run += 1;
                    }
                }

                _side[i] = last;
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
            for (var i = 0; i < _side.Length; i += 1)
            {
                var x = ObstacleX(i);

                if (!_passed[i] && x + ObstacleHalfWidth < PlayerX)
                {
                    _passed[i] = true;
                    _passedCount += 1;
                    Sfx.Play("tap");
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

            // 틈은 "지나갈 수 있는 쪽"이다. 위에서 내려온 장애물은 아래쪽이,
            // 아래에서 올라온 장애물은 위쪽이 열려 있다
            var gap = _gapSize[i];
            var open = _side[i] == Side.Top ? _playerY <= gap : _playerY >= 1f - gap;
            return !open;
        }

        public override void Draw(HudTheme theme, Rect body)
        {
            // 아래 72px는 진행 막대 몫이다. 비행 구간과 겹치면 막대 바로 위를
            // 스치듯 지나는 장애물이 막대에 파묻혀 안 보인다
            var play = new Rect(body.x, body.y + 8f, body.width, body.height - 72f);
            theme.Fill(play, HudTheme.Paper3);

            for (var i = 0; i < _side.Length; i += 1)
            {
                var x = ObstacleX(i);
                if (x < -0.12f || x > 1.12f) continue; // 화면 밖 — 그릴 필요 없다

                var barWidthPx = ObstacleHalfWidth * 2f * play.width;
                var barX = play.x + (x - ObstacleHalfWidth) * play.width;
                var barHeightPx = (1f - _gapSize[i]) * play.height;
                var barRect = _side[i] == Side.Top
                    ? new Rect(barX, play.y, barWidthPx, barHeightPx)
                    : new Rect(barX, play.yMax - barHeightPx, barWidthPx, barHeightPx);

                theme.Fill(barRect, HudTheme.Rule2);
                theme.Border(barRect, HudTheme.Rule);
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
