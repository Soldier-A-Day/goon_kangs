using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using UnityEngine;

#if !UNITY_WEBGL || UNITY_EDITOR
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
#else
using System.Runtime.InteropServices;
#endif

namespace SoldierADay.Net
{
    /// <summary>
    /// 서버와의 유일한 통로.
    ///
    /// WebGL 빌드에서는 브라우저 WebSocket을 .jslib 브릿지로 쓰고(18.0), 에디터·데스크톱에서는
    /// ClientWebSocket을 쓴다. 두 경로를 한 인터페이스 뒤에 두는 이유는 단순하다 —
    /// 에디터에서 서버에 못 붙으면 개발 자체가 불가능하기 때문이다.
    ///
    /// 이 클래스는 규칙을 모른다. 문자열을 보내고 받는 것이 전부이며, 판정·진척·기온은
    /// 전부 서버가 정한다(ARCH-02). 여기에 "이 퀘스트는 끝났다" 같은 판단이 들어오면
    /// 서버 권위가 무너진다.
    /// </summary>
    public sealed class GameSocket : MonoBehaviour
    {
        public event Action Opened;
        public event Action<string> MessageReceived;
        public event Action<string> Failed;
        public event Action<string> Closed;

        /// <summary>
        /// 수신 메시지 큐. 스냅샷이 10Hz로 오므로 도착 즉시 처리하지 않고 쌓아둔다 —
        /// 렌더 프레임과 수신 주기를 분리해야 프레임이 튀지 않는다.
        /// </summary>
        private readonly ConcurrentQueue<string> _inbox = new ConcurrentQueue<string>();

        public bool IsOpen { get; private set; }

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")]
        private static extern void SadSocketConnect(string url, string target);

        [DllImport("__Internal")]
        private static extern int SadSocketSend(string message);

        [DllImport("__Internal")]
        private static extern void SadSocketClose();

        [DllImport("__Internal")]
        private static extern int SadSocketReadyState();

        public void Connect(string url)
        {
            // .jslib가 SendMessage로 되돌리려면 GameObject 이름이 필요하다
            SadSocketConnect(url, gameObject.name);
        }

        public bool Send(string message)
        {
            return SadSocketSend(message) == 1;
        }

        public void Disconnect()
        {
            SadSocketClose();
            IsOpen = false;
        }
#else
        private ClientWebSocket _socket;
        private CancellationTokenSource _cancellation;

        public void Connect(string url)
        {
            _ = ConnectAsync(url);
        }

        private async Task ConnectAsync(string url)
        {
            _cancellation = new CancellationTokenSource();
            _socket = new ClientWebSocket();

            try
            {
                await _socket.ConnectAsync(new Uri(url), _cancellation.Token);
            }
            catch (Exception error)
            {
                OnSocketError(error.Message);
                return;
            }

            OnSocketOpen(string.Empty);
            _ = ReceiveLoopAsync();
        }

        private async Task ReceiveLoopAsync()
        {
            var buffer = new byte[64 * 1024];
            var builder = new StringBuilder();

            while (_socket != null && _socket.State == WebSocketState.Open)
            {
                WebSocketReceiveResult result;
                try
                {
                    result = await _socket.ReceiveAsync(
                        new ArraySegment<byte>(buffer), _cancellation.Token);
                }
                catch (Exception error)
                {
                    OnSocketError(error.Message);
                    return;
                }

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    OnSocketClose("closed");
                    return;
                }

                builder.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                if (!result.EndOfMessage) continue;

                OnSocketMessage(builder.ToString());
                builder.Clear();
            }
        }

        public bool Send(string message)
        {
            if (_socket == null || _socket.State != WebSocketState.Open) return false;
            var bytes = Encoding.UTF8.GetBytes(message);
            _ = _socket.SendAsync(
                new ArraySegment<byte>(bytes),
                WebSocketMessageType.Text,
                true,
                _cancellation.Token);
            return true;
        }

        public void Disconnect()
        {
            IsOpen = false;
            _cancellation?.Cancel();
            _socket?.Dispose();
            _socket = null;
        }
#endif

        /* --------------------------- .jslib 콜백 (이름을 바꾸면 브릿지가 깨진다) */

        public void OnSocketOpen(string _)
        {
            IsOpen = true;
            Opened?.Invoke();
        }

        public void OnSocketMessage(string payload)
        {
            _inbox.Enqueue(payload);
        }

        public void OnSocketError(string reason)
        {
            Failed?.Invoke(reason);
        }

        public void OnSocketClose(string code)
        {
            IsOpen = false;
            Closed?.Invoke(code);
        }

        /// <summary>
        /// 쌓인 메시지를 프레임마다 한 번에 흘린다.
        /// 한 프레임에 몇 개가 오든 처리 순서는 도착 순서 그대로다.
        /// </summary>
        private void Update()
        {
            while (_inbox.TryDequeue(out var payload))
            {
                MessageReceived?.Invoke(payload);
            }
        }

        private void OnDestroy()
        {
            Disconnect();
        }
    }
}
