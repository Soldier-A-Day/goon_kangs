// 페이지 쿼리 문자열을 그대로 넘긴다.
//
// Application.absoluteURL 로 읽으려 했으나 ?mode=heap 이 잡히지 않았다.
// WebGL에는 커맨드라인 인자가 없어 실행 중 파라미터를 넣을 통로가 URL뿐이므로,
// 이 경로가 불확실하면 모드 전환 자체가 성립하지 않는다. 브라우저에게 직접 묻는다.
mergeInto(LibraryManager.library, {
  M0GetQuery: function () {
    var query = document.location.search || "";
    var size = lengthBytesUTF8(query) + 1;
    var buffer = _malloc(size);
    stringToUTF8(query, buffer, size);
    return buffer;
  },
});
