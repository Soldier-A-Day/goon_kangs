using System.Collections.Generic;
using UnityEngine;

namespace SoldierADay.Net
{
    /// <summary>
    /// 구역 맵 하나. 모듈을 이름으로 찾아 쓸 수 있게 색인해둔다.
    ///
    /// 블록아웃 OBJ가 배치마다 그룹을 찍으므로(`관물대_3`) 임포트하면 자식
    /// GameObject 하나하나가 모듈 인스턴스가 된다. 그 이름이 곧 "무엇인지"라서,
    /// **퀘스트를 실제 물건 옆에 세울 수 있다.**
    ///
    /// 앵커를 씬에 미리 박아둘 수는 없다. 퀘스트는 서버가 그날그날 만들고
    /// (14.0 커리큘럼) 무엇이 나올지 빌드 시점에 모른다. 그래서 런타임에 붙인다.
    /// </summary>
    public sealed class ZoneMap : MonoBehaviour
    {
        public string zone;

        /// <summary>모듈 이름(인덱스 뗀 것) → 그 이름을 가진 인스턴스들</summary>
        private readonly Dictionary<string, List<Transform>> _modules =
            new Dictionary<string, List<Transform>>();

        private readonly List<Transform> _all = new List<Transform>();

        public Bounds Area { get; private set; }
        public Vector3 Spawn { get; private set; }

        /// <summary>맵 밖으로 나가는 문. 없으면 남쪽 가장자리를 쓴다</summary>
        public Vector3 Door { get; private set; }

        private static readonly string[] DoorNames = { "출입문", "셔터문", "기밀문", "위병소" };

        private void Awake()
        {
            var bounds = new Bounds(transform.position, Vector3.zero);
            var first = true;

            foreach (Transform child in transform)
            {
                var name = child.name;
                var cut = name.LastIndexOf('_');
                var key = cut > 0 ? name.Substring(0, cut) : name;

                if (!_modules.TryGetValue(key, out var list))
                {
                    list = new List<Transform>();
                    _modules[key] = list;
                }
                list.Add(child);
                _all.Add(child);

                var renderer = child.GetComponent<Renderer>();
                if (renderer == null) continue;
                if (first) { bounds = renderer.bounds; first = false; }
                else bounds.Encapsulate(renderer.bounds);
            }

            Area = bounds;
            Door = FindDoor(bounds);

            // 문에서 떨어진 곳에 선다. 문 위에 스폰하면 시작하자마자 이동 패널이
            // 화면을 덮고, 방금 들어온 구역을 보기도 전에 다시 나가라고 묻는 꼴이 된다.
            var center = new Vector3(bounds.center.x, 0f, bounds.center.z);
            var away = center - Door;
            away.y = 0f;
            var wanted = away.sqrMagnitude < 1f
                ? center
                : Door + away.normalized * Mathf.Min(away.magnitude + 8f, Mathf.Max(bounds.size.x, bounds.size.z) * 0.4f);

            // 바운드 안으로 당긴다. 모듈 하나가 벽 밖으로 뻗어 있으면 바운드가
            // 부풀고 스폰이 건물 밖에 선다 — 실제로 생활관 걸레받이가 그랬다.
            // 키트를 고쳤지만, 다음에 또 그러면 여기서 막힌다.
            Spawn = new Vector3(
                Mathf.Clamp(wanted.x, bounds.min.x + 2f, bounds.max.x - 2f),
                0f,
                Mathf.Clamp(wanted.z, bounds.min.z + 2f, bounds.max.z - 2f));
        }

        private Vector3 FindDoor(Bounds bounds)
        {
            foreach (var name in DoorNames)
            {
                if (!_modules.TryGetValue(name, out var list) || list.Count == 0) continue;

                // **transform.position 이 아니라 렌더러 바운드를 쓴다.**
                // OBJ 생성기가 배치를 정점에 구워 넣으므로 그룹 오브젝트는 전부
                // 맵 원점에 있다 — position 으로 읽으면 문이 맵 한가운데가 되고,
                // 스폰하자마자 이동 패널이 뜬다. 실제로 그렇게 됐다.
                var renderer = list[0].GetComponent<Renderer>();
                if (renderer == null) continue;

                var at = renderer.bounds.center;
                return new Vector3(at.x, 0f, at.z);
            }

            // 문 모듈이 없는 야외 구역(연병장·훈련장)은 남쪽 가장자리를 출입구로 삼는다.
            // 안쪽으로 조금 당겨야 담장 밖에 서지 않는다.
            return new Vector3(bounds.center.x, 0f, bounds.min.z + 3f);
        }

        /// <summary>
        /// 퀘스트를 세울 자리.
        ///
        /// 라벨에서 물건 이름이 읽히면 그 물건 옆에 세운다 — "관물대 정돈"은
        /// 관물대 앞에서 해야 말이 된다. 못 읽으면 그 구역의 아무 모듈에
        /// **결정적으로** 배정한다. 매번 다른 자리에 서면 익힐 수가 없다.
        /// </summary>
        public Vector3 AnchorFor(string questId, string label)
        {
            var keyword = KeywordFor(label);
            List<Transform> candidates = null;

            if (keyword != null)
            {
                foreach (var pair in _modules)
                {
                    if (!pair.Key.Contains(keyword)) continue;
                    candidates = pair.Value;
                    break;
                }
            }

            candidates ??= _all;
            if (candidates.Count == 0) return Spawn;

            var hash = StableHash(questId);
            var target = candidates[hash % candidates.Count];

            // 물건 위가 아니라 **앞에** 선다. 안 그러면 캐릭터가 관물대 속에 박힌다.
            var renderer = target.GetComponent<Renderer>();
            var center = renderer != null ? renderer.bounds.center : target.position;
            var away = center - new Vector3(Area.center.x, center.y, Area.center.z);
            if (away.sqrMagnitude < 0.01f) away = Vector3.back;

            return new Vector3(center.x, 0f, center.z) - away.normalized * 1.6f;
        }

        /// <summary>
        /// 퀘스트 라벨 → 모듈 이름 조각.
        ///
        /// 전부 적지 않는다. 일과가 81종인데(6.0) 표를 다 채우면 퀘스트가 하나
        /// 늘 때마다 여기도 고쳐야 하고, 빠뜨리면 조용히 엉뚱한 자리에 선다.
        /// 자주 나오고 **어디서 하는지가 분명한 것**만 적고 나머지는 결정적 배정에 맡긴다.
        /// </summary>
        private static string KeywordFor(string label)
        {
            if (string.IsNullOrEmpty(label)) return null;

            foreach (var (word, module) in Table)
            {
                if (label.Contains(word)) return module;
            }
            return null;
        }

        private static readonly (string word, string module)[] Table =
        {
            ("총기", "관물대"), ("관물대", "관물대"),
            ("모포", "매트리스"), ("취침", "매트리스"), ("침상", "매트리스"),
            ("게시", "게시판"), ("일과표", "게시판"), ("보고", "게시판"),
            ("청소", "청소도구함"), ("정돈", "관물대"),
            ("난방", "보일러 본체"), ("보일러", "보일러 본체"), ("동파", "배관"),
            ("발전기", "유류탱크"), ("급유", "유류탱크"),
            ("배식", "배식대"), ("식기", "세척대"), ("세척", "세척대"),
            ("잔반", "식기 반납대"), ("식수", "급수대"),
            ("탄약", "물자 상자"), ("물자", "물자 상자"), ("보급", "물자 상자"),
            ("재물", "선반 단"), ("공구", "공구대"),
            ("경계", "초소 벽"), ("초소", "초소 벽"), ("교대", "초소 벽"),
            ("의약품", "약품장"), ("환자", "처치대"), ("위생", "처치대"),
            ("게양", "국기 게양대"), ("태극기", "국기 게양대"),
            ("제식", "사열대"), ("점호", "사열대"), ("집합", "사열대"), ("체조", "사열대"),
            ("안테나", "조명탑"), ("교신", "조명탑"),
            ("제초", "화단 흙"), ("제설", "연석"), ("배수", "배수로"),
        };

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
