using SoldierADay.Protocol;
using UnityEngine;

namespace SoldierADay.Net
{
    /// <summary>
    /// 연속 A 상승 곡선 (와리오웨어 Speed Up).
    ///
    /// 보상(등급별 진급 점수)은 서버 소관이라 여기서 건드리지 않는다 — 조이는
    /// 것은 오직 **제한 시간**뿐이다. 연속으로 A를 받을수록 다음 판이 조금씩
    /// 빡빡해지고, A가 아닌 등급이 나오는 순간 스택이 풀린다. 계속 잘해야
    /// 압박이 유지되고, 한 번 흔들리면 다시 처음 속도로 돌아간다.
    ///
    /// ── 왜 static인가 ────────────────────────────────────────────────────
    /// 하루 동안 여러 판을 옮겨 다니며 이어지는 값이라 `QuestPlay` 인스턴스
    /// 하나에 담기 어렵다(판이 열릴 때마다 새 `Board`가 생긴다). 세션 전체에
    /// 하나만 있으면 되므로 `BoardLab`의 로그와 같은 자리에 둔다.
    /// </summary>
    public static class GradeStreak
    {
        /// <summary>최대 3단 — 그 이상 조이면 판을 아예 못 끝내는 지경이 된다</summary>
        private const int MaxStack = 3;

        /// <summary>스택 1당 5%씩 — 최대 3단이면 −15%까지 조인다</summary>
        private const float StepScale = 0.05f;

        public static int Stack { get; private set; }

        /// <summary>판 하나의 결과를 반영한다. A면 스택이 오르고, 아니면 풀린다</summary>
        public static void Record(string grade)
        {
            Stack = grade == SnapshotQuestsItemGradeValues.A
                ? Mathf.Min(Stack + 1, MaxStack)
                : 0;
        }

        /// <summary>다음 판의 제한 시간에 곱할 배율. 스택이 없으면 1(무변화)</summary>
        public static float LimitScale => 1f - StepScale * Stack;
    }
}
