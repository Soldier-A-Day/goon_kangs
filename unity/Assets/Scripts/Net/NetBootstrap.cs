using System.Collections;
using System.Runtime.InteropServices;
using System.Text;
using SoldierADay.Protocol;
using UnityEngine;

namespace SoldierADay.Net
{
    /// <summary>
    /// Unity ↔ 서버 연결의 수직 절개.
    ///
    /// 이 컴포넌트 하나가 방 생성 → 시작 → WS 연결 → 스냅샷 반영까지 이어붙인다.
    /// **ARCH-02가 설계가 아니라 동작이 되는 지점이다** — 여기까지 오기 전에는
    /// 규칙이 sim에만 있다는 것도, 생성된 DTO가 실제 서버 메시지와 맞는다는 것도
    /// 확인된 적이 없었다.
    ///
    /// 클라이언트는 규칙을 하나도 갖지 않는다. 날짜·시간대·기온·퀘스트 상태를
    /// 전부 스냅샷에서 읽어 그대로 표시한다. 스스로 세는 값이 하나라도 생기면
    /// 그 순간부터 두 클라가 다른 것을 믿게 된다.
    /// </summary>
    [RequireComponent(typeof(LobbyClient))]
    public sealed class NetBootstrap : MonoBehaviour
    {
        [Tooltip("자동으로 방을 만들고 시작한다. 4인 중 1인만 접속해도 NPC가 채운다(ROLE-03)")]
        public bool autoStart = true;

        public string playerName = "일병 김";
        public string role = "rifle";
        // 스키마가 정한 값만 통과한다 — 다른 문자열을 넣으면 방 생성이 invalidBody 로
        // 거절되고, Unity에서는 그게 "연결 실패"로만 보여 원인이 안 보인다.
        // (createRoomRequestSchema: difficulty regular|relaxed · season cold|hot|random)
        public string difficulty = "regular";
        public string season = "cold";

        public GameClient client;
        public SquadView squad;
        public Camera followCamera;

        private LobbyClient _lobby;
        private string _status = "시작 대기";
        private string _detail = "";
        private string _overlay = "";
        private float _nextOverlay;
        private int _snapshots;

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] private static extern System.IntPtr M0GetQuery();
#endif

        /// <summary>
        /// 서버 주소를 URL 쿼리로 받는다.
        ///
        /// 빌드를 다시 하지 않고 로컬·원격을 바꿔 붙일 수 있어야 한다. WebGL에는
        /// 커맨드라인 인자가 없어 통로가 URL뿐이다 — M0 측정에서 이미 겪었다.
        /// </summary>
        private static string Query()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            var pointer = M0GetQuery();
            return pointer == System.IntPtr.Zero ? "" : Marshal.PtrToStringUTF8(pointer) ?? "";
#else
            return Application.absoluteURL ?? "";
#endif
        }

        private static string ReadParam(string query, string key, string fallback)
        {
            var at = query.IndexOf(key + "=", System.StringComparison.Ordinal);
            if (at < 0) return fallback;

            var start = at + key.Length + 1;
            var end = query.IndexOf('&', start);
            var raw = end < 0 ? query.Substring(start) : query.Substring(start, end - start);
            return string.IsNullOrEmpty(raw) ? fallback : UnityEngine.Networking.UnityWebRequest.UnEscapeURL(raw);
        }

        private IEnumerator Start()
        {
            _lobby = GetComponent<LobbyClient>();

            var query = Query();
            _lobby.baseUrl = ReadParam(query, "http", _lobby.baseUrl);
            if (client != null) client.serverUrl = ReadParam(query, "ws", client.serverUrl);
            role = ReadParam(query, "role", role);

            Debug.Log($"[net] 쿼리 \"{query}\" · HTTP {_lobby.baseUrl} · WS {client?.serverUrl}");

            if (client != null)
            {
                client.SnapshotReceived += OnSnapshot;
                client.LobbyReceived += OnLobby;
            }

            if (!autoStart) yield break;

            LobbyClient.Ticket ticket = null;
            _status = "방 생성 중";
            yield return _lobby.CreateRoom(playerName, role, difficulty, season,
                result => ticket = result, Fail);
            if (ticket == null) yield break;

            _detail = $"방 {ticket.code} · 나 {ticket.memberId}";
            _status = "런 시작 중";
            var started = false;
            yield return _lobby.Start(ticket.code, ticket.token, () => started = true, Fail);
            if (!started) yield break;

            _status = "소켓 연결 중";
            client.token = ticket.token;
            client.Connect();
        }

        private void Fail(string reason)
        {
            _status = "실패";
            _detail = reason;
            Debug.LogError($"[net] {reason}");
        }

        private void OnLobby(LobbyState lobby)
        {
            if (lobby == null) return;
            Debug.Log($"[net] 로비 — 좌석 {lobby.seats?.Length ?? 0} · 시작됨 {lobby.started}");
        }

        private void OnSnapshot(Snapshot snapshot)
        {
            _snapshots += 1;
            _status = "연결됨";
            squad?.Apply(snapshot);
        }

        private void LateUpdate()
        {
            // 카메라는 내 분대원을 따라간다. 구역이 90m씩 떨어져 있어(ZoneLayout)
            // 고정 카메라로는 이동이 일어나도 화면에서 아무 일도 안 일어난다.
            if (followCamera != null && squad != null && client != null)
            {
                var body = squad.BodyOf(client.MemberId);
                if (body != null)
                {
                    var wanted = body.position + new Vector3(0f, 14f, -22f);
                    followCamera.transform.position = Vector3.Lerp(
                        followCamera.transform.position, wanted, 1f - Mathf.Exp(-3f * Time.deltaTime));
                    followCamera.transform.rotation = Quaternion.LookRotation(
                        body.position + Vector3.up - followCamera.transform.position);
                }
            }

            if (Time.unscaledTime < _nextOverlay) return;
            _nextOverlay = Time.unscaledTime + 0.5f;
            Compose();
        }

        private void Compose()
        {
            var text = new StringBuilder();
            text.AppendLine($"[{_status}] {_detail}");
            text.AppendLine($"스냅샷 {_snapshots}건");
            text.AppendLine();

            var snapshot = client?.Latest;
            if (snapshot == null)
            {
                text.AppendLine("아직 스냅샷 없음");
                _overlay = text.ToString();
                return;
            }

            // 전부 서버가 보낸 값을 그대로 찍는다. 계산하는 것이 하나도 없다.
            text.AppendLine($"{snapshot.day}/{snapshot.totalDays}일차 · {snapshot.status}");
            if (snapshot.phase != null)
            {
                text.AppendLine($"{snapshot.phase.label} ({snapshot.phase.clock})");
            }
            if (snapshot.weather != null)
            {
                text.AppendLine($"{snapshot.weather.label} 체감 {snapshot.weather.feelsLike}도");
            }
            text.AppendLine();

            if (snapshot.members != null)
            {
                foreach (var member in snapshot.members)
                {
                    if (member == null) continue;
                    var me = member.id == client.MemberId ? "▶ " : "  ";
                    text.AppendLine($"{me}{member.role,-6} {member.name,-8} {member.zone}");
                }
            }

            if (snapshot.quests != null)
            {
                var done = 0;
                foreach (var quest in snapshot.quests)
                {
                    if (quest?.status == SnapshotQuestsItemStatusValues.Done) done += 1;
                }
                text.AppendLine();
                text.AppendLine($"퀘스트 {done}/{snapshot.quests.Length}");
            }

            _overlay = text.ToString();
        }

        private void OnGUI()
        {
            if (Event.current.type != EventType.Repaint) return;

            GUI.color = _status == "실패" ? Color.red : _status == "연결됨" ? Color.green : Color.yellow;
            GUI.Box(new Rect(10, 10, 460, 300), "");
            GUI.Label(new Rect(20, 16, 440, 290), _overlay);
            GUI.color = Color.white;
        }
    }
}
