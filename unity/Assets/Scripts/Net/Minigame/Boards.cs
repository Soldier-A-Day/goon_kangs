using SoldierADay.Protocol;
using UnityEngine;

namespace SoldierADay.Net
{
    /// <summary>
    /// 원형 이름 → 판.
    ///
    /// 서버가 준 `minigame.type`으로만 고른다. 예전처럼 일과 **이름**을 보고
    /// 고르지 않는다 — 그러면 "총기 수입"과 "공구 정리"가 같은 판이 되고,
    /// 새 일과를 넣을 때마다 클라의 키워드 표를 같이 고쳐야 한다.
    /// </summary>
    public static class Boards
    {
        public static Board Create(string type) => type switch
        {
            SnapshotQuestsItemMinigameTypeValues.SCRUB => new ScrubBoard(),
            SnapshotQuestsItemMinigameTypeValues.PLACE => new PlaceBoard(),
            SnapshotQuestsItemMinigameTypeValues.AUDIT => new AuditBoard(),
            SnapshotQuestsItemMinigameTypeValues.MASH => new MashBoard(),
            SnapshotQuestsItemMinigameTypeValues.BALANCE => new BalanceBoard(),
            SnapshotQuestsItemMinigameTypeValues.HOLD => new HoldBoard(),
            SnapshotQuestsItemMinigameTypeValues.TRACE => new TraceBoard(),
            SnapshotQuestsItemMinigameTypeValues.SORT => new SortBoard(),
            SnapshotQuestsItemMinigameTypeValues.TIMING => new TimingBoard(),
            SnapshotQuestsItemMinigameTypeValues.SEQ => new SeqBoard(),
            SnapshotQuestsItemMinigameTypeValues.RHYTHM => new RhythmBoard(),
            SnapshotQuestsItemMinigameTypeValues.TRACK => new TrackBoard(),
            SnapshotQuestsItemMinigameTypeValues.SEARCH => new SearchBoard(),
            SnapshotQuestsItemMinigameTypeValues.REACT => new ReactBoard(),
            SnapshotQuestsItemMinigameTypeValues.RANDOM => new RandomBoard(),
            _ => new PendingBoard(),
        };

        /// <summary>
        /// 판 하나를 통째로 차린다 — 인터럽트 모디파이어까지 얹어서.
        ///
        /// `interrupt`가 붙은 일과는 진행 중인 판을 끊고 경례가 들어온다.
        /// 원형마다 그 처리를 넣으면 열네 번 같은 코드를 쓰게 되므로, 겉을
        /// 한 겹 씌운다(`InterruptBoard`).
        /// </summary>
        public static Board Open(SnapshotQuestsItemMinigame spec, float limitSeconds, string questId)
        {
            var board = Create(spec?.type);
            if (spec?.interrupt == "REACT") board = new InterruptBoard(board);
            board.Begin(spec, limitSeconds, questId);
            return board;
        }

        /// <summary>원형 이름표 — 판 머리띠에 붙는다</summary>
        public static string Name(string type) => type switch
        {
            SnapshotQuestsItemMinigameTypeValues.SCRUB => "문지르기",
            SnapshotQuestsItemMinigameTypeValues.PLACE => "정돈 · 배치",
            SnapshotQuestsItemMinigameTypeValues.AUDIT => "대조 점검",
            SnapshotQuestsItemMinigameTypeValues.MASH => "연타 작업",
            SnapshotQuestsItemMinigameTypeValues.BALANCE => "균형 운반",
            SnapshotQuestsItemMinigameTypeValues.HOLD => "계기 유지",
            SnapshotQuestsItemMinigameTypeValues.TRACE => "경로 잇기",
            SnapshotQuestsItemMinigameTypeValues.SORT => "분류",
            SnapshotQuestsItemMinigameTypeValues.TIMING => "타이밍",
            SnapshotQuestsItemMinigameTypeValues.SEQ => "순서 입력",
            SnapshotQuestsItemMinigameTypeValues.RHYTHM => "박자",
            SnapshotQuestsItemMinigameTypeValues.TRACK => "조준 유지",
            SnapshotQuestsItemMinigameTypeValues.SEARCH => "탐색",
            SnapshotQuestsItemMinigameTypeValues.REACT => "즉시 반응",
            SnapshotQuestsItemMinigameTypeValues.RANDOM => "지원 요청",
            _ => "일과",
        };
    }

    /// <summary>
    /// 판이 없는 일과 — 회복 행동 · 합동 · 훈련 체크포인트.
    ///
    /// **통과를 선언하지 않는다.** 이쪽은 서버가 시간으로 완료하므로
    /// (`applyWork`), 판은 붙잡고 있다는 신고를 계속 내보내는 일만 한다.
    /// 완료되면 스냅샷의 상태가 바뀌고 `QuestPlay`가 판을 닫는다.
    ///
    /// 예전에는 여기서 판을 아예 안 띄웠다. 그런데 `interact`를 보내는 곳이
    /// `QuestPlay` 하나뿐이라, 판을 안 띄우면 붙잡는 주체도 같이 사라진다 —
    /// 밥도 세면도 영영 못 하게 되고, E를 눌러도 아무 일이 안 일어난다.
    /// </summary>
    public sealed class ChoreBoard : Board
    {
        public override string Instruction => "그 자리에서 하는 일이다 — 끝날 때까지 둔다";
        public override string Status => "붙잡고 있는 동안 시간이 쌓인다";

        /// <summary>서버가 세는 진척. `QuestPlay`가 매 프레임 넣어준다</summary>
        public float Served { get; set; }

        protected override void Setup() { }
        protected override bool TimesOut => false;
        protected override void Advance(float dt, BoardInput input) => Fill = Served;

        public override void Draw(HudTheme theme, Rect body)
        {
            theme.Fill(body, HudTheme.Paper3);
            var bar = new Rect(body.center.x - 240f, body.center.y - 14f, 480f, 28f);
            theme.Bar(bar, Fill, HudTheme.Accent);
            GUI.Label(new Rect(body.x, bar.yMax + 16f, body.width, 26f),
                $"{Mathf.RoundToInt(Fill * 100f)}%",
                theme.At(theme.MonoBig, 24, HudTheme.Ink, TextAnchor.UpperCenter));
            theme.Border(body, HudTheme.Rule);
        }
    }

    /// <summary>
    /// 아직 안 만든 원형.
    ///
    /// **미구현이 진행을 막으면 안 된다.** 판이 붙은 일과는 통과해야 끝나므로,
    /// 판이 없으면 그 일과는 영영 못 끝낸다 — 하루 판정이 통째로 무너진다.
    /// 그래서 붙잡고 있으면 소요만큼 흘려보내고 B로 통과시킨다. 예전의
    /// "E를 누르고 있기"와 같은 것이고, 여기에만 남겨 둔다.
    /// </summary>
    public sealed class PendingBoard : Board
    {
        public override string Instruction => "붙잡고 있어라";
        public override string Status => "이 일과의 판은 아직 만들지 않았다";

        protected override void Setup() { }

        protected override void Advance(float dt, BoardInput input)
        {
            if (!input.Hold) return;
            Fill = Mathf.Clamp01(Fill + dt / Limit);
            if (Fill >= 0.98f) Clear();
        }

        public override void Draw(HudTheme theme, Rect body)
        {
            theme.Fill(body, HudTheme.Paper3);
            var bar = new Rect(body.center.x - 220f, body.center.y - 12f, 440f, 24f);
            theme.Bar(bar, Fill, HudTheme.Heat);
            GUI.Label(new Rect(body.x, bar.yMax + 14f, body.width, 24f),
                "누르고 있으면 진행된다",
                theme.At(theme.Small, 14, HudTheme.Ink3, TextAnchor.UpperCenter));
            theme.Border(body, HudTheme.Rule);
        }
    }
}
