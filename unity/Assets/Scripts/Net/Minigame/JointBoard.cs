using SoldierADay.Protocol;
using UnityEngine;

namespace SoldierADay.Net
{
    /// <summary>
    /// QST-01 합동 판 — **판 하나를 인원이 나눠 채운다.**
    ///
    /// 각자 같은 게임을 따로 통과하는 것이 아니다. 목표가 조각으로 쪼개져 있고,
    /// 누가 채우든 같은 칸에 쌓인다 — 남이 채운 조각이 내 화면에서도 채워지고,
    /// 인원이 모자라면 아무리 눌러도 하나도 오르지 않는다.
    ///
    /// ── 겉을 씌우는 이유 ─────────────────────────────────────────────────
    /// 합동 전용 원형을 새로 만들지 않는다. 창고 재물조사는 `AUDIT`이고 하역
    /// 릴레이는 `MASH`다 — 다른 것은 **누가 채우는가**뿐이라, 안쪽 판은 그대로
    /// 두고 조각을 세는 겉을 한 겹 씌운다(`InterruptBoard`와 같은 방식).
    ///
    /// ── 안쪽 판은 계속 다시 열린다 ───────────────────────────────────────
    /// 제 몫을 다 돌면 안쪽 판이 통과하는데, 거기서 끝내면 남은 조각을 채울
    /// 방법이 없다. 그래서 곧바로 새 판을 차린다 — 릴레이에서 다음 상자가 오는 것과 같다.
    ///
    /// ── 통과는 서버가 정한다 ─────────────────────────────────────────────
    /// 조각이 다 차면 서버가 완료로 넘기고 스냅샷의 상태가 바뀐다. 이 판은
    /// 스스로 통과를 선언하지 않는다 — 분대가 채운 것을 개인이 선언할 수 없다.
    ///
    /// ── B-1 정보 비대칭 ───────────────────────────────────────────────────
    /// SEQ·TRACE 합동판은 서버가 `watch`(정답만)와 `operate`(조작만)를 나눠
    /// 준다(`Snapshot.quests[].jointRoles`, `QuestPlay.Feed`가 내 몫을 찾아
    /// `MyRole`에 넣는다). 빈 문자열이면 비대칭이 꺼진 것이다 — 그때는
    /// 예전과 똑같이 전원이 같은 판을 돌린다.
    ///
    /// **근사가 아니라 차단이다(실플레이 반려 재작업).** 처음 구현은 `show`·
    /// `rotations` 파라미터를 극단으로 밀어 흉내만 냈다 — 그 결과 정답
    /// 역할도 안쪽 판 조작이 그대로 먹혔고(입력을 안 끊었으니까), 조작
    /// 역할은 `show=0`이 `SeqBoard.MinRevealDuration`(0.6초) 바닥에 걸려
    /// 정답이 잠깐 샜다. 지금은 안쪽 판에 같은 `Spec`을 그대로 주고, 대신
    /// 역할별로 다른 문을 연다:
    ///   - watch·SEQ — 안쪽 판의 `SeqBoard.Locked`를 켜 입력을 원천에서
    ///     끊고, `SeqBoard.DrawAnswer`로 노출 타이머 그대로 "봤다가
    ///     사라지는" 정답만 그린다(입력 칸 없음).
    ///   - watch·TRACE — 안쪽 판을 아예 돌리지 않고(Tick 자체를 안 부른다)
    ///     `TraceBoard.DrawSolved`로 스크램블 전 배치를 정적으로 그린다.
    ///   - operate·SEQ — `SeqBoard.NoReveal`을 켜 노출 단계 자체를 지운다.
    ///   - operate·TRACE — 그대로. 스크램블된 판을 직접 돌린다.
    /// 이 갈림은 `OpenRound`(안쪽 판 인스턴스에 스위치를 꽂는 곳)와
    /// `Advance`·`Draw`(그 스위치에 맞게 부르는 곳)에 있다.
    ///
    /// 두 화면이 같은 문제를 봐야 부를 말이 맞아떨어진다. 그래서 라운드
    /// 시드를 로컬 카운터(`_round`)가 아니라 **분대가 같이 보는 값**
    /// (`Done`을 `PerRound`로 나눈 라운드 번호)에서 뽑는다 — 스냅샷이 같으면
    /// 두 클라이언트가 같은 문제를 연다.
    /// </summary>
    public sealed class JointBoard : Board
    {
        private Board _inner;
        private int _round;
        private int _roundIndex;

        /// <summary>서버가 센 조각. `QuestPlay`가 스냅샷에서 넣어준다</summary>
        public int Done { get; set; }
        public int Total { get; set; } = 1;
        /// <summary>요구 인원과 지금 그 자리에 있는 인원 — 미달이면 경고를 띄운다</summary>
        public int NeedActors { get; set; } = 2;
        public int HereActors { get; set; }

        /// <summary>
        /// B-1 — 이 방·이 조각에서 내 배정. `watch`·`operate`·`""`(비대칭 꺼짐) 중 하나.
        /// `QuestPlay`가 스냅샷의 `jointRoles`에서 내 memberId를 찾아 매 프레임 넣어준다.
        /// </summary>
        public string MyRole { get; set; } = "";

        private bool IsWatch => MyRole == SnapshotQuestsItemJointRolesItemRoleValues.Watch;
        private bool IsOperate => MyRole == SnapshotQuestsItemJointRolesItemRoleValues.Operate;
        private bool Asymmetric => !string.IsNullOrEmpty(MyRole);

        /// <summary>조각 하나를 채웠다. `QuestPlay`가 서버로 올린다</summary>
        public System.Action OnStep;

        /// <summary>한 사람이 한 판에서 낼 조각 수 — 요구 인원이 나눠 가진다</summary>
        private int PerRound => Mathf.Max(1, Mathf.RoundToInt((float)Total / Mathf.Max(1, NeedActors)));

        private int _sentThisRound;
        private bool Short => HereActors < NeedActors;

        public override string Instruction =>
            Short ? $"{NeedActors}인이 모여야 진행된다 — 지금 {HereActors}명"
            : IsWatch ? "정답을 불러줘라 — 누르지 마라"
            : IsOperate ? "들은 대로 눌러라 — 정답은 안 보인다"
            : _inner?.Instruction ?? "";

        public override string Status =>
            $"분대 {Done}/{Total} 조각" +
            (IsWatch ? "  ·  정답" : IsOperate ? "  ·  조작" : "") +
            (string.IsNullOrEmpty(_inner?.Status) ? "" : $"  ·  내 판 {_inner.Status}");

        protected override void Setup()
        {
            _round = 0;
            OpenRound();
        }

        private void OpenRound()
        {
            _sentThisRound = 0;
            _inner = Boards.Create(Spec?.type);

            // 비대칭이면 두 클라이언트가 같은 문제를 봐야 한다. 조각 수(Done)를
            // 요구 인원으로 나눈 "라운드 번호"가 그 열쇠다 — 스냅샷이 전원에게
            // 같은 Done을 실어 나르므로, 같은 라운드 번호면 같은 씨앗이 나온다.
            // 로컬 라운드 카운터(`_round`)를 그대로 쓰면 정답 역할과 조작
            // 역할이 서로 다른 화면을 각자 혼자 돌리게 된다.
            _roundIndex = Done / Mathf.Max(1, PerRound);
            var seed = Asymmetric
                ? $"{Spec?.variant}#joint#{_roundIndex}"
                : $"{Spec?.variant}#{_round}";

            // B-1 — 역할별 실제 차이는 `Spec` 파라미터를 흉내 내는 것이 아니라
            // 안쪽 판 인스턴스에 스위치를 직접 꽂는다(위 클래스 요약). 둘 다
            // `Asymmetric`이 꺼져 있으면(=역할이 없으면) 항상 false라 기존
            // 12개 합동판은 이 블록이 없는 것과 같다.
            if (_inner is SeqBoard seq)
            {
                seq.Locked = IsWatch;
                seq.NoReveal = IsOperate;
            }

            _inner.Begin(Spec, Limit, seed);
            _round += 1;
        }

        // 제한 시간은 시간대가 정한다 (QST-01). 판이 스스로 실패하지 않는다
        protected override bool TimesOut => false;

        protected override void Advance(float dt, BoardInput input)
        {
            if (_inner == null) return;

            Fill = Total <= 0 ? 0f : Mathf.Clamp01((float)Done / Total);

            // **미달이면 손이 안 먹는다.** 게이지가 안 차는 것을 보여주는 것보다
            // 아예 못 만지게 하는 편이 "인원을 모아라"를 더 빨리 읽힌다. 안쪽
            // 판도 아예 돌리지 않는다 — 어느 역할이든 이 프레임엔 조작이 없다
            if (Short) return;

            if (IsWatch)
            {
                // 정답 역할은 절대 조작하지 않는다 — 커튼이 아니라 차단이다.
                // SEQ는 `SeqBoard.Locked`가 입력을 원천에서 끊으므로 `Tick`을
                // 불러도 안전하고, 그래야 노출 타이머가 흘러 "봤다가
                // 사라지는" 것이 보인다. TRACE는 회전이 곧 입력이라 아예
                // 돌리지 않는다(Tick 자체를 안 부른다) — `Draw`가 스크램블
                // 전 배치를 정적으로 그린다.
                if (_inner is SeqBoard) _inner.Tick(dt, default);

                // 정답 역할은 스스로 라운드를 못 끝낸다(입력이 없으니 안쪽
                // 판이 영원히 Running이다). 분대 조각 수(Done)가 라운드
                // 경계를 넘으면 그게 "조작 역할이 다음 문제로 넘어갔다"는
                // 신호다.
                var roundIndex = Done / Mathf.Max(1, PerRound);
                if (roundIndex != _roundIndex) OpenRound();
                return;
            }

            var state = _inner.Tick(dt, input);

            // 안쪽 판이 얼마나 갔는지를 조각으로 환산해 올린다
            var earned = Mathf.FloorToInt(_inner.Fill * PerRound);
            while (_sentThisRound < earned)
            {
                _sentThisRound += 1;
                OnStep?.Invoke();
            }

            // 제 몫을 다 돌았으면 다음 판을 차린다 — 남은 조각은 아직 남아 있다
            if (state != BoardState.Running) OpenRound();
        }

        public override void Draw(HudTheme theme, Rect body)
        {
            // 조각 띠 — **이 판의 주인공이다.** 남이 채운 것이 여기서 보인다
            var strip = new Rect(body.x, body.y, body.width, 34f);
            var cell = (strip.width - (Total - 1) * 4f) / Mathf.Max(1, Total);
            for (var i = 0; i < Total; i += 1)
            {
                var box = new Rect(strip.x + i * (cell + 4f), strip.y, cell, strip.height);
                theme.Fill(box, i < Done ? HudTheme.Accent : HudTheme.Paper);
                theme.Border(box, i < Done ? HudTheme.Accent : HudTheme.Rule2);
            }

            var inner = new Rect(body.x, strip.yMax + 12f, body.width, body.height - strip.height - 12f);

            // B-1 — 역할마다 아예 다른 화면을 그린다. 근사(반투명 커튼)가
            // 아니라 구조 자체가 다르다 — 정답 역할은 입력 칸을 그릴 코드
            // 경로 자체가 없고, 조작 역할은 정답을 그릴 코드 경로 자체가
            // 없다. 배너로 한 번 더 못 박는 이유는 클래스 요약에 적었다.
            if (!Short && IsWatch)
            {
                var content = DrawRoleBanner(theme, inner, "정답 화면 — 조작자에게 불러줘라", HudTheme.Accent);
                if (_inner is SeqBoard seq) seq.DrawAnswer(theme, content);
                else if (_inner is TraceBoard trace) trace.DrawSolved(theme, content);
                else _inner?.Draw(theme, content);
            }
            else if (!Short && IsOperate)
            {
                var content = DrawRoleBanner(theme, inner, "조작 화면 — 들은 대로 하라", HudTheme.Cold);
                _inner?.Draw(theme, content);
            }
            else
            {
                _inner?.Draw(theme, inner);
            }

            if (!Short) return;

            // 인원 미달 — 게이지가 안 차는 이유를 화면이 말한다 (§7.1.5)
            var warn = new Rect(inner.x, inner.center.y - 46f, inner.width, 92f);
            theme.Fill(warn, HudTheme.AlertW, 0.95f);
            theme.Border(warn, HudTheme.Alert, 2f);
            GUI.Label(new Rect(warn.x, warn.y + 14f, warn.width, 32f),
                $"인원 미달 — {HereActors}/{NeedActors}",
                theme.At(theme.Heading, 22, HudTheme.Alert, TextAnchor.MiddleCenter));
            GUI.Label(new Rect(warn.x, warn.y + 50f, warn.width, 28f),
                "혼자서는 한 조각도 오르지 않는다",
                theme.At(theme.Body, 17, HudTheme.Ink, TextAnchor.MiddleCenter));
        }

        /// <summary>배너 높이 — 이 아래로 본문이 그려진다</summary>
        private const float RoleBannerHeight = 32f;

        /// <summary>
        /// B-1 — 역할 배너. 화면 구조 자체가 역할마다 달라(입력 칸이 있고
        /// 없고) 배너 없이도 헷갈리지 않겠지만, 실플레이 반려("판도 보이고
        /// 조작도 된다")가 보여준 것은 사람이 화면을 스치듯 보고 판단한다는
        /// 것이다 — 글자로 한 번 더 못 박는다. 반환값은 배너를 뺀 나머지
        /// 자리라, 안쪽 판은 배너와 안 겹친다.
        /// </summary>
        private static Rect DrawRoleBanner(HudTheme theme, Rect area, string text, Color accent)
        {
            var banner = new Rect(area.x, area.y, area.width, RoleBannerHeight);
            theme.Fill(banner, accent, 0.16f);
            theme.Border(banner, accent, 2f);
            GUI.Label(banner, text, theme.At(theme.Heading, 16, accent, TextAnchor.MiddleCenter));
            return new Rect(area.x, banner.yMax + 8f, area.width, area.height - RoleBannerHeight - 8f);
        }
    }
}
