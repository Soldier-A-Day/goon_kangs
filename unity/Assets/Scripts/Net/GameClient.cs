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

        /// <summary>서버가 알려준 내 분대원 id. welcome에서 받는다.</summary>
        public string MemberId { get; private set; } = "";

        public Snapshot Latest { get; private set; }

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
            _socket.Connect($"{serverUrl}?token={Uri.EscapeDataString(token)}");
        }

        public void Send(Intent intent)
        {
            _socket.Send(JsonUtility.ToJson(intent));
        }

        /* ------------------------------------------------- 자주 쓰는 의도 */

        public void Move(string zone)
        {
            Send(new Intent { type = IntentTypeValues.Move, to = zone });
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

        public void QuickCommand(string command)
        {
            Send(new Intent { type = IntentTypeValues.QuickCommand, command = command });
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
