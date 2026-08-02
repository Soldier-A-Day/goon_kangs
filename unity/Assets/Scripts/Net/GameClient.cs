using System;
using SoldierADay.Protocol;
using UnityEngine;

namespace SoldierADay.Net
{
    /// <summary>
    /// 서버 메시지를 형태 있는 것으로 바꿔주는 얇은 층.
    ///
    /// 하는 일은 두 가지뿐이다 — 들어온 JSON을 생성된 DTO로 파싱하고, 나갈 의도를 JSON으로 만든다.
    /// **판정도 예측도 하지 않는다.** 남은 시간을 스스로 세거나 완료를 미리 정하면
    /// 그 순간 클라이언트가 규칙을 갖게 되고 ARCH-02가 무너진다.
    ///
    /// 스냅샷 사이를 부드럽게 잇는 보간은 표시 계층의 몫이며, 판정에는 영향을 주지 않는다.
    /// </summary>
    [RequireComponent(typeof(GameSocket))]
    public sealed class GameClient : MonoBehaviour
    {
        [Tooltip("게임서버 WS 주소. 로비가 발급한 토큰을 쿼리로 붙인다.")]
        public string serverUrl = "ws://localhost:8080/ws";

        [Tooltip("로비(Next.js)에서 발급받은 단기 세션 토큰 — ARCH-02 핸드오프")]
        public string token = "";

        public event Action<Snapshot> SnapshotReceived;
        public event Action<ServerEvent> EventReceived;
        public event Action<LobbyState> LobbyReceived;

        /// <summary>
        /// 내가 이동 의도를 보냈다. 스냅샷의 travelRemainingMs 는 "어디로"를
        /// 담지 않으므로, 이동 연출이 목적지를 알 길은 이것뿐이다. 판정이 아니라
        /// 기억이다 — 서버가 거절하면 잔여 시간이 오지 않고 연출도 없다.
        /// </summary>
        public event Action<string> MoveRequested;

        /// <summary>서버가 알려준 내 분대원 id. welcome에서 받는다.</summary>
        public string MemberId { get; private set; } = "";

        public Snapshot Latest { get; private set; }

        /// <summary>서버가 연결을 거절한 이유. 없으면 null — HUD가 읽어 그린다</summary>
        public string Rejected { get; private set; }

        /// <summary>
        /// 내부 소켓. NetBootstrap의 재접속 로직이 Opened/Closed/Failed를 구독하려면
        /// 이 통로가 필요하다 — GameClient는 소켓을 감싸기만 할 뿐 끊김을 스스로 다루지 않는다.
        /// </summary>
        public GameSocket Socket => _socket;

        private GameSocket _socket;
        /// <summary>늦게 도착한 스냅샷은 버린다. 순번이 역행하면 화면이 되감긴다.</summary>
        private double _lastSeq = -1;

        private void Awake()
        {
            _socket = GetComponent<GameSocket>();
            _socket.MessageReceived += OnMessage;
        }

        public void Connect()
        {
            if (string.IsNullOrEmpty(token))
            {
                Debug.LogError("[GameClient] 세션 토큰이 없다. 로비에서 발급받아야 한다.");
                return;
            }

            // **새 연결이면 이전 시퀀스는 의미가 없다.**
            //
            // seq 필터(OnMessage의 snapshot 분기)의 목적은 한 연결 안에서 순서가
            // 뒤바뀐 스냅샷을 버리는 것이지, 연결을 가로질러 상태를 지키는 게 아니다.
            // 서버가 잠들었다 깨거나 프로세스가 재시작되면 `resume()`이 되살린 방은
            // seq를 0부터 다시 센다(room.ts) — 그런데 여기서 리셋을 안 하면 재접속
            // 후 서버 seq(1, 2, 3…)가 이전 연결에서 쌓인 `_lastSeq`(수천)를 넘어설
            // 때까지 모든 스냅샷이 조용히 폐기되어, 화면은 아무 오류 없이 멈춘 것처럼
            // 보인다. Render 무료 플랜은 15분 놀면 잠들기 때문에 이 경로는 예외가
            // 아니라 일상이다. 그래서 새 연결을 여는 시점(재접속 포함)마다 여기서
            // 리셋한다 — 서버가 seq를 이어 받든 말든 클라는 항상 안전하다.
            _lastSeq = -1;
            _socket.Connect($"{serverUrl}?token={Uri.EscapeDataString(token)}");
        }

        public void Send(Intent intent)
        {
            _socket.Send(JsonUtility.ToJson(intent));
        }

        /* ------------------------------------------------- 자주 쓰는 의도 */

        /// <summary>
        /// 구역 이동 의도.
        ///
        /// `onFoot`은 **맵에서 실제로 경계를 넘었다**는 신고다. 서버는 인접
        /// 구역인지 확인하고(`zones.ts`) 맞으면 이동 소요를 붙이지 않는다 —
        /// 걷는 데 이미 시간을 썼기 때문이다.
        /// </summary>
        public void Move(string zone, bool onFoot = false)
        {
            Send(new Intent { type = IntentTypeValues.Move, to = zone, onFoot = onFoot });
            MoveRequested?.Invoke(zone);
        }

        public void Interact(string questId, bool active)
        {
            Send(new Intent
            {
                type = IntentTypeValues.Interact,
                questId = questId,
                active = active,
            });
        }

        /// <summary>
        /// B-2 위기 구조 — 쓰러진 동료 곁에서 E 홀드.
        ///
        /// `Interact`와 같은 모양이지만 대상이 퀘스트가 아니라 사람이다. 판정은
        /// 전부 서버(`packages/sim/src/crisis.ts`)가 한다 — 여기서는 "지금 이
        /// 사람을 붙잡고 있다"는 신고만 보낸다.
        /// </summary>
        public void Rescue(string targetId, bool active)
        {
            Send(new Intent
            {
                type = IntentTypeValues.Rescue,
                targetId = targetId,
                active = active,
            });
        }

        /// <summary>
        /// 미니게임 판을 통과했다.
        ///
        /// **완료 시점을 클라만 아는 유일한 경로다.** 서버는 커버리지도 오답
        /// 횟수도 볼 수 없으므로 통과 자체는 받아들이되, `interact`가 통과하는
        /// 관문은 전부 그대로 통과시킨다 — 엉뚱한 구역, 남의 일과, 지난 시간대,
        /// 소요의 절반도 안 지난 통과는 여기서도 막힌다.
        ///
        /// 실패는 보내지 않는다. 판을 다시 여는 것은 순전히 클라 동작이다.
        /// </summary>
        public void ClearQuest(string questId, string grade)
        {
            Send(new Intent
            {
                type = IntentTypeValues.QuestCleared,
                questId = questId,
                grade = grade,
            });
        }

        /// <summary>QST-01 합동 — 판의 조각 하나를 채웠다. 인원 미달이면 서버가 무시한다</summary>
        public void JointStep(string questId)
        {
            Send(new Intent { type = IntentTypeValues.JointStep, questId = questId });
        }

        /// <summary>
        /// 지금 서 있는 자리.
        ///
        /// **판정에 안 쓰인다.** 서버는 검증 없이 스냅샷에 되비추기만 하고,
        /// 규칙이 보는 것은 여전히 구역뿐이다. 이게 없으면 같은 방 안에서
        /// 남이 어디 있는지 알 수가 없다 — 구역이 바뀔 때만 갱신되어
        /// 방 안에서는 아무도 안 움직이는 것으로 보인다.
        /// </summary>
        public void Position(float x, float y)
        {
            Send(new Intent { type = IntentTypeValues.Position, x = x, y = y });
        }

        public void QuickCommand(string command)
        {
            Send(new Intent { type = IntentTypeValues.QuickCommand, command = command });
        }

        /// <summary>
        /// B-3 정형 문구(quick-phrase) 발화. `phrase`는 `Hud.QuickPhrases`의 id 8종 중 하나다.
        ///
        /// 스팸 가드(같은 사람 2초 쿨다운)는 서버가 판정한다(`room.ts`) — 여기서는
        /// 그냥 보낼 뿐이고, 쿨다운에 걸리면 서버가 조용히 버려 이벤트가 안 온다.
        /// </summary>
        public void QuickPhrase(string phrase)
        {
            Send(new Intent { type = IntentTypeValues.QuickPhrase, phrase = phrase });
        }

        /* ------------------------------------------------------------ 수신 */

        private void OnMessage(string payload)
        {
            // JsonUtility는 판별자를 미리 읽는 기능이 없어 두 번 파싱한다.
            // 첫 파싱은 type만 보기 위한 것이라 비용이 작다.
            var envelope = JsonUtility.FromJson<ServerMessage>(payload);
            if (envelope == null) return;

            switch (envelope.type)
            {
                case "welcome":
                    MemberId = envelope.memberId;
                    break;

                case "snapshot":
                {
                    var snapshot = JsonUtility.FromJson<Snapshot>(payload);
                    if (snapshot == null || snapshot.seq <= _lastSeq) return;
                    _lastSeq = snapshot.seq;
                    Latest = snapshot;
                    SnapshotReceived?.Invoke(snapshot);
                    break;
                }

                case "lobby":
                    LobbyReceived?.Invoke(JsonUtility.FromJson<LobbyState>(payload));
                    break;

                case "events":
                {
                    // JsonUtility는 최상위 배열을 못 읽어 래퍼를 거친다
                    var batch = JsonUtility.FromJson<EventBatch>(payload);
                    if (batch?.items == null) return;
                    foreach (var item in batch.items) EventReceived?.Invoke(item);
                    break;
                }

                case "error":
                    // **로그로만 남기면 화면은 아무 말도 안 한다.**
                    // 토큰이 죽었을 때(서버 재시작·잠듦) 스냅샷이 한 번도 안 오고,
                    // 플레이어는 빈 맵을 보며 왜 안 되는지 알 길이 없다
                    Rejected = string.IsNullOrEmpty(envelope.message)
                        ? "서버가 연결을 거절했다"
                        : envelope.message;
                    Debug.LogError($"[GameClient] 서버 거절: {envelope.message}");
                    break;
            }
        }

        [Serializable]
        private sealed class EventBatch
        {
            public ServerEvent[] items;
        }
    }
}
