using SoldierADay.Protocol;
using UnityEngine;

namespace SoldierADay.Net
{
    /// <summary>
    /// 일과를 **어떻게 하는가** (`docs/game_spec.md`).
    ///
    /// E를 누르면 그 일과의 판이 열리고, **통과해야 끝난다.** 못 끝내면 판이
    /// 다시 열리고, 계속 실패하면 시간대 종료로 잠긴다 — 잃은 것은 시간이다.
    ///
    /// ── 무엇이 바뀌었나 ──────────────────────────────────────────────────
    /// 예전에는 클라가 일과 **이름**을 보고 다섯 유형 중 하나를 골랐다. 서버가
    /// 안 바뀐다는 것이 이유였는데, 그 대가로 "총기 수입"과 "공구 정리"가 같은
    /// 판이 됐다. 지금은 `quests.json`이 69건의 원형과 파라미터를 소유하고
    /// 스냅샷으로 내려온다 — 같은 사실은 한 곳에만 있다(ARCH-02).
    ///
    /// 완료도 바뀌었다. 예전에는 붙잡고 있는 시간이 곧 완료였다. 지금은 판을
    /// 통과했을 때만 `questCleared`를 보내고, 서버가 자격을 검증한 뒤 완료로
    /// 넘긴다 — 구역·시간대·소유자·최소 진척은 여전히 전부 서버가 막는다.
    ///
    /// ── 진척은 여전히 서버가 센다 ────────────────────────────────────────
    /// 판이 열려 있는 동안 `interact`를 켜 두고, 그 시간을 서버가 센다. 그래야
    /// 엉뚱한 자리에 서 있으면 판을 통과해도 아무 일이 없다.
    /// </summary>
    [DefaultExecutionOrder(45)]
    public sealed class QuestPlay : MonoBehaviour
    {
        public GameClient client;
        public Interactor interactor;
        public LocalPlayer player;

        /// <summary>
        /// 판 열림/닫힘 페이드. `Evacuation`이 쓰는 것과 같은 슬롯이지만,
        /// 그쪽은 쓰러진 동안 값을 계속 밀어 넣고 이쪽은 200ms 펄스 동안만
        /// 건드린다 — 펄스가 끝나면 손을 뗀다. 그래야 두 시스템이 같은
        /// 프레임에 같은 필드를 쓰는데도 서로 지우지 않는다.
        /// </summary>
        public ScreenEffects effects;

        /// <summary>지금 열려 있는 판의 일과. 없으면 null</summary>
        public string QuestId { get; private set; }
        public string Label { get; private set; } = "";

        /// <summary>0~1. 서버가 세는 진척 — 화면이 따로 세지 않는다</summary>
        public float Progress { get; private set; }

        public Board Board { get; private set; }

        /// <summary>
        /// 통과했지만 아직 신고하지 않았다.
        ///
        /// 서버는 붙잡고 있던 시간이 소요의 절반에 못 미치면 통과를 무시한다.
        /// 판을 빨리 끝냈으면 그 문턱을 넘을 때까지 여기서 기다린다 — 안 그러면
        /// 통과를 외쳐도 아무 일이 없는 것처럼 보이고, 원인을 찾을 길이 없다.
        /// </summary>
        public bool Settling { get; private set; }

        /// <summary>실패 화면의 버튼이 눌렸다 — `HudMinigame`이 채우고 여기서 읽는다</summary>
        public bool Retry { get; set; }
        public bool Quit { get; set; }

        /// <summary>서버 `CLEAR_MIN_RATIO`와 같은 값. 어긋나면 통과가 조용히 무시된다</summary>
        private const float ClearMinProgress = 0.5f;

        /// <summary>결과를 읽을 시간. 통과한 판이 즉시 닫히면 등급이 안 보인다</summary>
        private const float ResultHold = 1.1f;

        private bool _sending;
        private float _resultAge;
        private SnapshotQuestsItemMinigame _spec;

        /// <summary>1 → 0, 200ms. 판이 열리거나 닫힐 때 한 번 튄다</summary>
        private float _fadePulse;

        private void Update()
        {
            if (_fadePulse > 0f)
            {
                _fadePulse = Mathf.Max(0f, _fadePulse - Time.deltaTime / 0.2f);
                if (effects != null) effects.fadeOut = _fadePulse;
            }

            if (QuestId != null && !Alive(QuestId))
            {
                Close();
                return;
            }

            if (QuestId == null)
            {
                var near = interactor != null ? interactor.Nearest : null;
                if (near == null || !Input.GetKeyDown(KeyCode.E)) return;
                Open(near.questId, near.label);
                return;
            }

            if (Input.GetKeyDown(KeyCode.Escape) || Quit) { Close(); return; }

            switch (Board.State)
            {
                case BoardState.Running:
                    Send(true);
                    // 판이 없는 일과는 서버가 세는 진척이 곧 화면이다
                    if (Board is ChoreBoard chore) chore.Served = Progress;
                    if (Board is JointBoard joint) Feed(joint);
                    Board.Tick(Time.deltaTime, BoardInput.Read());
                    break;

                case BoardState.Cleared:
                    Settle();
                    break;

                case BoardState.Failed:
                    // 붙잡은 것을 놓는다 — 실패한 판에 시간을 계속 태우지 않는다
                    Send(false);
                    _resultAge += Time.deltaTime;
                    // 남은 시간이 있으면 또 해볼 수 있다. 마우스·키 둘 다 받는다 —
                    // 판 대부분이 마우스 조작이라 실패하는 순간 손이 거기 있다
                    if (_resultAge > ResultHold && (Retry || Input.GetKeyDown(KeyCode.E))) Restart();
                    break;
            }
        }

        /// <summary>
        /// 통과 뒤. 서버의 최소 진척을 넘길 때까지 붙잡은 채로 기다린다.
        ///
        /// 설계안이 "일찍 끝내도 시간은 그대로 흐른다"고 한 것이 여기서 그대로
        /// 성립한다 — 다만 전부가 아니라 절반까지다. 그 위로는 실력이 시간을 번다.
        /// </summary>
        private void Settle()
        {
            _resultAge += Time.deltaTime;

            if (Progress < ClearMinProgress)
            {
                Settling = true;
                Send(true);
                return;
            }

            Settling = false;
            if (_resultAge < ResultHold) return;

            // 판이 없는 일과는 통과를 신고할 것이 없다 — 서버가 시간으로 끝낸다
            if (_spec != null) client?.ClearQuest(QuestId, Board.Grade());
            // 완료 선언은 서버가 한다. 여기서는 붙잡은 것을 놓고 판을 닫을 뿐이다
            Send(false);
            Close();
        }

        private void Open(string questId, string label)
        {
            var quest = Find(questId);
            if (quest == null) return;

            QuestId = questId;
            Label = label;
            Progress = (float)quest.progress;
            _spec = string.IsNullOrEmpty(quest.minigame?.type) ? null : quest.minigame;
            _resultAge = 0f;
            Settling = false;
            _fadePulse = 1f;

            // **판이 없는 일과도 판을 띄운다.**
            //
            // 회복 행동 · 합동 · 훈련 체크포인트가 여기 해당한다. 예전에는 여기서
            // 그냥 빠져나갔는데, `interact`를 보내는 곳이 이 클래스 하나뿐이라
            // 판을 안 띄우면 붙잡는 주체도 같이 사라진다 — 밥도 세면도 영영 못
            // 하게 되고, E를 눌러도 아무 일이 안 일어난다.
            Board = _spec == null
                ? Chore((float)quest.workSeconds, questId)
                : quest.jointTotal > 0
                    ? Joint(quest, questId)
                    : Boards.Open(_spec, (float)quest.workSeconds, questId);

            // 판이 화면을 덮으므로 걷지 못한다. 그 자리에서 하는 일이다
            if (player != null) player.Suspended = true;
        }

        /// <summary>
        /// 다시 하기.
        ///
        /// 제한 시간은 처음과 같다. 실패할 때마다 짧아지면 한 번 미끄러진 일과는
        /// 영영 못 끝내게 되고, 그건 재시도가 아니라 잠금이다. 압박은 시간대가 준다.
        ///
        /// 씨앗도 같아서 **같은 판이 다시 나온다.** 두 번째는 아는 판이어야
        /// 실력이 늘고, 매번 새 배치가 나오면 재시도가 뽑기가 된다.
        /// </summary>
        private void Restart()
        {
            Retry = false;
            _resultAge = 0f;
            Settling = false;

            var quest = Find(QuestId);
            var limit = quest != null ? (float)quest.workSeconds : Board.Limit;
            Board = _spec == null ? Chore(limit, QuestId) : Boards.Open(_spec, limit, QuestId);
        }

        /// <summary>
        /// 합동 판.
        ///
        /// 혼자 통과하는 판이 아니라 **인원이 나눠 채우는 판**이라 겉을 한 겹
        /// 씌운다. 조각은 서버가 세고, 통과 선언도 서버가 한다.
        /// </summary>
        private Board Joint(SnapshotQuestsItem quest, string questId)
        {
            var board = new JointBoard
            {
                Total = Mathf.Max(1, (int)quest.jointTotal),
                Done = (int)quest.jointDone,
                NeedActors = Mathf.Max(1, (int)quest.minActors),
            };
            board.OnStep = () => client?.JointStep(questId);
            board.Begin(_spec, (float)quest.workSeconds, questId);
            return board;
        }

        private static Board Chore(float limitSeconds, string questId)
        {
            var board = new ChoreBoard();
            board.Begin(null, limitSeconds, questId);
            return board;
        }

        /// <summary>합동 판에 서버 값을 넣어준다 — 남이 채운 조각과 지금 모인 인원</summary>
        private void Feed(JointBoard board)
        {
            var quest = Find(QuestId);
            if (quest == null) return;

            board.Done = (int)quest.jointDone;
            board.Total = Mathf.Max(1, (int)quest.jointTotal);
            board.NeedActors = Mathf.Max(1, (int)quest.minActors);

            // 그 구역에 실제로 서 있는 인원. 이동 중은 세지 않는다 — 서버의
            // `actorsAt`과 같은 잣대여야 화면과 실제가 갈라지지 않는다
            var here = 0;
            var snapshot = client?.Latest;
            if (snapshot?.members != null)
            {
                foreach (var m in snapshot.members)
                {
                    if (m == null || m.zone != quest.zone) continue;
                    if (m.presence == SnapshotMembersItemPresenceValues.Evacuated) continue;
                    if (m.travelRemainingMs > 0d) continue;
                    here += 1;
                }
            }
            board.HereActors = here;
        }

        private SnapshotQuestsItem Find(string questId)
        {
            var snapshot = client != null ? client.Latest : null;
            if (snapshot?.quests == null || questId == null) return null;

            foreach (var quest in snapshot.quests)
            {
                if (quest != null && quest.id == questId) return quest;
            }
            return null;
        }

        /// <summary>그 일과가 아직 살아 있는가 — 시간대 종료·완료면 판을 닫는다</summary>
        private bool Alive(string questId)
        {
            var quest = Find(questId);
            if (quest == null) return false;

            Progress = (float)quest.progress;
            return quest.status != SnapshotQuestsItemStatusValues.Done &&
                   quest.status != SnapshotQuestsItemStatusValues.Locked &&
                   quest.status != SnapshotQuestsItemStatusValues.Failed;
        }

        private void Send(bool active)
        {
            if (client == null || QuestId == null) return;
            if (active == _sending) return;
            _sending = active;
            client.Interact(QuestId, active);
        }

        public void Close()
        {
            Retry = false;
            Quit = false;
            Send(false);
            QuestId = null;
            Board = null;
            Settling = false;
            _resultAge = 0f;
            _spec = null;
            _fadePulse = 1f;
            if (player != null) player.Suspended = false;
        }
    }
}
