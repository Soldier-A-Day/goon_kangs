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
        public ZoneWorld world;



        private LobbyClient _lobby;
        /// <summary>지금 붙어 있는 방. 다시 시작할 때 쓴다</summary>
        private string _code = "";

        /// <summary>
        /// 같은 방으로 다시 시작한다 — 종료 화면의 버튼이 부른다.
        ///
        /// 성공하면 서버가 새 런의 스냅샷을 뿌리고, 그 순간 종료 화면이
        /// 스스로 사라진다(`HudEnding`이 status를 보고 그린다).
        /// </summary>
        public void RestartRun()
        {
            if (_lobby == null || client == null || string.IsNullOrEmpty(_code)) return;
            if (_restarting) return;
            _restarting = true;
            RestartError = "";
            StartCoroutine(RestartRoutine());
        }

        private bool _restarting;

        /// <summary>지금 재시작 요청이 오가는 중인가 — 종료 화면이 "다시 시작 중"을 그리는 데 쓴다</summary>
        public bool Restarting => _restarting;

        /// <summary>
        /// 마지막 재시작 시도가 실패한 이유. 비어 있으면 실패한 적이 없거나 이미 성공했다.
        ///
        /// **이걸 읽는 화면이 없으면 버튼은 눌러도 아무 일도 안 일어난 것처럼 보인다.**
        /// 재시작은 서버가 409(진행 중)·404(방이 청소됨)·401(토큰 만료) 어느 것으로든
        /// 거절할 수 있는데, 예전에는 `Status`/`Detail`에만 남고 종료 화면이 그걸 그리지
        /// 않아 실패가 통째로 화면 밖으로 사라졌다.
        /// </summary>
        public string RestartError { get; private set; } = "";

        private IEnumerator RestartRoutine()
        {
            Status = "다시 시작 중";
            yield return _lobby.RestartRun(_code, client.token,
                () => { Status = "진행 중"; Detail = $"방 {_code}"; RestartError = ""; },
                (reason) => { Status = "다시 시작 실패"; Detail = reason; RestartError = reason; });
            _restarting = false;
        }

        /// <summary>HUD가 읽는 연결 상태. 그리기는 Hud가 맡는다</summary>
        public string Status { get; private set; } = "시작 대기";
        public string Detail { get; private set; } = "";
        public bool Connected { get; private set; }
        public int Snapshots { get; private set; }

        /// <summary>
        /// 지금 재접속 루프가 도는 중인가 — HUD가 "끊김·재접속 중" 배지를 그리는 데 쓴다.
        /// 그리기는 여기서 하지 않는다. 프로퍼티만 정확히 유지한다.
        /// </summary>
        public bool Reconnecting { get; private set; }

        /// <summary>지금 몇 번째 재접속 시도인가(1부터). 재접속 중이 아니면 0</summary>
        public int ReconnectAttempt { get; private set; }

        /// <summary>
        /// 재접속 백오프 일정 — 1s → 2s → 4s → 8s.
        ///
        /// 합이 15초다. 각 시도에 <see cref="ReconnectAttemptTimeoutSec"/>(3초)까지 더해도
        /// 최악의 경우 27초로, 서버 유예(room.ts DISCONNECT_GRACE_MS = 30초) 안에 4번을
        /// 전부 시도하고도 여유가 남는다.
        /// </summary>
        private static readonly float[] ReconnectDelaysSec = { 1f, 2f, 4f, 8f };

        /// <summary>한 번의 재접속 시도가 응답 없이 버틸 수 있는 최대 시간</summary>
        private const float ReconnectAttemptTimeoutSec = 3f;

        private Coroutine _reconnectRoutine;

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

                // **A-4 재접속.** GameSocket은 Closed/Failed를 선언만 하고 아무도
                // 구독하지 않았다 — 순단·Render 재배포·슬립으로 WS가 끊기면 이벤트가
                // 허공에 발사되고 화면은 조용히 멈췄다. 여기서 받아 지수 백오프로
                // 다시 붙는다.
                client.Socket.Closed += OnSocketDropped;
                client.Socket.Failed += OnSocketDropped;
            }

            // **로비가 이미 방을 잡아줬으면 그대로 붙는다.**
            //
            // 웹 로비(Next.js)가 방을 만들고 시작까지 마친 뒤 세션 토큰을 준다.
            // 그 상태에서 Unity가 또 방을 만들면 **혼자 있는 새 방**으로 들어가고,
            // 같이 하려던 사람들과 영영 못 만난다 — 화면은 정상으로 보이므로
            // 원인을 찾을 길이 없는 종류의 고장이다.
            var handoff = ReadParam(query, "token", "");
            if (!string.IsNullOrEmpty(handoff))
            {
                Status = "소켓 연결 중";
                Detail = ReadParam(query, "code", "");
                _code = Detail;
                client.token = handoff;
                client.Connect();
                yield break;
            }

            // 토큰이 없을 때만 스스로 방을 만든다 — 에디터와 단독 실행용이다
            if (!autoStart) yield break;

            LobbyClient.Ticket ticket = null;
            Status = "방 생성 중";
            yield return _lobby.CreateRoom(playerName, role, difficulty, season,
                result => ticket = result, Fail);
            if (ticket == null) yield break;

            Detail = $"방 {ticket.code} · 나 {ticket.memberId}";
            _code = ticket.code;
            Status = "런 시작 중";
            var started = false;
            yield return _lobby.StartRun(ticket.code, ticket.token, () => started = true, Fail);
            if (!started) yield break;

            Status = "소켓 연결 중";
            client.token = ticket.token;
            client.Connect();
        }

        private void Fail(string reason)
        {
            Status = "실패";
            Detail = reason;
            Debug.LogError($"[net] {reason}");
        }

        private void OnLobby(LobbyState lobby)
        {
            if (lobby == null) return;
            Debug.Log($"[net] 로비 — 좌석 {lobby.seats?.Length ?? 0} · 시작됨 {lobby.started}");
        }

        private void OnSnapshot(Snapshot snapshot)
        {
            Snapshots += 1;
            Status = "연결됨";
            Connected = true;
            squad?.Apply(snapshot);
            world?.Apply(snapshot);
        }

        /// <summary>
        /// 소켓이 닫히거나 실패했다 — GameSocket.Closed/Failed 공용 핸들러.
        ///
        /// 재접속 루프가 이미 도는 중이면 여기서 또 시작하지 않는다. 그 루프가 스스로
        /// 건 client.Socket.Closed/Failed 구독이 "이번 시도 실패"를 감지하므로, 이
        /// 핸들러(영구 구독)까지 두 번째 루프를 띄우면 소켓이 겹쳐 붙는다.
        /// </summary>
        private void OnSocketDropped(string reason)
        {
            Connected = false;

            if (Reconnecting) return;

            // 서버가 토큰 자체를 거절했다(index.ts invalidToken → error 메시지 후 close).
            // 같은 토큰으로 다시 붙어봐야 똑같이 거절된다 — HudEnding이 Rejected를 보고
            // 이미 "연결 끊김" 화면을 그린다.
            if (!string.IsNullOrEmpty(client?.Rejected)) return;

            // 세션 토큰이 아예 없으면(로비 핸드오프 이전, 방 생성 실패 등) 진짜 연결이
            // 있었던 적이 없다 — 재접속할 대상이 없다.
            if (client == null || string.IsNullOrEmpty(client.token)) return;

            _reconnectRoutine = StartCoroutine(ReconnectRoutine(reason));
        }

        /// <summary>
        /// 지수 백오프 재접속: 1s → 2s → 4s → 8s, 최대 4회.
        ///
        /// 성공은 `client.Connect()`를 타는 것으로 정의한다 — 그 안에서 `_lastSeq`가
        /// -1로 리셋되고(GameClient.Connect) 새 소켓이 열린다. 서버 쪽 `Room.reconnect()`가
        /// 곧바로 최신 스냅샷을 쏘아 주므로, 재구독은 새 연결이 여는 순간 자연히 일어난다 —
        /// 여기서 스냅샷 핸들러를 다시 걸 필요가 없다(Awake에서 한 번만 걸리고 그대로 유효).
        /// </summary>
        private IEnumerator ReconnectRoutine(string firstReason)
        {
            Reconnecting = true;
            var lastReason = firstReason;

            for (var attempt = 0; attempt < ReconnectDelaysSec.Length; attempt++)
            {
                // 대기하는 동안 다른 경로(예: 지연 도착한 error 메시지)로 Rejected가
                // 채워졌으면 더 시도해봐야 소용없다 — 그만둔다.
                if (!string.IsNullOrEmpty(client.Rejected)) break;

                ReconnectAttempt = attempt + 1;
                var delay = ReconnectDelaysSec[attempt];

                Status = "연결 끊김";
                Detail = $"{delay:0}s 후 재접속 ({ReconnectAttempt}/{ReconnectDelaysSec.Length}차 · {lastReason})";
                yield return new WaitForSeconds(delay);

                if (!string.IsNullOrEmpty(client.Rejected)) break;

                Status = "재접속 시도 중";
                Detail = $"{ReconnectAttempt}/{ReconnectDelaysSec.Length}차";

                var opened = false;
                var failed = false;
                string failReason = null;

                void OnOpened() => opened = true;
                void OnDropped(string r) { failed = true; failReason = r; }

                client.Socket.Opened += OnOpened;
                client.Socket.Closed += OnDropped;
                client.Socket.Failed += OnDropped;

                // **여기가 핵심.** GameClient.Connect()를 타야 `_lastSeq = -1` 리셋이
                // 일어난다 — 그렇지 않으면 서버가 새 연결의 seq를 0부터 다시 셀 때
                // (room.ts resume, 또는 Render 재기동) 이전 연결에서 쌓인 큰 _lastSeq에
                // 막혀 스냅샷이 전부 조용히 폐기된다.
                client.Connect();

                var waited = 0f;
                while (!opened && !failed && waited < ReconnectAttemptTimeoutSec)
                {
                    waited += Time.deltaTime;
                    yield return null;
                }

                client.Socket.Opened -= OnOpened;
                client.Socket.Closed -= OnDropped;
                client.Socket.Failed -= OnDropped;

                if (opened)
                {
                    Reconnecting = false;
                    ReconnectAttempt = 0;
                    Status = "재접속됨";
                    Detail = "스냅샷 대기 중";
                    _reconnectRoutine = null;
                    yield break;
                }

                lastReason = failed ? failReason : "응답 없음";
                if (!failed)
                {
                    // 시간 안에 열림도 닫힘도 안 왔다 — 이 시도를 포기하고 소켓을
                    // 정리한 뒤 다음 백오프로 넘어간다. 안 하면 늦게 도착하는 콜백이
                    // 다음 시도와 뒤섞인다.
                    client.Socket.Disconnect();
                }
            }

            Reconnecting = false;
            ReconnectAttempt = 0;
            _reconnectRoutine = null;

            if (string.IsNullOrEmpty(client.Rejected))
            {
                Status = "연결 끊김";
                Detail = $"재접속 실패 — {lastReason}";
            }
        }

        private void OnDestroy()
        {
            if (client?.Socket != null)
            {
                client.Socket.Closed -= OnSocketDropped;
                client.Socket.Failed -= OnSocketDropped;
            }
        }
    }
}
