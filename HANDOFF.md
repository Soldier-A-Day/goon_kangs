# 이어하기

이 세션에서 무엇을 만들었고, **무엇이 아직 확인되지 않았는지**.
새 세션에서 이 문서만 읽고 바로 붙을 수 있게 쓴다.

마지막 커밋 `6461618` · 2026-08-02


## 지금 돌아가는 것

| | 주소 |
|---|---|
| 웹 (Next.js) | https://solider-a-day.vercel.app — Vercel |
| 게임 서버 | https://sad-gameserver.onrender.com — Render 무료 |
| 저장소 | Upstash Redis(스냅샷·세션) + Supabase(전적) |

`/lobby`에서 방을 만들면 `/play/[코드]`로 가고, 거기가 **Unity WebGL**이다.
DOM 클라이언트는 걷어냈다 — 미니게임은 Unity에만 있어서 화면이 둘이면 규칙도 둘이 된다.

```
Vercel(웹 셸 + 번들)  ──HTTP/WSS──▶  Render(게임 서버, 20Hz 틱)  ──▶  Upstash · Supabase
```


## 이 세션에서 한 일 (커밋 19개, `ee1bf1e` 이후)

**일과 미니게임 — 원형 14종 + 랜덤**
`unity/Assets/Scripts/Net/Minigame/`. 일과 69건이 전부 판을 갖고, 어떤 원형인지는
`packages/sim/data/quests.json`이 소유한다. 예전처럼 클라가 일과 이름에서 키워드를 뽑지 않는다.
**완료 조건이 시간에서 통과로 바뀌었다** — 못 채우면 재시도이고, 계속 실패하면 시간대 종료로 잠긴다.

**등급 A/B/C → 복무 점수**
선택 퀘스트 `A 3 · B 2 · C 1`, 필수는 A일 때만 +1. **B가 등급 도입 전과 같은 값**이라
심사 요구치(18·70·150)를 손대지 않았다. 보상 배율·평판은 넣지 않았다.

**합동 — 판 하나를 인원이 나눠 채운다**
`docs/JOINT.md`에 설계가 통째로 있다. 요구 인원이 모이지 않으면 한 조각도 안 오른다.

**해금이 규칙이 됐다**
`unlocks`가 읽는 곳 없는 필드였다. 공통 일과 D-2 · 하달 D-3 · 돌발 D-4로 게이트를 걸었다.
`보직 퀘스트`(D-2)는 걸지 않았다 — D-1이 필수로 보직 2건을 요구해 모순된다.

**방 인테리어를 목업대로 다시 깔았다**
`mock-img/files-6/`의 구역별 배치를 규칙으로 삼는다. 소품 **105종 · 493개**.
배치를 둘레 산포에서 **목업 좌표 + 타일 간격 반복**으로 바꿔서, 방이 크면 더 들어찬다.
일과마다 **어느 물건 앞에서 하는지**(`spot`)를 데이터로 들고, 그 물건이 그 구역에
실제로 놓였는지는 맵 생성기가 굽는 자리에서 검사한다.

**멀티 위치 동기화**
좌표가 프로토콜에 없어서 구역이 바뀔 때만 순간이동했다. `position` 의도를 200ms마다
보내고 `SquadView`가 보간한다. **좌표는 표시 정보이지 규칙이 아니라서 sim에 넣지 않았다** —
게임서버가 따로 들고 스냅샷에 얹기만 한다.

**배포 · 안정화**
Unity WebGL을 `apps/web/public/game/`에 넣고 브로틀리 헤더를 `next.config.ts`가 붙인다.
세션 토큰을 Redis로 옮겼다(메모리에만 두면 서버가 잠들 때 전부 죽는다).
종료·연결거절 화면(`HudEnding`), 퇴소 후 **같은 방에서 다시 시작**.
레티나 흐림(`devicePixelRatio`)과 넓은 화면 오른쪽 공백(`HudTheme.ViewWidth`)을 고쳤다.


## 눈으로 확인한 것 / 못 한 것

확인함 — 공개 주소 접속, 방 생성·시작, Unity 로드, 스냅샷 흐름,
`BALANCE` 판 하나, 위치 동기화(스크립트로 원 그리기), 서버 재시작 후 토큰 생존,
재시작 엔드포인트(진행 중 409 / 끝난 뒤 200 같은 코드).

**확인 못 함 — 여기가 다음 할 일이다.**

1. **미니게임 13종.** `BALANCE` 말고는 한 번도 안 눌러봤다. 파라미터가 전부 추정값이라
   통과 난이도를 모른다. `Z01 생활관`에서 다섯 종(SCRUB·PLACE·SEARCH·SCRUB+PLACE·RHYTHM)을
   연달아 볼 수 있다.
2. **합동 판.** 오후 칸에 요구 인원이 같은 구역에 서 있어야 열린다.
3. **퇴소 → 재시작 버튼의 실제 화면.** 서버 쪽은 검증했지만 Unity 화면은 못 봤다.
4. **Redis·Supabase가 실제로 읽고 쓰는지.** 로그가 `스냅샷=upstash`라고 뜨는 것만 봤다.
5. **전체화면 창(수첩·심사·일과표)의 선명도.** DPR은 고쳤지만 그 화면들은 안 열어봤다.


## 알려진 문제

**`tools/simrunner/test/balance.test.ts` 5건이 빨갛다.** 완주율 0 — 봇이 보급·장비를
다루지 않아 조건 C·D로 D-5에 전멸한다. 이 세션 이전부터 있던 것이고 손대지 않았다.
고치려면 simrunner 봇에 보급 청구와 장비 착용을 넣어야 한다.

**Render 무료 플랜은 15분 놀면 잠든다.** 깨는 데 30초~1분. 그 동안 로비에서
"분대 만들기"가 먹통으로 보인다. 로비 진입 시 `/health`를 한 번 치면 그 사이 깨어난다 —
아직 안 넣었다.

**공통 일과가 전부 오전 칸에만 뜬다.** `quests.ts`의 `pickPhase(chore.required, 0)`이
인덱스를 언제나 0으로 넘긴다. 의도인지 실수인지 코드만으론 안 읽힌다.

**`optionalMissed` 군기 페널티가 건수 비례가 아니다**(`discipline.ts`). 몇 건을 빼먹든
−값 한 번이라, 공통을 전부 선택으로 바꾸면 "다 무시"가 지배 전략이 된다.

**QST-01의 협동 장치 넷 중 하나만 들어갔다.** 인원 미달 시 진행 정지만 있고
역할 게이트·공동 부하·정보 비대칭은 없다. 남은 셋은 사람마다 화면이 달라야 해서,
조각에 `role`이나 소유자를 붙이는 것이 다음 걸음이다.

**CORS가 `*`다.** 도메인이 확정되면 Render 대시보드에서 `CORS_ORIGIN`을 좁히면 된다.


## 손에 익혀야 할 명령

```bash
# 로컬 전체
npm run -w services/gameserver dev     # :8080 — .env.local을 읽어 Redis·Supabase에 붙는다
npm run dev -w apps/web                # :3000

# Unity — 씬을 고쳤거나 데이터가 바뀌면
python3 tools/sprites/generate.py                       # 맵·타일·소품 재생성
/Applications/Unity/Hub/Editor/6000.0.80f1/Unity.app/Contents/MacOS/Unity \
  -batchmode -quit -nographics -projectPath unity \
  -executeMethod SoldierADay.EditorTools.BaseScene.CreateScene -logFile /tmp/scene.log
/Applications/Unity/Hub/Editor/6000.0.80f1/Unity.app/Contents/MacOS/Unity \
  -batchmode -quit -nographics -projectPath unity \
  -executeMethod SoldierADay.EditorTools.BuildPlayer.Web -out Build/web -logFile /tmp/web.log
node tools/webgl-sync.mjs              # 빌드를 apps/web/public/game 으로 나른다

# 프로토콜을 고쳤으면
npx tsx tools/csharpgen/src/index.ts   # Protocol.cs 재생성

# 문서
python3 tools/questdoc.py              # docs/QUESTS.md 재생성

# 배포
npx vercel --prod --yes                # 웹 — 서버는 Render가 main push를 보고 자동 배포
```

**빌드를 다시 구우면 `webgl-sync`를 꼭 돌려야 한다.** 안 그러면 코드는 고쳤는데
화면은 그대로인 상태가 된다 — 이 세션에서 두 번 걸렸다.

**번들은 저장소에 있다.** 배포할 때만 커밋한다 — 개발 중 빌드를 매번 커밋하면
이력에 15MB가 계속 쌓인다.


## 이 프로젝트에서 반복해서 문제가 된 것

같은 종류의 사고가 이 세션에서만 네 번 났다. **화면은 정상으로 보이는데 원인이 안 보이는** 것들이다.

- **서버가 낡은 코드로 떠 있었다** — 스냅샷에 `minigame`이 없어 모든 일과가 판 없는 일과로 보였다
- **번들이 낡아 있었다** — 코드를 고쳐도 화면이 그대로
- **`.gitignore`의 `build/`가 `public/game/Build/`를 삼켰다** — `git add`가 조용히 아무것도 안 담았다
- **`.vercelignore`에서 같은 실수를 반복했다** — 배포된 사이트에서 로더가 404

무엇이 안 될 때 **코드를 의심하기 전에 "지금 도는 것이 내가 고친 그것인가"를 먼저 보는 편이 빠르다.**


## 참고 문서

| | |
|---|---|
| `SAD-GDD-002_2D기획서.html` | 기획서. v1.1 변경(미니게임·합동·등급·해금·인테리어)이 반영돼 있다 |
| `docs/game_spec.md` | 미니게임 원형 설계안 + 구현하며 달라진 곳 |
| `docs/JOINT.md` | 합동 판 설계 — 왜 이 방식인지가 통째로 있다 |
| `docs/QUESTS.md` | 구역별 일과 69건. **손으로 고치지 마라** — `questdoc.py`가 뽑는다 |
| `mock-img/files-6/` | 구역별 배치와 일과 수행 지점 목업 (SAD-ART-003) |
| `AGENTS.md` | Next.js가 학습 데이터와 다르다 — `node_modules/next/dist/docs/`를 먼저 읽어라 |
