// 웹 셸의 접근성 설정을 Unity로 넘긴다 (SAD-ART-001 §15.0).
//
// 로비(Next.js)가 `window.__sadSettings`에 객체를 놓고, `Accessibility.cs`가
// 매 프레임 이걸 읽는다. 이벤트를 쓰지 않는 이유는 Unity 인스턴스가 **나중에**
// 뜨기 때문이다 — 설정을 먼저 고르고 게임에 들어오므로, 그때 그 자리에 값이
// 있어야 한다.
mergeInto(LibraryManager.library, {
  SadReadSettings: function () {
    var settings = typeof window !== "undefined" && window.__sadSettings;
    if (!settings) return 0;

    var json = JSON.stringify(settings);
    // 문자열을 C#으로 넘기려면 Unity 힙에 직접 올려야 한다. `_free`는 마샬링이
    // 끝난 뒤 C# 쪽이 부른다 — 여기서 풀면 넘어가기 전에 사라진다
    var size = lengthBytesUTF8(json) + 1;
    var buffer = _malloc(size);
    stringToUTF8(json, buffer, size);
    return buffer;
  },

  // 런이 끝났을 때 웹 셸로 돌려보낸다.
  //
  // Unity 안에 로비를 다시 만들지 않는다 — 방 만들기·보직 고르기는 이미
  // Next.js에 있고, IMGUI로 그린 폼은 브라우저 폼보다 언제나 나쁘다.
  SadGoLobby: function () {
    if (typeof window !== "undefined") window.location.href = "/lobby";
  },
});
