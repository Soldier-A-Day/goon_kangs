"""
`docs/QUESTS.md`를 데이터에서 다시 뽑는다.

    python3 tools/questdoc.py

문서를 손으로 쓰면 데이터와 갈라진다. 갈라진 문서는 없는 문서보다 나쁘다 —
읽는 사람이 그걸 믿기 때문이다.
"""
import json
import os

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

HEAD = """# 구역별 일과 목록

`packages/sim/data/quests.json`에서 생성한 것이다. **손으로 고치지 마라** —
데이터를 고치고 다시 뽑는다:

```
python3 tools/questdoc.py
```

## 방 하나가 곧 구역이다

예전에는 서버가 구역을 8개만 알았다. 도면(SAD-ART-002)의 부대는 방이 25개라
`barracks` 하나에 생활관·복도·세면장·행정반·통신실·세탁실·사이버지식방·
체력단련장이 전부 걸렸고, 그래서 퀘스트마다 `room`을 따로 붙여 뒀다.

같은 사실이 두 곳에 살면 반드시 어긋난다 — 실제로 17건이 어긋나 있었고,
그걸 막으려고 테스트를 하나 세워 둬야 했다. 지금은 서버 구역 자체가 방 단위라
붙일 것이 없다. `packages/sim/data/zones.json`이 구역을 소유하고,
맵 생성기(`tools/sprites/basemap.py`)가 그 표대로 그려졌는지 매번 검사한다.

그래서 이 문서의 "구역"은 곧 걸어 들어가야 하는 그 방이다. 세면장 청소는
세면장에 서 있어야 진척이 쌓인다.

**수행 지점**은 그 방 안에서 어느 물건 앞인가다(files-6 목업). 예전에는 클라가
일과 이름에서 키워드를 뽑아 방 안의 물건을 골랐고, 그래서 관물대 정돈과 복도
정돈이 같은 자리에서 벌어졌다. 지금은 데이터가 지목하고, 그 물건이 실제로
놓였는지는 맵 생성기가 굽는 자리에서 검사한다.
"""


# 원형 이름표. 코드만 쓰면 표가 암호가 되고, 이름만 쓰면 데이터와 대조가 안 된다
BOARDS = {
    "SCRUB": "문지르기", "PLACE": "정돈·배치", "AUDIT": "대조 점검",
    "MASH": "연타 작업", "BALANCE": "균형 운반", "HOLD": "계기 유지",
    "TRACE": "경로 잇기", "SORT": "분류", "TIMING": "타이밍",
    "SEQ": "순서 입력", "RHYTHM": "박자", "TRACK": "조준 유지",
    "SEARCH": "탐색", "REACT": "즉시 반응", "RANDOM": "무작위 소환",
}


def board(quest) -> str:
    """이 일과를 무엇으로 하는가. 2페이즈·인터럽트가 붙으면 같이 적는다."""
    game = quest.get("minigame")
    if not game:
        # 판이 없으면 붙잡고 있는 시간만으로 완료된다
        return "—"

    text = f'`{game["type"]}` {BOARDS.get(game["type"], "")}'
    if game.get("phase2"):
        text += f' + `{game["phase2"]}`'
    if game.get("interrupt"):
        text += f' ⚡`{game["interrupt"]}`'
    return text


def level(quest) -> str:
    game = quest.get("minigame")
    if not game:
        return "—"
    n = game.get("difficulty", 1)
    return "●" * n + "○" * (3 - n)


def main() -> int:
    quests = json.load(open(os.path.join(ROOT, "packages/sim/data/quests.json"), encoding="utf-8"))
    table = json.load(open(os.path.join(ROOT, "packages/sim/data/zones.json"), encoding="utf-8"))
    zones = {z["id"]: z for z in table["zones"]}
    order = {z["id"]: i for i, z in enumerate(table["zones"])}

    rows = []
    for role, lst in quests["role"].items():
        rows += [("보직 " + role, q) for q in lst]
    rows += [("공통 일과", q) for q in quests["chores"]]
    rows += [("돌발", q) for q in quests["surprise"]]

    by_zone = {}
    for kind, q in rows:
        by_zone.setdefault(q["zone"], []).append((kind, q))

    out = [HEAD]
    total = 0
    for zid in sorted(by_zone, key=lambda z: order.get(z, 999)):
        items = by_zone[zid]
        total += len(items)
        info = zones.get(zid, {})
        where = info.get("buildingName") or ("야외" if info else "?")
        out += [f"\n## {zid} · {info.get('name', '?')}  ({where})\n",
                "| 종류 | 일과 | 소요 | 수행 지점 | 원형 | 난이도 |",
                "|---|---|---|---|---|---|"]
        for kind, q in sorted(items, key=lambda kv: kv[1]["id"]):
            out.append(f'| {kind} | {q["label"]} | {q["workSeconds"]}초 | '
                       f'{q.get("spot") or "—"} | {board(q)} | {level(q)} |')

    empty = [z for z in zones if z not in by_zone]
    if empty:
        out.append("\n## 일과가 없는 구역\n")
        out.append("지나가는 곳이거나 들어갈 수 없는 곳이다.\n")
        for zid in sorted(empty, key=lambda z: order[z]):
            out.append(f"- `{zid}` {zones[zid]['name']}")

    tally = {}
    for _, q in rows:
        name = (q.get("minigame") or {}).get("type", "—")
        tally[name] = tally.get(name, 0) + 1

    out.append("\n## 원형별 건수\n")
    out.append("퀘스트마다 새 게임을 만들지 않는다 — **원형을 파라미터로 변주한다.**")
    out.append("같은 `SCRUB`이라도 대상과 브러시 크기·오염 패턴이 다르면 다른 일로 읽힌다.\n")
    out.append("| 원형 | 건수 |")
    out.append("|---|---|")
    for name, n in sorted(tally.items(), key=lambda kv: (-kv[1], kv[0])):
        out.append(f"| `{name}` | {n} |")

    out.append(f"\n---\n\n합계 **{total}건** · 구역 {len(by_zone)}곳 / 전체 {len(zones)}곳\n")
    open(os.path.join(ROOT, "docs/QUESTS.md"), "w", encoding="utf-8").write("\n".join(out))
    print(f"docs/QUESTS.md — {total}건 · 구역 {len(by_zone)}/{len(zones)}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
