// Unity WebGL ↔ 브라우저 WebSocket 브릿지
//
// 18.0: "Unity WebGL은 네이티브 소켓 불가 → 브라우저 WS 경유".
// C#의 ClientWebSocket은 WebGL 빌드에서 동작하지 않으므로, 브라우저의 WebSocket을
// 이 플러그인으로 열고 메시지를 SendMessage로 C#에 되돌린다.
//
// 여기에는 규칙이 없다. 문자열을 나르는 것이 전부다 — 판정은 서버가 한다(ARCH-02).

// **autoAddDeps가 없으면 $SadSocketState가 빌드에서 빠진다.**
// Emscripten은 함수가 실제로 참조하는 심볼을 문자열로 추적하지 못하므로,
// 상태 객체를 쓰는 함수마다 의존을 걸어줘야 한다. 없으면 컴파일도 빌드도
// 통과하고 **실행할 때 ReferenceError로만 드러난다** — 소켓을 열려는 순간에.
var SadSocketLibrary = {
  $SadSocketState: {
    socket: null,
    // C#이 붙어 있는 GameObject 이름. SendMessage 대상이다.
    target: null,
  },

  SadSocketConnect: function (urlPtr, targetPtr) {
    var url = UTF8ToString(urlPtr);
    SadSocketState.target = UTF8ToString(targetPtr);

    try {
      SadSocketState.socket = new WebSocket(url);
    } catch (error) {
      SendMessage(SadSocketState.target, "OnSocketError", String(error));
      return;
    }

    SadSocketState.socket.onopen = function () {
      SendMessage(SadSocketState.target, "OnSocketOpen", "");
    };

    SadSocketState.socket.onmessage = function (event) {
      // 스냅샷은 10Hz로 온다. 프레임마다 파싱하지 않도록 C#에서 큐에 쌓는다.
      SendMessage(SadSocketState.target, "OnSocketMessage", event.data);
    };

    SadSocketState.socket.onerror = function () {
      SendMessage(SadSocketState.target, "OnSocketError", "websocket error");
    };

    SadSocketState.socket.onclose = function (event) {
      SendMessage(SadSocketState.target, "OnSocketClose", String(event.code));
    };
  },

  SadSocketSend: function (messagePtr) {
    if (!SadSocketState.socket) return 0;
    if (SadSocketState.socket.readyState !== 1) return 0;
    SadSocketState.socket.send(UTF8ToString(messagePtr));
    return 1;
  },

  SadSocketClose: function () {
    if (!SadSocketState.socket) return;
    SadSocketState.socket.close();
    SadSocketState.socket = null;
  },

  SadSocketReadyState: function () {
    return SadSocketState.socket ? SadSocketState.socket.readyState : 3;
  },
};

autoAddDeps(SadSocketLibrary, "$SadSocketState");
mergeInto(LibraryManager.library, SadSocketLibrary);
