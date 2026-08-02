using System.Collections.Generic;

namespace SoldierADay.Net
{
    /// <summary>
    /// 명칭(enum성) 문자열 표 — 영어화 착수점(WORKORDER.md H-3).
    ///
    /// 계급 키(`private`/`pfc`/`corporal`/`sergeant`)는 sim이 보내는 시점부터 이미
    /// 영문이다. 화면에 뿌리는 한글 표시값만 이 표 하나를 거치게 만들면, 나중에
    /// 번역이 들어올 때 흩어진 switch 문(하드코딩 531개, WORKORDER.md H-3 실측)을
    /// 하나씩 찾아다니는 대신 표만 갈아 끼우면 된다 — **이후 정리가 따라 할 템플릿이다.**
    ///
    /// 담는 범위는 **명칭뿐이다**: 계급 · 보직 · 시간대(일과 구간) · 온도 밴드 —
    /// 전부 sim이 보내는 enum 값의 표시 이름이다. 거부 사유 문구·소대장 대사 같은
    /// **문장형** 텍스트는 여기 없다(Hud.cs · HudScreens.cs가 소유, 이번 정리 범위 밖 —
    /// WORKORDER.md H-3 지시).
    ///
    /// 표를 늘릴 때 지킬 규칙(템플릿):
    ///   1. 키는 sim 원본 id(영문 enum 값, `packages/sim/src/types.ts`)를 그대로 쓴다 —
    ///      표시 문자열만 갈아 끼우면 되게. 축약형이 필요하면 `xxxTag`처럼 별도 표를 둔다.
    ///   2. 모르는 키는 이전 switch 문의 기본값과 같은 값으로 떨어진다(호출부의
    ///      `TryGetValue` + 기본값). 빈 문자열보다 "그럴듯한 기본값"이 낫다 —
    ///      `ZoneNames`(`Generated/Zones.cs`)가 이미 그렇게 한다.
    ///   3. Unity Localization 패키지는 쓰지 않는다 — IMGUI 프로토타입 규모에 과하다
    ///      (WORKORDER.md H-3). 자체 Dictionary로 충분하고, 실제 다국어 전환 시
    ///      이 표를 언어별 리소스로 승격해도 호출부(`HudTheme.RankName` 등)는
    ///      그대로 둘 수 있다 — 내부 구현만 바뀐다.
    ///   4. 문장형 대사(거부 사유·소대장 대사·알림 문구)는 이 표에 넣지 않는다.
    ///      명칭과 문장은 번역 난이도가 다르고(Finnish Army Simulator식 번역 전략,
    ///      WORKORDER.md H-3), 섞으면 다음 정리 때 또 갈라내야 한다.
    /// </summary>
    internal static class HudLabels
    {
        /// <summary>4.2 계급 4종 — 전체 이름. sim `Rank` 유니온과 키가 같다</summary>
        internal static readonly Dictionary<string, string> Rank = new Dictionary<string, string>
        {
            ["private"] = "이병",
            ["pfc"] = "일병",
            ["corporal"] = "상병",
            ["sergeant"] = "병장",
        };

        /// <summary>5.3 보직 4종 — 전체 이름. sim `Role` 유니온과 키가 같다</summary>
        internal static readonly Dictionary<string, string> RoleName = new Dictionary<string, string>
        {
            ["rifle"] = "소총",
            ["comms"] = "통신",
            ["medic"] = "의무",
            ["admin"] = "행정",
        };

        /// <summary>5.3 보직 3글자 태그 — 완장·배지에 쓰는 축약형(영어여도 3글자 그대로다)</summary>
        internal static readonly Dictionary<string, string> RoleTag = new Dictionary<string, string>
        {
            ["rifle"] = "RFL",
            ["comms"] = "COM",
            ["medic"] = "MED",
            ["admin"] = "ADM",
        };

        /// <summary>4.4 하루 시간대 6종. sim `PhaseId` 유니온과 키가 같다</summary>
        internal static readonly Dictionary<string, string> Phase = new Dictionary<string, string>
        {
            ["reveille"] = "기상 · 점검",
            ["morning"] = "오전 일과",
            ["lunch"] = "중식 · 휴식",
            ["afternoon"] = "오후 일과",
            ["personal"] = "석식 · 개인정비",
            ["rollcall"] = "점호 · 판정",
        };

        /// <summary>5.0 온도 밴드 6종. sim `TempBand` 유니온과 키가 같다</summary>
        internal static readonly Dictionary<string, string> Band = new Dictionary<string, string>
        {
            ["extremeCold"] = "극혹한",
            ["cold"] = "한랭",
            ["normal"] = "평시",
            ["warm"] = "온난",
            ["hot"] = "혹서",
            ["extremeHot"] = "극혹서",
        };
    }
}
