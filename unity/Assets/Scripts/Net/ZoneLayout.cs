using System.Collections.Generic;
using UnityEngine;

namespace SoldierADay.Net
{
    /// <summary>
    /// 구역의 월드 좌표.
    ///
    /// **이건 규칙이 아니라 표시다.** sim의 `zones.ts`가 직접 적어둔 그대로다 —
    /// "좌표는 격자 위 논리 좌표이며 렌더링과 무관하다. Unity는 실제 좌표를
    /// zone으로 매핑해 보고할 뿐이다."
    ///
    /// 그래서 여기 있는 숫자가 sim의 격자와 달라도 문제가 아니다. sim은 이동 **비용**을
    /// 정하고, 여기는 **어디에 그릴지**를 정한다. 둘을 같은 값으로 묶으려 하면
    /// 맵을 예쁘게 배치할 때마다 이동 시간이 바뀌는 이상한 결합이 생긴다.
    ///
    /// 다만 상대적 배치는 격자를 따라간다. 격자에서 먼 구역이 화면에서 가까우면
    /// 6.1이 말한 "동선이 멀다"가 눈으로 읽히지 않는다.
    /// </summary>
    public static class ZoneLayout
    {
        /// <summary>격자 한 칸의 월드 거리(m). 맵끼리 겹치지 않을 만큼 벌린다</summary>
        private const float Step = 90f;

        private static readonly Dictionary<string, Vector2> Grid = new Dictionary<string, Vector2>
        {
            // sim/zones.ts 의 GRID 와 같은 상대 배치
            { "barracks", new Vector2(0, 0) },
            { "boilerRoom", new Vector2(3, 0) },
            { "messHall", new Vector2(2, 0) },
            { "infirmary", new Vector2(2, 1) },
            { "drillGround", new Vector2(1, 2) },
            { "storage", new Vector2(3, 2) },
            { "guardPost", new Vector2(0, 4) },
            { "trainingField", new Vector2(4, 4) },
        };

        public static IEnumerable<string> Zones => Grid.Keys;

        public static Vector3 AnchorOf(string zone)
        {
            return Grid.TryGetValue(zone, out var cell)
                ? new Vector3(cell.x * Step, 0f, cell.y * Step)
                : Vector3.zero;
        }

        /// <summary>
        /// 같은 구역 안에서 인원이 겹치지 않게 자리를 준다.
        ///
        /// 4인이 한 점에 겹치면 누가 어디 있는지 안 보이고, 그러면 스냅샷이
        /// 제대로 오는지도 확인할 수 없다. 판정과 무관한 순수 표시다.
        /// </summary>
        public static Vector3 SlotIn(string zone, int index, int total)
        {
            var anchor = AnchorOf(zone);
            if (total <= 1) return anchor;

            var spread = 2.0f;
            return anchor + new Vector3((index - (total - 1) * 0.5f) * spread, 0f, 0f);
        }
    }
}
