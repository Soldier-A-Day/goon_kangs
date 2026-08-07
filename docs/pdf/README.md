# 제출 문서 (PDF)

| 문서 | 내용 | 분량 |
|---|---|---|
| `게임_소개_및_설명서.pdf` | 게임 개요 · 하루의 흐름 · 보직 · 맵 · 미니게임 · 조작 · 실행 방법 | 9쪽 |
| `AI_활용_기술_문서.pdf` | AI 도구 · 프롬프트 설계 · 병렬 오케스트레이션 · 검증 체계 · 활용 내역 | 10쪽 |

## 다시 만드는 법

PDF는 같은 폴더의 `.html`에서 생성한다. HTML이 원본이고 PDF는 산출물이다 —
내용을 고칠 때는 반드시 HTML을 고치고 다시 뽑는다.

```bash
CHROME="/Applications/Google Chrome.app/Contents/MacOS/Google Chrome"

"$CHROME" --headless --disable-gpu --no-pdf-header-footer \
  --print-to-pdf="docs/pdf/게임_소개_및_설명서.pdf" \
  "file://$PWD/docs/pdf/게임_소개_및_설명서.html"

"$CHROME" --headless --disable-gpu --no-pdf-header-footer \
  --print-to-pdf="docs/pdf/AI_활용_기술_문서.pdf" \
  "file://$PWD/docs/pdf/AI_활용_기술_문서.html"
```

Chrome 헤드리스를 쓰는 이유는 이 저장소에 pandoc·weasyprint가 없고,
한글 폰트(Apple SD Gothic Neo)와 CSS `@page` 조판을 그대로 살릴 수 있기 때문이다.
`--no-pdf-header-footer`를 빼면 페이지마다 파일 경로와 날짜가 인쇄된다.

## 수치의 출처

두 문서의 모든 수치는 저장소에서 직접 측정했다. 값이 바뀌면 문서도 고쳐야 한다.

| 수치 | 측정 방법 |
|---|---|
| 커밋 317 · 브랜치 124 | `git rev-list --count HEAD` · `git branch -a` |
| 코드 62,310줄 | `find … -name '*.ts' -o -name '*.cs' -o -name '*.py' \| xargs wc -l` |
| 테스트 531건 | `npm run test --workspaces` 합계 |
| 에셋 488장 | `find unity/Assets -name '*.png' \| wc -l` |
| 워크트리 37개 | `ls .claude/worktrees` |
| 훈련장 이동 시간 | `packages/sim/data/training.json` · `basemap.py` 플러드필 실측 |
| 미니게임 14종 | `packages/sim/data/quests.json`의 `archetypeIntroDay` |

사례(구제권 死기능 · 훈련장 도달 불가 · 광원 오진단 등)는 `HANDOFF.md`와
커밋 이력에 기록된 실제 사건이다.
