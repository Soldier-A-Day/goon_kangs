using System.Collections.Generic;
using UnityEngine;

namespace SoldierADay.Net
{
    /// <summary>
    /// 구역 하나 (SAD-ART-001 §6.4).
    ///
    /// 구역은 단순한 방 하나가 아니라 **세 가지의 단위**다.
    ///   1. 카메라 Confiner 경계 (§2.2)
    ///   2. 시야 차단 경계 (§1.3-A) — 통신병 시스템이 여기 걸려 있다
    ///   3. 퀘스트가 벌어지는 곳 (서버 zone과의 매핑)
    ///
    /// **`id`가 곧 서버 구역이다.** 예전에는 아트가 방 25개를 들고 서버는 8개만
    /// 알아서 `serverZone`이라는 매핑이 따로 있었는데, 같은 사실이 두 곳에 살면
    /// 반드시 어긋난다 — 실제로 퀘스트 17건이 어긋났다. 서버 쪽을 방 단위로
    /// 쪼갠 뒤로는 이을 것이 없다. 여기 `id`를 그대로 보고하면 된다.
    /// </summary>
    public sealed class ZoneMap : MonoBehaviour
    {
        /// <summary>`Z01` — 아트 구역 id이자 서버 구역 id</summary>
        public string id;
        public string zoneName;
        public bool indoor;
        /// <summary>`room` · `corridor` · `outdoor`</summary>
        public string kind = "room";

        /// <summary>구역 사각형 (월드 유닛). 타일 좌표가 아니다</summary>
        public Rect area;
        public Vector2 door;
        public Vector2 spawn;

        /// <summary>소품 이름(인덱스 뗀 것) → 그 이름을 가진 인스턴스들</summary>
        private readonly Dictionary<string, List<Transform>> _props =
            new Dictionary<string, List<Transform>>();
        private readonly List<Transform> _all = new List<Transform>();

        /// <summary>
        /// 소품 색인을 자식에서 다시 만든다.
        ///
        /// **씬을 저장하면 딕셔너리가 사라진다.** 씬 빌더가 채워둔 색인은 직렬화되지
        /// 않으므로, 빌드해서 실행하면 비어 있는 채로 시작한다. 그러면 `AnchorFor`가
        /// 후보를 못 찾아 퀘스트를 전부 스폰 지점에 세우고, 마커가 플레이어 머리
        /// 위에 뜬다 — 실제로 그랬다.
        ///
        /// 자식 이름이 곧 소품 이름이므로(씬 빌더가 그렇게 짓는다) 여기서 복구된다.
        /// </summary>
        private void Awake()
        {
            _props.Clear();
            _all.Clear();
            _taken.Clear();
            _anchors.Clear();

            foreach (Transform child in transform)
            {
                if (child.GetComponent<SpriteRenderer>() == null) continue;
                Register(child.name, child);
            }
        }

        /// <summary>씬 빌더가 소품을 세우며 등록한다. 런타임에는 `Awake`가 다시 채운다</summary>
        public void Register(string name, Transform instance)
        {
            if (!_props.TryGetValue(name, out var list))
            {
                list = new List<Transform>();
                _props[name] = list;
            }
            list.Add(instance);
            _all.Add(instance);
        }

        /// <summary>
        /// 일과 이름 → 그 일이 벌어지는 물건.
        ///
        /// "관물대 정돈"은 관물대 앞에서 하는 일이다. 표식을 방 한가운데에 찍으면
        /// 어느 방인지는 알아도 **무엇을 하는 일인지**가 화면에서 사라지고, 같은
        /// 방에 두 건이 있으면 표식이 겹쳐 고를 수가 없다.
        ///
        /// 미니게임 유형표(`Minigame.KindFor`)와 같은 방식이다 — 서버가 물건을
        /// 보내지 않는 이유도 같다. 어디 앞에 서든 서버가 세는 진척은 같으므로
        /// 이건 순전히 표현이고, 클라가 이름을 보고 고른다(ARCH-02).
        /// </summary>
        /// <summary>
        /// 퀘스트를 세울 자리.
        ///
        /// **그 일에 어울리는 물건 앞**이다. 물건 아래쪽(캐릭터가 서는 쪽)에
        /// 세워야 표식이 물건을 가리지 않고, 다가가면 물건과 표식이 같이 보인다.
        ///
        /// 한 번 정한 자리는 기억한다 — 스냅샷은 10Hz로 오는데 매번 다시 고르면
        /// 표식이 방 안에서 계속 옮겨 다닌다. 이미 쓴 물건은 다음 일과에 내주지
        /// 않으므로, 같은 방에 여러 건이 있어도 **자연히 흩어진다.**
        /// </summary>
        public Vector2 AnchorFor(string questId, string spot)
        {
            if (_anchors.TryGetValue(questId, out var known)) return known;

            var at = Choose(spot);
            _anchors[questId] = at;
            return at;
        }

        private readonly Dictionary<string, Vector2> _anchors =
            new Dictionary<string, Vector2>();
        private readonly HashSet<Transform> _taken = new HashSet<Transform>();

        private Vector2 Choose(string spot)
        {
            // 1. **서버가 지목한 물건.**
            //
            // 예전에는 일과 **이름**에서 키워드를 뽑아 물건을 골랐다. 표가 예순
            // 줄이었는데도 "정돈"·"정리"·"점검" 같은 말이 여러 일과에 걸쳐 있어,
            // 관물대 정돈과 복도 정돈이 같은 물건 앞에서 벌어졌다. 지금은
            // `quests.json`이 물건을 들고 있고(`spot`), 그 물건이 그 구역에
            // 실제로 놓였는지는 맵 생성기가 굽는 자리에서 검사한다.
            if (!string.IsNullOrEmpty(spot))
            {
                var at = FreeProp(spot);
                if (at.HasValue) return at.Value;
            }

            // 2. 그 물건이 이미 다른 일과에 나갔거나 없다 — 방의 아무 물건.
            //    판이 없는 일과(회복 · 합동 · 훈련)가 늘 여기로 온다
            foreach (var prop in _all)
            {
                if (prop == null || _taken.Contains(prop)) continue;
                _taken.Add(prop);
                return Front(prop);
            }

            // 3. 물건이 없거나 다 찼다 — 가운데에서 조금씩 어긋나게 놓는다
            return area.center + new Vector2((_anchors.Count % 3 - 1) * 1.6f,
                                             (_anchors.Count / 3 % 2) * -1.4f);
        }

        private Vector2? FreeProp(string name)
        {
            if (!_props.TryGetValue(name, out var list)) return null;
            foreach (var prop in list)
            {
                if (prop == null || _taken.Contains(prop)) continue;
                _taken.Add(prop);
                return Front(prop);
            }
            return null;
        }

        /// <summary>물건 앞 — 아래쪽 한 칸. 소품 피벗이 아랫변이므로(§6.2) 거기서 더 내린다</summary>
        private Vector2 Front(Transform prop)
        {
            var at = (Vector2)prop.position + new Vector2(0f, -0.9f);
            // 방 밖으로 밀려나면 물건 위쪽으로 돌린다
            if (at.y < area.yMin + 0.6f) at = (Vector2)prop.position + new Vector2(0f, 0.9f);
            return new Vector2(Mathf.Clamp(at.x, area.xMin + 0.6f, area.xMax - 0.6f),
                               Mathf.Clamp(at.y, area.yMin + 0.6f, area.yMax - 0.6f));
        }

        /// <summary>
        /// 문자열 해시.
        ///
        /// `string.GetHashCode`를 쓰지 않는 이유는 .NET에서 **실행마다 값이 달라지기**
        /// 때문이다(해시 DoS 방어). 그러면 같은 퀘스트가 매번 다른 자리에 서고,
        /// 플레이어는 어디로 가야 할지 익힐 수 없다.
        /// </summary>
        private static int StableHash(string text)
        {
            unchecked
            {
                var hash = 17;
                foreach (var c in text) hash = hash * 31 + c;
                return hash & 0x7FFFFFFF;
            }
        }
    }
}
