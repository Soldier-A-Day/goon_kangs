# SoldierKR.otf

**Noto Sans KR Regular**의 서브셋이다.

| | |
|---|---|
| 원본 | Noto Sans CJK KR (Google) |
| 라이선스 | SIL Open Font License 1.1 |
| 원본 크기 | 4.4MB |
| 서브셋 | 73KB |

## 왜 필요한가

Unity 기본 GUI 폰트에 **한글 글리프가 없다.** 서버가 보낸 시간대 이름·기온
라벨·퀘스트 이름이 전부 빈칸으로 나온다 — 값은 정상적으로 오는데 읽을 수가
없어서, 처음에는 데이터가 안 오는 것으로 오인하기 쉽다.

## 왜 서브셋인가

원본 4.4MB는 `ASSETS.md` §10이 UI+아이콘에 배정한 4MB를 혼자 넘긴다.
실제로 쓰는 글자만 남기면 73KB다.

## 다시 만들기

```bash
python3 tools/font/subset.py <원본.otf> unity/Assets/Fonts/SoldierKR.otf
```

글자 집합은 저장소가 정한다 — `packages/sim/data/*.json`, 카탈로그,
Unity C# 문자열 리터럴에서 한글을 모은다(현재 660자). 새 한글 라벨을
추가하면 **다시 뽑아야 한다.** 없는 글자는 빈칸으로 나오므로 눈에 띈다.

플레이어가 입력하는 이름은 예외다. 임의의 한글이 들어올 수 있고, 그건
서브셋으로 막을 수 없다 — 이름 표시가 중요해지면 상용 한글 2,350자를
통째로 넣어야 한다(약 500KB 예상).

## OFL 고지

이 폰트는 SIL Open Font License 1.1로 배포되며, 서브셋 파생물도 같은
라이선스를 따른다. 전문은 https://openfontlicense.org 를 참조한다.
