using UnityEngine;

namespace SoldierADay.Net
{
    /// <summary>
    /// `DODGE` 회피 — 3레인 낙하물 피하기(A-5 2차, 설계안 §A-5 2차).
    ///
    /// 조작은 좌우(`Horizontal`) 하나뿐이다 — A·D 또는 ←→로 세 레인을 오간다.
    /// 위에서 떨어지는 낙하물이 내 레인에 닿기 전에 다른 레인으로 비켜야 한다.
    ///
    /// ── 목표는 생존이 아니라 회피 개수다 ────────────────────────────────────
    /// 시간을 버티는 판이 아니다 — **낙하 `count`개를 피해야 통과**한다
    /// (`GradesOnTimeout = true`, `Fill` = 피한 수 / 목표). `TrackBoard`의
    /// 풍선과 같은 셈법이다: 맞아도 목표 개수는 줄지 않고 뒤에서 계속
    /// 보충되므로, 놓친 대가는 기회가 아니라 시간과 무적 낭비다.
    ///
    /// 맞으면 `Miss()` 하나와 함께 짧은 무적 시간이 붙는다 — 그 사이 겹쳐
    /// 떨어지는 다른 낙하물에 한 프레임에 두 번 맞는 것을 막는다.
    ///
    /// 낙하 패턴은 시드 고정 — 같은 일과는 재시도해도 같은 순서로 떨어진다.
    /// </summary>
    public sealed class DodgeBoard : Board
    {
        private const int LaneCount = 3;

        /// <summary>맞은 뒤 무적 시간(초)</summary>
        private const float InvulnDuration = 0.7f;

        /// <summary>낙하물 하나의 반지름(px)</summary>
        private const float DropRadius = 22f;

        private struct Drop
        {
            public int Lane;
            /// <summary>0(등장)~1(레인 바닥 도달)</summary>
            public float Progress;
            public bool Alive;
        }

        private Drop[] _drops;
        private float _fallTime;
        private float _spawnInterval;
        private float _spawnTimer;
        private int _lastSpawnLane;

        private int _lane;
        private int _prevHorizontalSign;

        private int _dodged;
        private int _target;
        private float _invulnT;
        private float _hitFlashT;

        public override string Instruction => "← → (A · D)로 레인을 옮겨 낙하물을 피하라";

        public override string Status =>
            $"회피 {_dodged}/{_target}" + (_invulnT > 0f ? "  ·  무적" : "");

        /// <summary>과정형이다 — 시간이 다 돼도 그때까지 피한 수(Fill)로 등급만 매겨 통과시킨다</summary>
        protected override bool GradesOnTimeout => true;

        protected override void Setup()
        {
            // count = 통과에 필요한 회피 횟수. REACT의 count를 그대로 재해석한
            // 값이라, 워크오더 지시대로 기존 키를 재사용한다
            _target = Mathf.Clamp(ParamInt("count", 8), 4, 20);

            // speed 0(느림)~1(빠름) — 다른 원형의 speed와 같은 방향으로 읽는다.
            // 기본값 0.5는 중간 낙하 속도
            var speed = Mathf.Clamp01(Param("speed", 0.5f));
            _fallTime = Mathf.Lerp(2.6f, 1.15f, speed);
            _spawnInterval = Mathf.Max(0.45f, _fallTime * 0.55f);

            _drops = new Drop[8];
            _spawnTimer = 0.6f; // 판이 열리자마자 쏟아지지 않게 첫 낙하는 살짝 늦춘다
            _lastSpawnLane = -1;

            _lane = 1; // 가운데에서 시작
            _prevHorizontalSign = 0;

            _dodged = 0;
            _invulnT = 0f;
            _hitFlashT = 0f;
        }

        protected override void Advance(float dt, BoardInput input)
        {
            HandleMove(input);

            if (_invulnT > 0f) _invulnT -= dt;
            if (_hitFlashT > 0f) _hitFlashT -= dt;

            _spawnTimer -= dt;
            if (_spawnTimer <= 0f)
            {
                Spawn();
                _spawnTimer = _spawnInterval;
            }

            for (var i = 0; i < _drops.Length; i += 1)
            {
                if (!_drops[i].Alive) continue;

                _drops[i].Progress += dt / _fallTime;
                if (_drops[i].Progress < 1f) continue;

                _drops[i].Alive = false;

                if (_drops[i].Lane == _lane && _invulnT <= 0f)
                {
                    _invulnT = InvulnDuration;
                    _hitFlashT = 0.3f;
                    Miss();
                    continue;
                }

                // 내 레인이 아니었거나 무적 중이었다 — 피한 것으로 센다.
                // 무적 중에 겹쳐 떨어진 것까지 회피로 세는 이유는, 무적은
                // 방금 맞은 대가일 뿐 그 사이 온 낙하물까지 또 벌점을 주면
                // 짧은 무적이 오히려 손해가 되기 때문이다
                _dodged += 1;
                Fill = (float)_dodged / _target;
                if (_dodged >= _target)
                {
                    Clear();
                    return;
                }
            }
        }

        /// <summary>레인 이동 — 축을 누르고 있는 동안 계속 밀리지 않게 한 번 누를 때 한 칸만 옮긴다</summary>
        private void HandleMove(BoardInput input)
        {
            var sign = input.Horizontal > 0.4f ? 1 : input.Horizontal < -0.4f ? -1 : 0;
            if (sign != 0 && sign != _prevHorizontalSign)
            {
                _lane = Mathf.Clamp(_lane + sign, 0, LaneCount - 1);
            }
            _prevHorizontalSign = sign;
        }

        private void Spawn()
        {
            var slot = -1;
            for (var i = 0; i < _drops.Length; i += 1)
            {
                if (_drops[i].Alive) continue;
                slot = i;
                break;
            }
            if (slot < 0) return; // 버퍼가 꽉 찼다 — 다음 틱에 다시 시도한다(사실상 없는 경우)

            // 같은 레인이 두 번 연달아 나오지 않게 — 최적 전략이 "한 레인에
            // 붙박여 있기"가 되는 것을 막는다
            int lane;
            do { lane = Rng.Next(LaneCount); } while (lane == _lastSpawnLane && LaneCount > 1);
            _lastSpawnLane = lane;

            _drops[slot] = new Drop { Lane = lane, Progress = 0f, Alive = true };
        }

        public override void Draw(HudTheme theme, Rect body)
        {
            theme.Fill(body, HudTheme.Paper3);

            var laneW = body.width / LaneCount;
            for (var l = 1; l < LaneCount; l += 1)
            {
                var x = body.x + laneW * l;
                theme.Fill(new Rect(x - 1f, body.y, 2f, body.height), HudTheme.Rule2);
            }

            // 낙하물
            for (var i = 0; i < _drops.Length; i += 1)
            {
                if (!_drops[i].Alive) continue;
                var cx = body.x + laneW * (_drops[i].Lane + 0.5f);
                var cy = body.y + body.height * 0.08f + (body.height * 0.72f) * _drops[i].Progress;
                var box = new Rect(cx - DropRadius, cy - DropRadius, DropRadius * 2f, DropRadius * 2f);
                theme.Fill(box, HudTheme.AlertW);
                theme.Border(box, HudTheme.Alert, 2f);
            }

            // 내 위치 — 무적 중엔 깜빡인다
            var playerY = body.y + body.height * 0.86f;
            var playerX = body.x + laneW * (_lane + 0.5f);
            var showPlayer = _invulnT <= 0f || HudTheme.Pulse(8f);
            if (showPlayer)
            {
                var box = new Rect(playerX - 26f, playerY - 20f, 52f, 40f);
                theme.Fill(box, _hitFlashT > 0f ? HudTheme.AlertW : HudTheme.AccentW);
                theme.Border(box, _hitFlashT > 0f ? HudTheme.Alert : HudTheme.Accent, 2f);
            }

            // 회피율 바
            var bar = new Rect(body.x + 40f, body.yMax - 34f, body.width - 80f, 16f);
            theme.Bar(bar, Fill, HudTheme.Accent);

            theme.Border(body, HudTheme.Rule);
        }
    }
}
