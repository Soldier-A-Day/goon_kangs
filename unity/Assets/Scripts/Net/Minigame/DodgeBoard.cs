using UnityEngine;

namespace SoldierADay.Net
{
    /// <summary>
    /// `DODGE` 전면 재설계 — 3레인 러너(사용자 반려 "그게 뭐냐" 이후 재발주,
    /// Subway Surfers·Crossy Road·탄막 graze 리서치로 설계 확정).
    ///
    /// ── 옛 판을 버린 이유 ────────────────────────────────────────────────
    /// 옛 `DODGE`는 위에서 떨어지는 원을 좌우로 피하기만 하는 판이었다.
    /// "회피 개수"가 목표라 예고 없이 뭔가 나타나 스치듯 지나가는 게 전부였고,
    /// 그게 "그게 뭐냐"는 반려로 돌아왔다. 이번 판은 **훈련병이 연병장을
    /// 달리는 장면**을 판 전체의 은유로 삼는다 — 바닥이 아래로 스크롤되고,
    /// 장애물은 앞(위)에서 다가오고, 나는 3레인을 오가며 피한다.
    ///
    /// ── 조작은 하나뿐이다(§1) ────────────────────────────────────────────
    /// A·D 또는 ←→로 레인을 즉시 스냅 이동한다. `_laneTweenT`가 0.1초에 걸쳐
    /// 선형으로만 보간한다 — 가속·감속·오버슈트 없음("관성 없음"). 판단은
    /// 순간이고 이동만 짧게 보이면 된다.
    ///
    /// ── 속도 3막(§2) ─────────────────────────────────────────────────────
    /// 제한 시간(`Limit`)을 1/3씩 갈라 조깅→구보→전력 질주로 넘어간다. 막이
    /// 바뀔 때마다(§2) 호루라기 소리 + "빨라진다!" 배너로 반드시 예고한다 —
    /// 예고 없는 가속은 텔레그래프 원칙(§3)을 스스로 어기는 것이다.
    ///
    /// ── 텔레그래프(§3) ───────────────────────────────────────────────────
    /// 장애물은 도착 0.7초 전(3막은 0.5초)부터만 그려진다(`Draw`의
    /// `hazard.Remaining > hazard.TelegraphTime` 컷). 그 전에는 스케줄만
    /// 있고 그림이 없다 — "예고 없는 장애물 금지"를 판정이 아니라 렌더링
    /// 순서로 강제한 것이다. 레인 이동은 0.1초, 예고는 0.5~0.7초라 반응
    /// 여유는 항상 4~6배다(클래스 하단 §검산 참고).
    ///
    /// ── 판정(§4) ─────────────────────────────────────────────────────────
    /// 히트박스는 그림의 70%(플레이어 유리) — `HitHalfWidth`. 레인 이동 중
    /// (`VisualLane`이 두 레인 사이에 있을 때) 장애물이 도착하면서 거리가
    /// 히트박스보다는 멀고 반 칸(`GrazeHalfWidth`)보다는 가까우면 "아슬"이다
    /// — 막 피해 나가는 순간을 보너스로 잡아준다. 맞아도 즉사가 아니라
    /// `Miss()` + 0.5초 무적(비틀거림)뿐이라, 여러 번 맞아도 판이 끝나지
    /// 않는다(`Fail()`을 이 판에서는 절대 부르지 않는다).
    ///
    /// ── 수집 유혹(§5) ────────────────────────────────────────────────────
    /// 초코파이는 위험이 없는 그림이라 텔레그래프 없이 도착 전 내내 보인다.
    /// 단일 장애물 스폰 때는 그 옆 레인에, 봉쇄 패턴(2레인 동시 막힘) 때는
    /// 가끔 막힐 레인 중 하나에 훨씬 이른 도착 시각으로 놓는다 — 먼저 들렀다
    /// 안전 레인으로 빠져야 하는 "두 장애물 사이" 선택을 만든다.
    ///
    /// ── 정지 방지(§6) ────────────────────────────────────────────────────
    /// 같은 레인에 3초 머무르면 경고, 그 뒤로 1초 더 안 움직이면 `Miss()`.
    /// 안전 레인에 눌러앉아 텔레그래프만 기다리는 것을 막는다.
    ///
    /// ── 통과·등급(§7) ────────────────────────────────────────────────────
    /// `GradesOnTimeout = true`를 유지한다 — 이 판은 시간을 버티는 생존형
    /// 이지 목표 개수형이 아니다. `Fill = 경과/제한 + 초코파이·아슬 가산
    /// - 실수 감산`(0~1 클램프). 실수 감산은 최대 0.3으로 캡을 걸어 두는데,
    /// 시간을 다 버틴 시점의 경과 항만으로 이미 1.0이라 아무리 실수해도
    /// `Fill`이 0.7 밑으로 못 내려간다 — `Board.HandleTimeout`의 `Fill>=0.5`
    /// 통과선을 항상 넘긴다는 뜻이다("시간을 다 버티면 Clear"의 실제 구현).
    /// 대신 실수 3회 이상이면 0.85 밑으로 떨어져 등급이 B에서 C로 깎인다
    /// (`Board.Grade()`의 시간초과 분기) — Miss 누적이 통과 여부는 못 건드리되
    /// 등급에는 그대로 반영된다.
    ///
    /// ── 마지막 3초(§8) ───────────────────────────────────────────────────
    /// 결승선 테이프가 화면 위에서 아래로 다가오는 연출을 얹고, 판이 끝나면
    /// `Status`가 아슬·초코파이 결산 문구로 바뀐다.
    ///
    /// ── 파라미터 재해석(§9, 근거) ────────────────────────────────────────
    /// `speed`(0~1, 다른 원형과 같은 방향)는 이제 "낙하 속도"가 아니라 3막
    /// 전체에 걸리는 전역 배속(`_tempoMul`)이다 — 막 구조 자체가 난이도
    /// 곡선을 이미 맡고 있어서, speed는 그 위에 얹는 미세 조정으로만 쓴다.
    /// `count`는 예전엔 "피해야 할 낙하물 개수"였는데 이 판엔 그런 목표가
    /// 없다. quests.json이 모든 DODGE 항목에 이미 내려주는 난이도 신호라
    /// 버리지 않고 "봉쇄 패턴 빈도·초코파이 인색함"으로 재해석한다 — 값이
    /// 클수록(옛 의미로 "더 많이 피해야 하는 판") 봉쇄가 잦고 초코파이가
    /// 줄어, 원래 의도한 체감 난이도 방향을 그대로 잇는다.
    ///
    /// 낙하 패턴은 여느 판과 같이 시드 고정(`Rng`, `Board.Begin`)이고
    /// 프레임 전부 `dt` 기반이다.
    ///
    /// ── §검산: 반응 가능성(레인 이동 0.1초 vs 예고 시간) ────────────────
    /// `speed` 극단값(0·1)에서 `_tempoMul`은 [0.85, 1.15]:
    ///
    ///   막        레인이동  예고   여유(예고-이동)  스폰간격        동시비행
    ///   1막 조깅   0.1s    0.7s   0.6s(6배)       1.02~1.38s      ~1
    ///   2막 구보   0.1s    0.7s   0.6s(6배)       0.68~0.92s      ~2(봉쇄)
    ///   3막 전력질주 0.1s  0.5s   0.4s(4배)       0.47~0.63s      ~2
    ///
    /// 가장 빡빡한 3막도 예고가 레인 이동의 4배다 — 텔레그래프를 본 순간
    /// 바로 반응해도 여유가 남는다. 봉쇄 패턴(2레인 동시 점유)도 안전 레인
    /// 하나로 "한 번만" 옮기면 되므로 같은 여유가 그대로 적용된다.
    /// </summary>
    public sealed class DodgeBoard : Board
    {
        private const int LaneCount = 3;

        /// <summary>레인 이동에 걸리는 시간(초) — "즉시 스냅"이지만 그림은 이만큼 걸려 옮긴다(§1)</summary>
        private const float LaneSnapDuration = 0.1f;

        /// <summary>히트 판정 반경(레인 폭 기준) — 그림의 70%만 맞는다(플레이어 유리, §4)</summary>
        private const float HitHalfWidth = 0.5f * 0.7f;

        /// <summary>아슬(그레이즈) 판정 반경 — 반 칸(§4)</summary>
        private const float GrazeHalfWidth = 0.5f;

        /// <summary>맞은 뒤 무적·비틀거림 시간(초) — 즉사가 아니라 잠깐의 대가일 뿐이다(§4)</summary>
        private const float InvulnDuration = 0.5f;

        /// <summary>같은 레인에 이만큼(초) 머무르면 경고가 뜬다(§6)</summary>
        private const float StallWarnAt = 3f;

        /// <summary>경고가 뜬 뒤 이만큼(초) 더 안 움직이면 `Miss()`(§6)</summary>
        private const float StallMissAt = 1f;

        /// <summary>결승선 연출이 시작되는 잔여 시간(초, §8)</summary>
        private const float FinishWindow = 3f;

        /// <summary>1·2막 공통 스폰~도착 기본 시간(초). 3막은 이 값을 스크롤 배속으로 나눈다</summary>
        private const float BaseTravelTime = 1.6f;

        /// <summary>3막 "전력 질주" 스크롤 배속(§2) — 장애물 도착이 이만큼 빨라진다</summary>
        private const float ScrollBoostAct3 = 1.6f;

        /// <summary>장애물 그림 반지름(px)</summary>
        private const float DropRadius = 22f;

        /// <summary>바닥 줄무늬가 흐르는 기본 속도(px/초) — 3막엔 `ScrollBoostAct3`만큼 곱해진다(§2)</summary>
        private const float GroundScrollSpeed = 90f;

        /// <summary>Fill 가산 — 초코파이 1개(§7)</summary>
        private const float PieBonus = 0.03f;

        /// <summary>Fill 가산 — 아슬 1회(§7)</summary>
        private const float GrazeBonus = 0.02f;

        /// <summary>Fill 감산 — 실수 1회(§7)</summary>
        private const float MissPenalty = 0.06f;

        /// <summary>
        /// 실수 감산 총합의 상한(§7). 시간을 다 버틴 시점의 경과 항은 항상 1.0이라,
        /// 이 캡이 0.5를 넘지 않는 한 `Fill`이 `Board.HandleTimeout`의 통과선(0.5)
        /// 아래로 떨어질 수 없다 — "시간을 다 버티면 Clear"를 수식으로 보장한다.
        /// </summary>
        private const float MissPenaltyCap = 0.3f;

        private struct Hazard
        {
            public int Lane;
            /// <summary>스폰~도착 총 시간(초)</summary>
            public float TravelTime;
            /// <summary>도착까지 남은 시간(초) — 0 이하면 도착</summary>
            public float Remaining;
            /// <summary>이 장애물이 예고를 시작하는 잔여 시간(초) — 스폰 시점 막(act) 값을 그대로 박제한다</summary>
            public float TelegraphTime;
            public bool Alive;
        }

        private struct Pie
        {
            public int Lane;
            public float TravelTime;
            public float Remaining;
            public bool Alive;
        }

        private Hazard[] _hazards;
        private Pie[] _pies;

        private int _lastSpawnLane;
        private float _spawnTimer;

        /// <summary>지금 막(0~2). -1은 "아직 한 번도 안 정해졌다"는 뜻 — 첫 진입은 예고 배너를 안 띄운다</summary>
        private int _act;
        private float _spawnInterval;
        private float _travelTime;
        private float _telegraphTime;
        private bool _blockAllowed;
        private float _scrollSpeed;
        private float _scrollY;

        /// <summary>막 전환 배너("빨라진다!")가 남은 시간(초)</summary>
        private float _actBannerT;
        private string _actBannerText = "";

        /// <summary>speed 파라미터로 정한 전역 배속(§9) — 1보다 크면 느슨, 작으면 빡빡</summary>
        private float _tempoMul;

        /// <summary>count 파라미터로 정한 봉쇄 패턴 확률(§9)</summary>
        private float _blockChance;

        /// <summary>count 파라미터로 정한 초코파이 스폰 확률(§9)</summary>
        private float _pieChance;

        private int _lane;
        private int _laneFrom;
        private float _laneTweenT;
        private int _prevHorizontalSign;

        /// <summary>같은 레인에 머무른 시간(초, §6)</summary>
        private float _stallTime;

        private float _invulnT;
        private float _staggerT;

        private int _grazeCount;
        private int _pieCount;

        public override string Instruction => "A · D(← →)로 레인을 옮겨라 — 부딪혀도 비틀거릴 뿐, 끝나지 않는다";

        public override string Status
        {
            get
            {
                if (State == BoardState.Cleared)
                {
                    // §8 — 결승선을 통과한 뒤엔 결산 문구로 바뀐다
                    var text = $"완주 — 아슬 {_grazeCount}회 · 초코파이 {_pieCount}개";
                    if (Mistakes > 0) text += $" · 실수 {Mistakes}회";
                    return text;
                }

                var status = $"{ActLabel(_act)} · 아슬 {_grazeCount} · 초코파이 {_pieCount}";
                if (_stallTime >= StallWarnAt) status += "  ·  야! 뭐해!";
                return status;
            }
        }

        /// <summary>생존형이다 — 시간을 버틴 만큼(Fill)으로 통과시킨다(§7)</summary>
        protected override bool GradesOnTimeout => true;

        protected override void Setup()
        {
            // speed 0(느림)~1(빠름) — §9 재해석: 3막 구조가 난이도 곡선을
            // 이미 맡고 있어서, 여기서는 스폰 간격·도착 시간 전체에 걸리는
            // 미세 배속으로만 쓴다. 0.85~1.15 범위라 극단값도 텔레그래프
            // 여유를 깨지 않는다(클래스 §검산)
            var speed = Mathf.Clamp01(Param("speed", 0.5f));
            _tempoMul = Mathf.Lerp(1.15f, 0.85f, speed);

            // count — §9 재해석: 예전엔 "피해야 할 낙하물 개수"였다. 이 판엔
            // 그런 목표가 없지만, quests.json이 모든 DODGE 항목에 이미
            // 내려주는 난이도 신호라 버리지 않는다. 값이 클수록(옛 의미로
            // "더 많이 피해야 하는 판") 봉쇄 패턴이 잦아지고 초코파이가
            // 인색해진다 — 체감 난이도 방향은 그대로 잇는다
            var count = Mathf.Clamp(ParamInt("count", 10), 4, 20);
            var countT = (count - 4) / 16f;
            _blockChance = Mathf.Lerp(0.15f, 0.45f, countT);
            _pieChance = Mathf.Lerp(0.4f, 0.22f, countT);

            _hazards = new Hazard[12];
            _pies = new Pie[6];
            _lastSpawnLane = -1;
            _spawnTimer = 0.6f; // 판이 열리자마자 쏟아지지 않게 첫 스폰은 살짝 늦춘다

            _act = -1;
            _actBannerT = 0f;
            _scrollY = 0f;

            _lane = 1; // 가운데에서 시작
            _laneFrom = 1;
            _laneTweenT = 1f;
            _prevHorizontalSign = 0;
            _stallTime = 0f;

            _invulnT = 0f;
            _staggerT = 0f;

            _grazeCount = 0;
            _pieCount = 0;
        }

        protected override void Advance(float dt, BoardInput input)
        {
            HandleMove(dt, input);
            if (_laneTweenT < 1f) _laneTweenT = Mathf.Min(1f, _laneTweenT + dt / LaneSnapDuration);

            if (_invulnT > 0f) _invulnT -= dt;
            if (_staggerT > 0f) _staggerT -= dt;
            if (_actBannerT > 0f) _actBannerT -= dt;

            UpdateAct();
            _scrollY += dt * _scrollSpeed;

            UpdateSpawns(dt);
            UpdateHazards(dt);
            UpdatePies(dt);

            RecomputeFill();
        }

        /// <summary>레인 이동(§1) + 정지 방지(§6) — 한 번 누를 때 한 칸만 옮긴다(edge-trigger)</summary>
        private void HandleMove(float dt, BoardInput input)
        {
            var sign = input.Horizontal > 0.4f ? 1 : input.Horizontal < -0.4f ? -1 : 0;
            var attempted = sign != 0 && sign != _prevHorizontalSign;

            if (attempted)
            {
                var next = Mathf.Clamp(_lane + sign, 0, LaneCount - 1);
                if (next != _lane)
                {
                    _laneFrom = _lane;
                    _lane = next;
                    _laneTweenT = 0f;
                }
            }
            _prevHorizontalSign = sign;

            if (attempted)
            {
                // 가장자리 레인에서 더 못 가는 눌림도 "움직이려는 시도"로 쳐서
                // 정지 경고를 없앤다 — 양 끝 레인에 서 있는 것 자체가 죄는 아니다
                _stallTime = 0f;
                return;
            }

            _stallTime += dt;
            if (_stallTime >= StallWarnAt + StallMissAt)
            {
                Miss();
                _stallTime = StallWarnAt; // 경고 상태로 되돌아간다 — 계속 안 움직이면 1초마다 다시 벌점
            }
        }

        /// <summary>레인 이동 트윈 결과 — 관성 없는 선형 보간(§1). 두 레인 사이 값이 그레이즈 판정(§4)의 기준이 된다</summary>
        private float VisualLane => Mathf.Lerp(_laneFrom, _lane, _laneTweenT);

        /// <summary>지금 막을 판정하고, 바뀌었으면 파라미터를 새로 잡고 예고 배너를 띄운다(§2)</summary>
        private void UpdateAct()
        {
            var third = Limit / 3f;
            var idx = Elapsed < third ? 0 : Elapsed < third * 2f ? 1 : 2;
            if (idx == _act) return;

            var first = _act < 0;
            _act = idx;
            _spawnInterval = ActSpawnInterval(idx) * _tempoMul;
            _travelTime = ActTravelTime(idx) * _tempoMul;
            _telegraphTime = ActTelegraphTime(idx);
            _blockAllowed = idx >= 1; // 2막 "구보"부터 2레인 동시 봉쇄 패턴이 등장한다
            _scrollSpeed = (idx >= 2 ? ScrollBoostAct3 : 1f) * GroundScrollSpeed;

            if (first) return; // 판이 열리며 0막으로 들어가는 것은 "전환"이 아니다 — 예고 대상 아님

            // 막 전환 예고 — 호루라기 전용 자산이 없어 `alarm` 클립을 피치 올려
            // 대신 쓴다(Sfx.cs §pitch 변주 관례. `TrackBoard`의 tap 재사용과 같다)
            Sfx.Play("alarm", 1f, 1.5f);
            _actBannerT = 1.6f;
            _actBannerText = "빨라진다! — " + ActLabel(idx);
        }

        private void UpdateSpawns(float dt)
        {
            _spawnTimer -= dt;
            if (_spawnTimer > 0f) return;
            _spawnTimer = _spawnInterval;
            SpawnWave();
        }

        private void SpawnWave()
        {
            if (_blockAllowed && RandRange(0f, 1f) < _blockChance)
            {
                SpawnBlockPair();
            }
            else
            {
                SpawnSingle();
            }
        }

        private void SpawnSingle()
        {
            var lane = PickLane(_lastSpawnLane);
            _lastSpawnLane = lane;
            SpawnHazard(lane);
            MaybeSpawnPieForSingle(lane);
        }

        /// <summary>2레인 동시 봉쇄 — 남은 레인 하나가 유일한 통로가 된다(§2)</summary>
        private void SpawnBlockPair()
        {
            var safe = Rng.Next(LaneCount);
            for (var l = 0; l < LaneCount; l += 1)
            {
                if (l != safe) SpawnHazard(l);
            }
            _lastSpawnLane = -1; // 다음 단일 스폰의 레인 편중 검사는 쉰다 — 안전 레인이 매번 랜덤이라 자연히 갈린다

            MaybeSpawnPieForBlock(safe);
        }

        /// <summary>같은 레인이 두 번 연달아 나오지 않게 고른다 — "한 레인에 붙박여 있기"를 최적 전략으로 만들지 않는다</summary>
        private int PickLane(int avoid)
        {
            if (avoid < 0 || LaneCount <= 1) return Rng.Next(LaneCount);
            int lane;
            do { lane = Rng.Next(LaneCount); } while (lane == avoid);
            return lane;
        }

        private void SpawnHazard(int lane)
        {
            var slot = -1;
            for (var i = 0; i < _hazards.Length; i += 1)
            {
                if (_hazards[i].Alive) continue;
                slot = i;
                break;
            }
            if (slot < 0) return; // 버퍼가 꽉 찼다 — 사실상 없는 경우, 다음 스폰에 다시 시도한다

            _hazards[slot] = new Hazard
            {
                Lane = lane,
                TravelTime = _travelTime,
                Remaining = _travelTime,
                TelegraphTime = _telegraphTime,
                Alive = true,
            };
        }

        /// <summary>단일 장애물 옆 레인에 초코파이 — "장애물 옆 레인" 배치(§5)</summary>
        private void MaybeSpawnPieForSingle(int hazardLane)
        {
            if (RandRange(0f, 1f) >= _pieChance) return;
            var lane = PickLane(hazardLane);
            SpawnPie(lane, _travelTime); // 장애물과 같은 도착 시각 — 바로 옆에서 갈등을 만든다
        }

        /// <summary>봉쇄 패턴 중 초코파이 — "두 장애물 사이" 배치(§5)</summary>
        private void MaybeSpawnPieForBlock(int safeLane)
        {
            if (RandRange(0f, 1f) >= _pieChance) return;

            if (RandRange(0f, 1f) < 0.6f)
            {
                // 봉쇄될 레인 중 하나에, 그 레인이 막히기 한참 전(도착 시각의
                // 45%)에 놓는다 — 먼저 들렀다 안전 레인으로 빠져야 하는
                // "두 장애물 사이" 위험-보상을 만든다
                var lane = (safeLane + 1 + Rng.Next(2)) % LaneCount;
                SpawnPie(lane, _travelTime * 0.45f);
            }
            else
            {
                // 그냥 안전 레인에 놓는 경우도 섞는다 — 매번 위험을 강요하면
                // "위험-보상 선택"이 아니라 "강제 리스크"가 된다
                SpawnPie(safeLane, _travelTime);
            }
        }

        private void SpawnPie(int lane, float travelTime)
        {
            var slot = -1;
            for (var i = 0; i < _pies.Length; i += 1)
            {
                if (_pies[i].Alive) continue;
                slot = i;
                break;
            }
            if (slot < 0) return;

            _pies[slot] = new Pie { Lane = lane, TravelTime = travelTime, Remaining = travelTime, Alive = true };
        }

        private void UpdateHazards(float dt)
        {
            for (var i = 0; i < _hazards.Length; i += 1)
            {
                if (!_hazards[i].Alive) continue;

                _hazards[i].Remaining -= dt;
                if (_hazards[i].Remaining > 0f) continue;

                _hazards[i].Alive = false;
                ResolveHazard(_hazards[i].Lane);
            }
        }

        /// <summary>장애물 도착 판정(§4) — 히트박스 70% 안이면 피격, 반 칸 안이면 아슬</summary>
        private void ResolveHazard(int lane)
        {
            var dist = Mathf.Abs(lane - VisualLane);

            if (dist < HitHalfWidth)
            {
                if (_invulnT > 0f) return; // 무적 중 겹쳐 맞는 것은 벌점을 또 주지 않는다(원형 계승 규칙)

                Miss(); // 즉사가 아니다 — 비틀거림 하나와 벌점 하나만 쌓인다
                _invulnT = InvulnDuration;
                _staggerT = InvulnDuration;
                return;
            }

            if (dist < GrazeHalfWidth)
            {
                // 아슬 — 반 칸 이내로 스쳤다. 보너스 점수 + 휙 소리(피치 변주,
                // Sfx.cs §pitch 관례. 전용 자산 없이 `tap`을 빌린다)
                _grazeCount += 1;
                Sfx.Play("tap", 0.9f, RandRange(1.5f, 1.9f));
            }
        }

        private void UpdatePies(float dt)
        {
            for (var i = 0; i < _pies.Length; i += 1)
            {
                if (!_pies[i].Alive) continue;

                _pies[i].Remaining -= dt;
                if (_pies[i].Remaining > 0f) continue;

                _pies[i].Alive = false;
                if (Mathf.Abs(_pies[i].Lane - VisualLane) < GrazeHalfWidth)
                {
                    _pieCount += 1;
                    Sfx.Play("pop", 0.8f, 1.1f);
                }
                // 못 주웠어도 벌점은 없다 — 유혹이지 시험이 아니다(§5)
            }
        }

        /// <summary>§7 — 경과 시간 기반 + 초코파이·아슬 가산 - 실수 감산(캡 있음), 0~1 클램프</summary>
        private void RecomputeFill()
        {
            var timeRatio = Mathf.Clamp01(Elapsed / Limit);
            var bonus = _pieCount * PieBonus + _grazeCount * GrazeBonus;
            var penalty = Mathf.Min(Mistakes * MissPenalty, MissPenaltyCap);
            Fill = Mathf.Clamp01(timeRatio + bonus - penalty);
        }

        private static float ActSpawnInterval(int act) => act switch { 0 => 1.2f, 1 => 0.8f, _ => 0.55f };

        private static float ActTravelTime(int act) =>
            act >= 2 ? BaseTravelTime / ScrollBoostAct3 : BaseTravelTime;

        private static float ActTelegraphTime(int act) => act >= 2 ? 0.5f : 0.7f;

        private static string ActLabel(int act) => act switch { 0 => "조깅", 1 => "구보", _ => "전력 질주" };

        public override void Draw(HudTheme theme, Rect body)
        {
            theme.Fill(body, HudTheme.Paper3);

            var laneW = body.width / LaneCount;
            DrawScrollingGround(theme, body);

            for (var l = 1; l < LaneCount; l += 1)
            {
                var x = body.x + laneW * l;
                theme.Fill(new Rect(x - 1f, body.y, 2f, body.height), HudTheme.Rule2);
            }

            // 초코파이 — 위험이 없는 그림이라 텔레그래프 없이 도착 전 내내 보인다(§5)
            for (var i = 0; i < _pies.Length; i += 1)
            {
                if (_pies[i].Alive) DrawPie(theme, body, laneW, _pies[i]);
            }

            // 장애물 — 예고 창 안에서만 그려진다. 그 전엔 스케줄만 있고 그림이
            // 없다 — "예고 없는 장애물"을 렌더링 순서로 아예 불가능하게 만든다(§3)
            for (var i = 0; i < _hazards.Length; i += 1)
            {
                if (_hazards[i].Alive && _hazards[i].Remaining <= _hazards[i].TelegraphTime)
                {
                    DrawHazard(theme, body, laneW, _hazards[i]);
                }
            }

            DrawPlayer(theme, body, laneW);

            if (_actBannerT > 0f) DrawActBanner(theme, body);
            if (_stallTime >= StallWarnAt) DrawStallWarning(theme, body);
            if (Remaining <= FinishWindow) DrawFinishLine(theme, body);

            var bar = new Rect(body.x + 40f, body.yMax - 34f, body.width - 80f, 16f);
            theme.Bar(bar, Fill, HudTheme.Accent);

            theme.Border(body, HudTheme.Rule);
        }

        /// <summary>연병장 바닥이 아래로 스크롤되는 것처럼 보이게 하는 줄무늬. 3막엔 배속만큼 더 빨리 흐른다(§2)</summary>
        private void DrawScrollingGround(HudTheme theme, Rect body)
        {
            const float stripeH = 44f;
            var start = -Mathf.Repeat(_scrollY, stripeH * 2f);
            var i = 0;
            for (var y = body.y + start; y < body.yMax; y += stripeH)
            {
                if ((i & 1) == 0)
                {
                    var top = Mathf.Max(y, body.y);
                    var h = Mathf.Min(stripeH, body.yMax - top);
                    if (h > 0f) theme.Fill(new Rect(body.x, top, body.width, h), HudTheme.Paper2);
                }
                i += 1;
            }
        }

        private void DrawPlayer(HudTheme theme, Rect body, float laneW)
        {
            var playerY = body.y + body.height * 0.86f;
            var playerX = body.x + laneW * (VisualLane + 0.5f);

            // 무적 중엔 깜빡인다 — 지금 안 맞는다는 것을 눈으로도 알려준다
            var showPlayer = _invulnT <= 0f || HudTheme.Pulse(8f);
            if (!showPlayer) return;

            var box = new Rect(playerX - 26f, playerY - 20f, 52f, 40f);
            var staggering = _staggerT > 0f;
            theme.Fill(box, staggering ? HudTheme.AlertW : HudTheme.AccentW);
            theme.Border(box, staggering ? HudTheme.Alert : HudTheme.Accent, 2f);
        }

        /// <summary>예고 마커 + 커지는 그림자(§3) — 예고가 시작된 순간부터 도착까지만 그려진다</summary>
        private void DrawHazard(HudTheme theme, Rect body, float laneW, Hazard hazard)
        {
            var t = Mathf.Clamp01(1f - hazard.Remaining / hazard.TelegraphTime); // 0(예고 시작)~1(도착)
            var cx = body.x + laneW * (hazard.Lane + 0.5f);
            var cy = Mathf.Lerp(body.y + body.height * 0.5f, body.y + body.height * 0.82f, t);

            // 커지는 그림자 — 도착이 가까울수록 크고 짙어진다
            var shadowR = Mathf.Lerp(10f, DropRadius * 1.4f, t);
            theme.Fill(new Rect(cx - shadowR, cy + shadowR * 0.6f, shadowR * 2f, shadowR * 0.5f), HudTheme.Dim);

            var r = Mathf.Lerp(DropRadius * 0.4f, DropRadius, t);
            var box = new Rect(cx - r, cy - r, r * 2f, r * 2f);
            var closing = t >= 1f || HudTheme.Pulse(9f);
            theme.Fill(box, HudTheme.AlertW);
            theme.Border(box, closing ? HudTheme.Alert : HudTheme.Heat, 2f);
        }

        private void DrawPie(HudTheme theme, Rect body, float laneW, Pie pie)
        {
            var t = Mathf.Clamp01(1f - pie.Remaining / pie.TravelTime);
            var cx = body.x + laneW * (pie.Lane + 0.5f);
            var cy = Mathf.Lerp(body.y + body.height * 0.1f, body.y + body.height * 0.82f, t);
            const float r = 14f;
            var box = new Rect(cx - r, cy - r, r * 2f, r * 2f);
            theme.Fill(box, HudTheme.Heat);
            theme.Border(box, HudTheme.White, 2f);
        }

        private void DrawActBanner(HudTheme theme, Rect body)
        {
            var alpha = Mathf.Clamp01(_actBannerT / 0.3f); // 끝나기 0.3초 전부터 옅어진다
            var banner = new Rect(body.center.x - 170f, body.y + 24f, 340f, 40f);
            theme.Fill(banner, HudTheme.AlertW, alpha);
            theme.Border(banner, HudTheme.Alert, 2f);
            GUI.Label(banner, _actBannerText, theme.At(theme.Heading, 20, HudTheme.Alert, TextAnchor.MiddleCenter));
        }

        private void DrawStallWarning(HudTheme theme, Rect body)
        {
            if (!HudTheme.Pulse(4f)) return;
            var box = new Rect(body.center.x - 120f, body.y + body.height * 0.4f, 240f, 30f);
            theme.Fill(box, HudTheme.AlertW, 0.85f);
            GUI.Label(box, "야! 뭐해!", theme.At(theme.Small, 16, HudTheme.Alert, TextAnchor.MiddleCenter));
        }

        /// <summary>마지막 3초 — 결승선 테이프가 위에서 아래로 다가온다(§8)</summary>
        private void DrawFinishLine(HudTheme theme, Rect body)
        {
            var k = 1f - Mathf.Clamp01(Remaining / FinishWindow); // 0(3초 전)→1(도착)
            var y = Mathf.Lerp(body.y + body.height * 0.15f, body.y + body.height * 0.8f, k);
            theme.Fill(new Rect(body.x, y - 3f, body.width, 6f), HudTheme.White);
            GUI.Label(new Rect(body.x, y - 26f, body.width, 20f), "결승선",
                theme.At(theme.Small, 15, HudTheme.White, TextAnchor.MiddleCenter));
        }
    }
}
