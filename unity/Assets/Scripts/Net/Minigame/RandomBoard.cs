using SoldierADay.Protocol;
using UnityEngine;

namespace SoldierADay.Net
{
    /// <summary>
    /// `RANDOM` 지원 요청 — 1건. `타 분대 지원 요청`.
    ///
    /// **다른 구역의 미니게임 하나를 무작위로 불러온다.** 다른 분대를 도우러
    /// 가는 일이라 무슨 일을 하게 될지 모르는 것이 이 일과의 전부다.
    ///
    /// 파라미터는 없다. 불려 나온 원형은 제 기본값으로 돈다 — 남의 구역 일이라
    /// 우리 장비도 우리 배치도 아니다.
    ///
    /// 같은 일과는 늘 같은 것을 부른다. 씨앗이 일과 id에서 나오므로, 다시
    /// 시도해도 판이 바뀌지 않는다 — 실패하고 다시 열 때마다 다른 게임이 나오면
    /// 재시도가 뽑기가 된다.
    /// </summary>
    public sealed class RandomBoard : Board
    {
        /// <summary>부를 수 있는 것. `REACT`는 단독으로 쓰지 않으므로 빠진다</summary>
        private static readonly string[] Pool =
        {
            SnapshotQuestsItemMinigameTypeValues.SCRUB,
            SnapshotQuestsItemMinigameTypeValues.PLACE,
            SnapshotQuestsItemMinigameTypeValues.AUDIT,
            SnapshotQuestsItemMinigameTypeValues.MASH,
            SnapshotQuestsItemMinigameTypeValues.BALANCE,
            SnapshotQuestsItemMinigameTypeValues.HOLD,
            SnapshotQuestsItemMinigameTypeValues.TRACE,
            SnapshotQuestsItemMinigameTypeValues.SORT,
            SnapshotQuestsItemMinigameTypeValues.TIMING,
            SnapshotQuestsItemMinigameTypeValues.SEQ,
            SnapshotQuestsItemMinigameTypeValues.RHYTHM,
            SnapshotQuestsItemMinigameTypeValues.TRACK,
            SnapshotQuestsItemMinigameTypeValues.SEARCH,
        };

        private Board _inner;
        private string _summoned;

        public override string Instruction => _inner?.Instruction ?? "";
        public override string Status =>
            $"{Boards.Name(_summoned)} — 남의 구역 일이다" +
            (string.IsNullOrEmpty(_inner?.Status) ? "" : $"  ·  {_inner.Status}");

        protected override void Setup()
        {
            _summoned = Pool[Rng.Next(Pool.Length)];
            _inner = Boards.Create(_summoned);

            // **파라미터를 물려주지 않는다.** `RANDOM`에는 애초에 없고, 불려 나온
            // 원형은 `Param`의 기본값으로 돈다. 난이도만 넘겨 판의 무게를 맞춘다
            _inner.Begin(new SnapshotQuestsItemMinigame
            {
                type = _summoned,
                variant = "summon",
                difficulty = Spec?.difficulty ?? 2,
            }, Limit, $"{_summoned}:{Spec?.variant}");
        }

        protected override void Advance(float dt, BoardInput input)
        {
            if (_inner == null) { Fail(); return; }

            var state = _inner.Tick(dt, input);
            Fill = _inner.Fill;

            // 실수도 그대로 물려받는다 — 등급은 실제로 한 일에서 나온다
            while (Mistakes < _inner.Mistakes) Miss();

            if (state == BoardState.Cleared) Clear();
            else if (state == BoardState.Failed) Fail();
        }

        public override void Draw(HudTheme theme, Rect body) => _inner?.Draw(theme, body);
    }
}
