using SoldierADay.Protocol;
using UnityEngine;

namespace SoldierADay.Net
{
    /// <summary>§7.1.3 무전 3단계</summary>
    public enum RadioState
    {
        /// <summary>마커 4개 + 퀘스트 핑 + 통신병 지정 핑</summary>
        Ok,
        /// <summary>1.5초 주기 점멸 · 위치 갱신 3초 지연</summary>
        Weak,
        /// <summary>모든 마커 · 핑 소멸 — 걸어가야만 확인 가능</summary>
        Down,
    }

    /// <summary>
    /// 구역 단위 시야 차단 (SAD-ART-001 §1.3-A + §1.3-C).
    ///
    /// **2D 전환이 깨뜨리는 것 중 최우선 문제**를 여기서 막는다. 3D 3인칭에서는
    /// 벽 너머가 안 보이므로 "무전 두절 → 물리적으로 모여야 정보가 전달된다"(§8.0
    /// COMM-01)가 성립했다. 2D 탑다운은 기본적으로 화면 안이 전부 보이므로,
    /// 그대로 옮기면 **통신병이라는 보직 하나가 통째로 무의미해진다.**
    ///
    /// 해결은 두 겹이다.
    ///   A. 다른 구역의 분대원 스프라이트를 렌더하지 않는다 (여기)
    ///   C. 미니맵 마커도 무전 상태에 게이팅한다 (여기서 상태를 정하고 HUD가 읽는다)
    ///
    /// §13.2가 "M1에서 반드시 검증 — M3로 미루면 되돌릴 수 없다"고 적어둔 항목이다.
    ///
    /// 무전 상태는 **서버가 소유한다**(`packages/sim/src/radio.ts`). 클라이언트가
    /// 통신병의 상태를 보고 스스로 유도하면 4인이 각자 다른 화면을 보게 되고,
    /// "무전이 끊겼으니 모이자"는 합의 자체가 성립하지 않는다.
    /// </summary>
    public sealed class ZoneVisibility : MonoBehaviour
    {
        public GameClient client;

        /// <summary>
        /// 내가 지금 있는 구역(`Z01`). 시야의 기준이자 카메라 Confiner의 기준이다.
        ///
        /// 예전에는 이 값이 둘이었다 — 서버 구역과 아트 구역. 서버가 8구역만 알던
        /// 시절이라 생활관·복도·세면장이 전부 `barracks` 하나였고, 그래서 §1.3-A가
        /// **벽을 통과했다**: 세면장에 있는 사람이 생활관에 있는 사람에게 그냥 보였다.
        /// 서버를 방 단위로 쪼갠 뒤로는 하나면 되고, 시야도 실제 방 경계를 따른다.
        /// </summary>
        public string CurrentZone { get; set; } = "";

        public RadioState Radio { get; private set; } = RadioState.Ok;

        /// <summary>§7.1.3 "약함"에서 마커가 1.5초마다 깜빡인다</summary>
        public bool MarkerVisible =>
            Radio switch
            {
                RadioState.Ok => true,
                RadioState.Weak => Mathf.Repeat(Time.unscaledTime * 1.5f, 1f) < 0.6f,
                _ => false,
            };

        /// <summary>
        /// 스냅샷을 받는다.
        ///
        /// 구역은 **여기서 덮어쓰지 않는다.** 걸어 다니는 동안 내 위치를 아는 것은
        /// 발밑을 보는 `ZoneWorld`이고, 서버 스냅샷은 10Hz라 한 박자 늦다. 늦은
        /// 값으로 덮으면 방을 나서는 순간 시야가 깜빡인다.
        /// </summary>
        public void Apply(Snapshot snapshot)
        {
            Radio = Parse(snapshot?.radio);
        }

        private static RadioState Parse(string radio) => radio switch
        {
            "down" => RadioState.Down,
            "weak" => RadioState.Weak,
            _ => RadioState.Ok,
        };

        /// <summary>
        /// §1.3-A — 이 분대원을 월드에 그릴 것인가.
        ///
        /// **무전과 무관하다.** 무전이 살아 있어도 다른 구역 사람이 눈에 보이지는
        /// 않는다. 무전이 주는 것은 미니맵 마커이지 시야가 아니다 — 그 구분이
        /// 사라지면 §1.3-A와 C가 같은 장치가 되고 통신병이 다시 무의미해진다.
        /// </summary>
        public bool CanSee(string zone) => !string.IsNullOrEmpty(zone) && zone == CurrentZone;

        /// <summary>
        /// §1.3-C — 미니맵·부대 지도에 이 분대원의 마커를 띄울 것인가.
        ///
        /// 같은 구역이면 눈에 보이므로 무전과 상관없이 띄운다. 다른 구역이면
        /// 무전이 살아 있을 때만 뜬다.
        /// </summary>
        public bool CanTrack(string zone) => CanSee(zone) || MarkerVisible;

        /// <summary>§7.1.3 우측 상단 라벨. 두절이면 적색으로 항상 떠 있어야 한다</summary>
        public string RadioLabel => Radio switch
        {
            RadioState.Ok => "무전 연결",
            RadioState.Weak => "무전 약함",
            _ => "무전 두절",
        };

        public Color RadioColor => Radio switch
        {
            RadioState.Ok => HudTheme.Accent,
            RadioState.Weak => HudTheme.Heat,
            _ => HudTheme.Alert,
        };

    }
}
