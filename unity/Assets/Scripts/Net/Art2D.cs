using System;
using UnityEngine;

namespace SoldierADay.Net
{
    /// <summary>
    /// 생성기가 내보낸 2D 에셋 색인의 형태 (`tools/sprites/`).
    ///
    /// `JsonUtility`가 읽을 수 있게 **전부 배열**이다 — Dictionary를 쓰려면
    /// JSON 라이브러리를 하나 더 들여야 하는데, 색인 순회는 씬을 세울 때 한 번뿐이라
    /// 의존성을 늘릴 이유가 없다.
    ///
    /// 이 타입들은 **에디터(씬 빌더)에서만** 파싱된다. 런타임에는 이미 구워진
    /// `SpriteLibrary`만 남는다 — WebGL에서 파일을 읽으려면 통째로 다른 경로가
    /// 필요해지고, 그건 에디터와 빌드가 다르게 도는 원인이 된다.
    /// </summary>
    [Serializable]
    public sealed class Art2D
    {
        public CharsIndex chars;
        public TilesIndex tiles;
        public VfxIndex vfx;

        /// <summary>§9.1 파티클 알갱이 색인</summary>
        [Serializable]
        public sealed class VfxIndex
        {
            public VfxSprite[] sprites;
        }

        [Serializable]
        public sealed class VfxSprite
        {
            public string name;
            /// <summary>가로로 이어붙인 프레임 수. 1이면 낱장</summary>
            public int frames;
            /// <summary>프레임 한 칸의 픽셀 크기 — 파티클 크기를 이걸로 정한다</summary>
            public int cellW;
            public int cellH;
        }

        [Serializable]
        public sealed class CharsIndex
        {
            public int cellW;
            public int cellH;
            public int pivotX;
            public int pivotY;
            /// <summary>시트의 행 이름. `walk_S` 꼴이며 인덱스가 곧 행 번호다</summary>
            public string[] rows;
            public int cols;
            public ClipDef[] clips;
            public SheetDef[] sheets;
        }

        /// <summary>§5.5 애니메이션 리스트 한 줄</summary>
        [Serializable]
        public sealed class ClipDef
        {
            public string name;
            public int frames;
            public int fps;
            /// <summary>"4" = S/N/E · "1" = 정면만 · "SIDE" = 사이드뷰 씬 전용</summary>
            public string dirs;
            public bool loop;
            public string[] rows;
        }

        /// <summary>§5.2 레이어 하나의 변형 하나</summary>
        [Serializable]
        public sealed class SheetDef
        {
            public string layer;
            public string variant;
            public string file;
        }

        [Serializable]
        public sealed class TilesIndex
        {
            public int tile;
            public FloorDef[] floors;
            public WallDef[] walls;
            public PropDef[] props;
            /// <summary>§7.1.5 월드 표식 — 색은 런타임에 입힌다</summary>
            public MarkerDef[] markers;
            /// <summary>§6.3 `TS_Snow` 오버레이 — 두께 순서대로</summary>
            public string[] snow;
        }

        [Serializable] public sealed class MarkerDef { public string name; public string file; }



        [Serializable] public sealed class FloorDef { public string kind; public string file; }
        [Serializable] public sealed class WallDef { public string kind; public string[] files; }

        [Serializable]
        public sealed class PropDef
        {
            public string name;
            public string file;
            public int w;
            public int h;
            /// <summary>밟고 지나갈 수 있는가. 배수로처럼 바닥에 깔린 것만 참이다</summary>
            public bool walkable;
        }
    }

    /// <summary>
    /// 훈련 맵 색인 (`train_maps.json` · §6.4 TR01~TR10).
    ///
    /// 탑다운 7종은 이미 부대 맵 타일에 얹혀 있다. 여기 있는 것은 **사이드뷰
    /// 코스**뿐이다 — 지면 높이와 구간 이름은 타일에 담기지 않는다.
    /// </summary>
    [Serializable]
    public sealed class TrainMaps
    {
        public LaneDef[] lanes;

        [Serializable]
        public sealed class LaneDef
        {
            public string id;
            public string name;
            public int x;
            public int y;
            public int w;
            public int h;
            public int segments;
            /// <summary>열마다의 지면 높이(타일). 왼쪽 끝부터</summary>
            public int[] ground;
            public LegDef[] legs;
        }

        [Serializable] public sealed class LegDef { public string name; }
    }

    /// <summary>
    /// 부대 본영 맵 (`base_map.json` · §6.4).
    ///
    /// 구역이 1차 구조다 — 카메라 Confiner와 시야 차단(§1.3-A)이 전부 이 사각형을
    /// 단위로 돈다. 타일은 그 안을 채우는 것이고, 그 반대가 아니다.
    /// </summary>
    [Serializable]
    public sealed class BaseMap
    {
        public int tile;
        public int width;
        public int height;
        public Layers layers;
        public ZoneDef[] zones;
        public PropPlacement[] props;
        public DoorDef[] doors;
        /// <summary>
        /// 위병소에서 훈련장까지 깔린 길. 바닥 타일(`asphalt`)로도 깔려 있지만
        /// 지도는 바닥을 그리지 않으므로 사각형 목록으로 따로 받는다
        /// </summary>
        public RoadDef[] roads;
        /// <summary>지도를 동 단위로 묶어 그리기 위한 것</summary>
        public BuildingDef[] buildings;
        /// <summary>§6.3 야외에 쌓인 눈 — 구역별로 묶여 있다</summary>
        public SnowPatch[] snow;

        [Serializable]
        public sealed class SnowPatch
        {
            public string zone;
            /// <summary>두께 0~3</summary>
            public int level;
            /// <summary>x, y가 번갈아 든 평평한 배열</summary>
            public int[] cells;
        }

        [Serializable]
        public sealed class Layers
        {
            public TileRun[] ground;
            public TileRun[] groundDeco;
            public TileRun[] wall;
        }

        /// <summary>
        /// 같은 타일을 쓰는 칸들을 한 줄로 접은 것.
        ///
        /// `cells`는 x, y가 번갈아 든 평평한 배열이다. 칸마다 객체를 하나씩 쓰면
        /// 8,800칸짜리 맵에서 JSON이 수 MB가 되고, 그건 그대로 초기 다운로드
        /// 예산(§1.2 ≤25MB)을 갉아먹는다.
        /// </summary>
        [Serializable]
        public sealed class TileRun
        {
            /// <summary>`floor:concrete` 또는 `wall:interior`</summary>
            public string tile;
            public int[] cells;
        }

        [Serializable]
        public sealed class ZoneDef
        {
            /// <summary>`Z01` — 아트 구역 id이자 **서버 구역 id**</summary>
            public string id;
            public string name;
            public int x;
            public int y;
            public int w;
            public int h;
            public bool indoor;
            /// <summary>`room` · `corridor` · `outdoor` — 카메라와 시야가 다르게 다룬다</summary>
            public string kind;
            public Cell door;
            public Cell spawn;
        }

        [Serializable] public sealed class Cell { public int x; public int y; }

        [Serializable]
        public sealed class BuildingDef
        {
            public string id;
            public string name;
            public int x;
            public int y;
            public int w;
            public int h;
        }

        [Serializable]
        public sealed class PropPlacement
        {
            public string name;
            public string zone;
            public int x;
            public int y;
            public int w;
            public int h;
            public bool walkable;
            /// <summary>이정표에만 있다. 글자는 월드 UI가 얹는다(§2.1)</summary>
            public string label;
        }

        /// <summary>
        /// 문 — 걸어서 구역을 옮기는 **유일한 통로**.
        ///
        /// 이정표 자리(`signX/signY`)를 함께 실어 보낸다. 걸어서만 이동하는
        /// 설계에서 문 옆에 무엇이 있는지 적혀 있지 않으면, 플레이어는 문을
        /// 하나씩 열어보며 부대를 외우게 된다.
        /// </summary>
        [Serializable]
        public sealed class DoorDef
        {
            public string zone;
            public string name;
            public int x;
            public int y;
            public int w;
            public int h;
            /// <summary>문이 난 방향 — 지도에 문을 그릴 때 쓴다</summary>
            public string side;
            /// <summary>야외로 나가는 문인가</summary>
            public bool exit;
            /// <summary>나가는 쪽에 보여줄 문구</summary>
            public string exitLabel;
        }

        [Serializable]
        public sealed class RoadDef
        {
            public int x;
            public int y;
            public int w;
            public int h;
        }
    }
}
