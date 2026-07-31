using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;

namespace SoldierADay.Net
{
    /// <summary>
    /// 로비 진입 (HTTP).
    ///
    /// WS는 토큰이 있어야 열린다 — 서버가 `?token=` 없는 연결을 즉시 끊는다.
    /// 토큰은 방을 만들거나 들어갈 때 나오므로, 소켓보다 먼저 이쪽이 필요하다.
    ///
    /// **여기서도 규칙을 갖지 않는다.** 보직이 비었는지, 방장이 누구인지, 시작할 수
    /// 있는지는 전부 서버가 판정하고 이 클래스는 응답을 그대로 옮긴다(ARCH-02).
    /// 클라가 "이 보직은 이미 찼다"를 스스로 판단하면 두 클라가 다르게 판단하는
    /// 순간이 오고, 그때 어느 쪽이 맞는지 정할 방법이 없다.
    /// </summary>
    public sealed class LobbyClient : MonoBehaviour
    {
        [Tooltip("게임서버 HTTP 주소")]
        public string baseUrl = "http://localhost:8080";

        [Serializable]
        public sealed class Ticket
        {
            public string code;
            public string memberId;
            public string token;
        }

        [Serializable]
        private sealed class CreateRoomBody
        {
            public string name;
            public string role;
            public string difficulty;
            public string season;
        }

        [Serializable]
        private sealed class JoinBody
        {
            public string name;
            public string role;
        }

        /// <summary>방을 만들고 방장으로 들어간다. 성공하면 티켓이, 실패하면 이유가 온다</summary>
        public IEnumerator CreateRoom(
            string name, string role, string difficulty, string season,
            Action<Ticket> onDone, Action<string> onError)
        {
            var body = JsonUtility.ToJson(new CreateRoomBody
            {
                name = name, role = role, difficulty = difficulty, season = season,
            });

            yield return Post($"{baseUrl}/rooms", body, text =>
            {
                var ticket = JsonUtility.FromJson<Ticket>(text);
                if (ticket == null || string.IsNullOrEmpty(ticket.token))
                {
                    onError($"토큰이 없는 응답: {text}");
                    return;
                }
                onDone(ticket);
            }, onError);
        }

        public IEnumerator JoinRoom(
            string code, string name, string role,
            Action<Ticket> onDone, Action<string> onError)
        {
            var body = JsonUtility.ToJson(new JoinBody { name = name, role = role });
            yield return Post($"{baseUrl}/rooms/{code}/join", body, text =>
            {
                var ticket = JsonUtility.FromJson<Ticket>(text);
                if (ticket == null || string.IsNullOrEmpty(ticket.token))
                {
                    onError($"토큰이 없는 응답: {text}");
                    return;
                }
                onDone(ticket);
            }, onError);
        }

        /// <summary>
        /// 런 시작. 방장만 통과하며, 그 판정도 서버가 한다.
        ///
        /// 이름이 `Start`가 아닌 이유: MonoBehaviour의 생명주기 메시지와 겹치면
        /// Unity가 이걸 콜백으로 부르려다 **"Start() can not take parameters"**로
        /// 죽는다. 컴파일은 통과하고 실행할 때만 터진다.
        /// </summary>
        public IEnumerator StartRun(string code, string token, Action onDone, Action<string> onError)
        {
            var url = $"{baseUrl}/rooms/{code}/start?token={UnityWebRequest.EscapeURL(token)}";
            yield return Post(url, "{}", _ => onDone(), onError);
        }

        private static IEnumerator Post(
            string url, string body, Action<string> onDone, Action<string> onError)
        {
            using var request = new UnityWebRequest(url, "POST");
            request.uploadHandler = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(body));
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");

            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                // 서버가 이유를 본문에 담아 보내므로 상태 코드만 옮기면 원인을 잃는다
                var detail = request.downloadHandler?.text;
                onError(string.IsNullOrEmpty(detail)
                    ? $"{request.responseCode} {request.error}"
                    : $"{request.responseCode} {detail}");
                yield break;
            }

            onDone(request.downloadHandler.text);
        }
    }
}
