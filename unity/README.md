# SOLDIER : A DAY — Unity 클라이언트

기획서 ARCH-01/02/03에 따라 **3D 세션만** 담당한다. 규칙은 여기에 없다.

## 이 프로젝트가 갖지 않는 것

- **판정 로직** — 점호, 온도 밴드, 퀘스트 완료 판단은 전부 서버(`packages/sim`)에 있다
- **밸런스 수치** — 커리큘럼·군기·복무 점수는 서버의 데이터 테이블에 있다
- **난수** — 기온 롤과 돌발은 서버가 시드로 결정한다

`Assets/Scripts/Generated/Protocol.cs`는 `packages/protocol`의 zod 정의에서 **생성**된다.
손으로 고치지 마라. 다시 만들려면 리포 루트에서:

```
npm run codegen:csharp
```

## 처음 열 때

1. Unity Hub에서 **6000.0.32f1** (Unity 6) 설치 — 모듈에 **WebGL Build Support** 포함
2. 이 `unity/` 폴더를 프로젝트로 연다
3. 에디터가 `.meta` 파일과 `ProjectSettings` 나머지를 생성한다 (첫 실행에서 시간이 걸린다)
4. Edit → Project Settings → Player → WebGL
   - Color Space: **Linear**
   - Compression Format: **Brotli**
   - Managed Stripping Level: **High**

## 서버에 붙기

`GameSocket` + `GameClient`를 같은 GameObject에 붙이고 토큰을 넣는다.
토큰은 웹 로비(`apps/web`)가 발급한다 — Unity가 직접 방을 만들지 않는다(ARCH-02 핸드오프).

```
# 게임서버
npm run dev:server

# 로비에서 방을 만들고 토큰 확인
npm run dev
```

WebGL 빌드에서는 브라우저 WebSocket을 `Assets/Plugins/WebGL/GameSocket.jslib` 브릿지로 쓴다.
에디터에서는 `ClientWebSocket`으로 붙는다 — 에디터에서 서버에 못 붙으면 개발이 불가능하다.

## M0에서 재야 하는 것

기획서 19.0이 말하듯 M0의 Unity 트랙은 **게임을 만드는 것이 아니라 숫자를 재는 것**이다.

| 항목 | 목표 | 완화 |
|---|---|---|
| 4인 야외 프레임 | 60fps / 최저 30 | **불가** |
| 100분 세션 힙 | 누수 0 | **불가** |
| 초기 다운로드 | ≤ 120MB | 가능 |
| 콜드 스타트 | ≤ 90초 | 가능 |

미달 시 대응은 배포 타깃 변경이 아니라 스코프 축소이며, 순서가 미리 정해져 있다:
파티클 → 맵 크기 → 배경 NPC → 인원(최후 수단).
