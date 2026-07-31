#!/usr/bin/env bash
# 에셋 예산 검사. 카탈로그가 정한 예산과 실제 파일을 맞춰본다.
#
# Unity 배치모드는 -logFile 을 파이프로 넘기면 출력이 비어 나오므로
# 임시 파일을 거친다. **종료 코드는 Unity의 것을 그대로 넘긴다** —
# 여기서 삼키면 CI가 예산 초과를 통과로 읽는다.
set -uo pipefail

UNITY="${UNITY:-/Applications/Unity/Hub/Editor/6000.0.80f1/Unity.app/Contents/MacOS/Unity}"
MILESTONE="${MILESTONE:-M0}"
LOG="$(mktemp -t sad-assets)"

"$UNITY" -batchmode -quit -nographics \
  -projectPath unity \
  -executeMethod SoldierADay.EditorTools.AssetBudgetReport.RunForCi \
  -milestone "$MILESTONE" \
  -logFile "$LOG" > /dev/null 2>&1
status=$?

# 리포트 본문만 뽑는다. Unity 로그는 수천 줄이고 그 안에서 표를 찾는 건
# 도구가 할 일이지 사람이 할 일이 아니다.
awk '/\[에셋\] 예산 검사/,/^UnityEngine\.Debug/' "$LOG" | grep -v '^UnityEngine\.' || cat "$LOG"
rm -f "$LOG"

exit $status
