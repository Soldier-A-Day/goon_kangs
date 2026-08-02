using System.Collections.Generic;
using SoldierADay.Protocol;
using UnityEngine;

namespace SoldierADay.Net
{
    /// <summary>
    /// 파티클 12종 (SAD-ART-001 §9.1).
    ///
    /// 셋으로 나뉜다. **누가 켜는가**가 다르기 때문이다.
    ///
    ///   **날씨**   밴드가 켠다 (눈·비·먼지·아지랑이). 카메라에 붙어 화면을 덮는다
    ///   **몸**     내 상태가 켠다 (입김·땀). 캐릭터를 따라다닌다
    ///   **사건**   한 번 터지고 만다 (완료 링·총구 화염·쓰러짐·제독)
    ///
    /// 날씨 파티클을 카메라 자식으로 두는 것은 §9.1이 `VFX_Snow_Light`에 "카메라
    /// 자식"이라고 적어둔 그대로다. 월드에 뿌리면 110×85 타일을 다 덮어야 하고,
    /// 화면 밖 눈송이 수만 개를 시뮬레이션하게 된다.
    ///
    /// **판정은 하나도 없다.** 밴드도 수분도 서버가 보낸 값이고(ARCH-02),
    /// 여기서는 그 값에 따라 방출기를 켜고 끌 뿐이다.
    /// </summary>
    [DefaultExecutionOrder(60)]
    public sealed class Vfx : MonoBehaviour
    {
        public GameClient client;
        public Transform follow;
        public WeatherGrading grading;

        [Header("날씨 — 카메라 자식")]
        public ParticleSystem snowLight;
        public ParticleSystem snowHeavy;
        public ParticleSystem rain;
        public ParticleSystem dust;
        public ParticleSystem heatHaze;

        [Header("몸 — 캐릭터를 따라간다")]
        public ParticleSystem breath;
        public ParticleSystem sweat;

        [Header("사건 — 한 번 터진다")]
        public ParticleSystem questComplete;
        public ParticleSystem muzzleFlash;
        public ParticleSystem collapse;
        public ParticleSystem decon;
        public ParticleSystem steam;

        /// <summary>완료 링을 두 번 띄우지 않으려고 기억한다</summary>
        private readonly HashSet<string> _celebrated = new HashSet<string>();

        /// <summary>§9.1 `VFX_Dust` 버스트 조건 — 최근 스냅샷의 "온난 이상" 여부를 기억해 둔다.
        /// `Apply`는 스냅샷(10Hz)마다, `HandleStepContact`는 걷기 애니메이션 프레임마다
        /// 오므로 주기가 다르다 — 그래서 D-3 #6부터는 값을 캐시만 하고 방출은 안 한다</summary>
        private bool _warm;

        /// <summary>`follow`(내 캐릭터)의 리그. 걷기 접지 이벤트를 구독하고 완료 팝을 부른다</summary>
        private CharacterRig _followRig;

        private void OnEnable()
        {
            if (client != null) client.SnapshotReceived += Apply;
            _followRig = follow != null ? follow.GetComponent<CharacterRig>() : null;
            if (_followRig != null) _followRig.StepContact += HandleStepContact;
        }

        private void OnDisable()
        {
            if (client != null) client.SnapshotReceived -= Apply;
            if (_followRig != null) _followRig.StepContact -= HandleStepContact;
        }

        /// <summary>D-3 #6 — 걷기 접지 순간의 먼지 버스트. `CharacterRig`의 몸 스쿼시와
        /// 같은 프레임에서 온다(같은 신호가 발원지). 연속 방출이던 것을 여기로 옮겼다</summary>
        private void HandleStepContact()
        {
            if (!_warm) return;
            Puff(dust, follow != null ? follow.position : transform.position);
        }

        private void Apply(Snapshot snapshot)
        {
            if (snapshot == null) return;

            var band = snapshot.weather?.band;
            var indoor = IsIndoor();

            // 실내에는 눈이 안 온다. 이게 빠지면 생활관 천장에서 눈이 내리고,
            // 그 한 장면이 부대 전체를 가짜로 만든다
            var cold = !indoor && band == SnapshotWeatherBandValues.Cold;
            var freezing = !indoor && band == SnapshotWeatherBandValues.ExtremeCold;
            var hot = !indoor && (band == SnapshotWeatherBandValues.Hot ||
                                  band == SnapshotWeatherBandValues.ExtremeHot);
            var warm = !indoor && band != SnapshotWeatherBandValues.Cold &&
                                  band != SnapshotWeatherBandValues.ExtremeCold;

            Emit(snowLight, cold);
            Emit(snowHeavy, freezing);
            Emit(heatHaze, hot);

            // §9.1 `VFX_Rain` "기상 악화 (D-10)". 비 여부는 서버가 정한다 —
            // 날짜로 클라가 유추하면 4인이 서로 다른 날씨를 본다
            Emit(rain, !indoor && snapshot.weather != null && snapshot.weather.rain);

            var me = HudScreens.FindMember(snapshot, client != null ? client.MemberId : null);
            if (me?.stats == null) return;

            // §9.1 `VFX_Breath` "한랭 이하, 캐릭터". 실내에서도 춥긴 하지만
            // 입김은 밖에서 나는 것으로 둔다 — 실내는 §5.0에서 열원이다
            Emit(breath, cold || freezing);

            // §9.1 `VFX_SweatDrop` "수분 ≤ 50"
            Emit(sweat, me.stats.hydration <= 50);

            // D-3 #6 — 먼지는 더 이상 연속 방출이 아니다. 걷기 접지 프레임(다리가 땅을
            // 짚는 순간)에 `CharacterRig.StepContact`가 오면 `HandleStepContact`가
            // `Puff()`로 그 순간에만 뿌린다. 방출기 자체는 계속 꺼둔다 — 켜는 건
            // `Puff()`의 `Emit(count)`뿐이라 이 `Emit(dust, false)`는 안전장치다
            _warm = warm;
            Emit(dust, false);

            // 완료 링 — 방금 끝난 일과에만. 스냅샷은 10Hz라 같은 완료를
            // 계속 보내오므로, 한 번 띄운 것은 기억해 둔다
            if (snapshot.quests == null) return;
            foreach (var quest in snapshot.quests)
            {
                if (quest == null) continue;
                if (quest.status != SnapshotQuestsItemStatusValues.Done) continue;
                if (quest.ownerId != client.MemberId) continue;
                if (!_celebrated.Add(quest.id)) continue;
                var at = follow != null ? follow.position : transform.position;
                Burst(questComplete, at);
                _followRig?.PunchScale();   // D-3 #4 — 링만 터지고 몸은 무반응이던 것을 고친다
            }
        }

        private bool IsIndoor()
        {
            var world = GetComponentInParent<ZoneWorld>();
            return world != null && world.HereIndoor;
        }

        private void LateUpdate()
        {
            if (follow == null) return;

            var at = follow.position;
            foreach (var system in new[] { breath, sweat })
            {
                if (system == null) continue;
                // 머리 앞 · 머리 옆. 캐릭터 셀이 32×48이고 발이 원점이므로
                // 머리는 1.2유닛 위다 (§5.1)
                system.transform.position = at + new Vector3(0f, 1.2f, 0f);
            }
        }

        private static void Emit(ParticleSystem system, bool on)
        {
            if (system == null) return;
            var emission = system.emission;
            if (emission.enabled == on) return;
            emission.enabled = on;
            if (on && !system.isPlaying) system.Play();
        }

        /// <summary>사건 파티클 — 그 자리에서 한 번 터뜨린다</summary>
        public void Burst(ParticleSystem system, Vector3 at)
        {
            if (system == null) return;
            system.transform.position = at;
            system.Play();
        }

        /// <summary>
        /// D-3 #6 — `Burst`와 달리 `Play()`(지속 이펙트 시작)가 아니라 `Emit(count)`로
        /// 알갱이 몇 개만 즉시 뿌린다. 걸음마다 반복되는 이벤트라 `Play()`를 쓰면 방출기의
        /// 지속시간·루프 설정에 따라 겹쳐 쌓일 수 있어, 알갱이 수를 코드에서 못박는다.
        /// </summary>
        private static void Puff(ParticleSystem system, Vector3 at, int count = 6)
        {
            if (system == null) return;
            system.transform.position = at;
            system.Emit(count);
        }

        /// <summary>§7.10 화생방 제독소 · 사격장 격발 — 훈련 씬이 부른다</summary>
        public void Fire(Vector3 at) => Burst(muzzleFlash, at);
        public void Decontaminate(Vector3 at) => Burst(decon, at);
        public void Collapsed(Vector3 at) => Burst(collapse, at);
    }
}
