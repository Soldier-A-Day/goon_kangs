"""
한글 폰트 서브셋.

Unity WebGL 기본 폰트에는 한글 글리프가 없다 — 시간대 이름·퀘스트 라벨이
전부 빈칸으로 나온다. 값은 정상적으로 오는데 읽을 수가 없다.

Noto Sans KR 전체는 4.6MB로, ASSETS.md §10이 UI+아이콘에 배정한 4MB를
혼자 넘긴다. 그래서 **실제로 쓰는 글자만** 남긴다.

글자 집합은 저장소가 정한다 — 표시되는 한글은 전부 sim 데이터 파일과
클라이언트 코드의 문자열 리터럴에서 나온다. 플레이어가 입력하는 이름만
예외이며, 없는 글자는 빈칸으로 나오므로 눈에 띈다.
"""
import re
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
HANGUL = re.compile(r"[가-힣ㄱ-ㆎ]")

SOURCES = [
    *(ROOT / "packages/sim/data").glob("*.json"),
    ROOT / "packages/assets/data/manifest.json",
    *(ROOT / "unity/Assets/Scripts").rglob("*.cs"),
    *(ROOT / "unity/Assets/Editor").rglob("*.cs"),
]


def collect() -> set[str]:
    found: set[str] = set()
    for path in SOURCES:
        try:
            found.update(HANGUL.findall(path.read_text(encoding="utf-8")))
        except (UnicodeDecodeError, OSError):
            continue
    return found


def main() -> int:
    source = Path(sys.argv[1])
    target = Path(sys.argv[2])

    chars = collect()
    # ASCII 는 통째로 넣는다. 숫자·영문은 어디서 올지 예측할 수 없고 값도 싸다.
    ascii_range = "".join(chr(c) for c in range(0x20, 0x7F))
    text = "".join(sorted(chars)) + ascii_range + "·—…▶△▽○●×"

    target.parent.mkdir(parents=True, exist_ok=True)
    subprocess.run(
        [
            sys.executable, "-m", "fontTools.subset", str(source),
            f"--text={text}",
            f"--output-file={target}",
            "--layout-features=",
            "--no-hinting",
            "--desubroutinize",
        ],
        check=True,
    )

    before = source.stat().st_size / (1024 * 1024)
    after = target.stat().st_size / 1024
    print(f"한글 {len(chars)}자 + ASCII → {before:.1f}MB에서 {after:.0f}KB")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
