using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace SoldierADay.EditorTools
{
    /// <summary>
    /// 에셋 카탈로그 읽기 (`packages/assets/data/manifest.json`).
    ///
    /// **복사하지 않고 원본을 직접 읽는다.** Unity 프로젝트 안으로 복사해두면
    /// 편하지만, 그 순간 같은 숫자가 두 곳에 살게 되고 반드시 갈라진다.
    /// 갈라진 뒤에는 어느 쪽이 맞는지 알 방법이 없다 — 예산 문서가 두 개면
    /// 예산이 없는 것과 같다.
    ///
    /// 에디터 전용이므로 프로젝트 밖 경로를 읽어도 된다. 런타임이 이 값을
    /// 필요로 하는 일은 없다 — 예산은 만들 때 지키는 것이지 실행 중에 보는 것이 아니다.
    /// </summary>
    public static class AssetManifest
    {
        // Application.dataPath 는 `<repo>/unity/Assets` 다. 저장소 루트까지 두 단계 올라간다.
        private const string RelativePath = "../../packages/assets/data/manifest.json";

        [Serializable]
        public sealed class Entry
        {
            public string id;
            public string category;
            public string label;
            public int count;
            public int lod0;
            public string milestone;
            public int lodLevels;
            public string rig;
            public string slot;
            public string zone;
            public int modules;
            public int bundleMb;
            public string curriculum;
            public int streamingSegments;
            public bool instanced;
            public string attach;
            public string role;
            public string note;

            /// <summary>이 항목 전체가 차지하는 LOD0 폴리 (개당 × 벌 수)</summary>
            public int TotalTris => lod0 * count;
        }

        [Serializable]
        public sealed class Budget
        {
            public int value;
            /// <summary>표 18-2의 **완화 불가** 항목인가</summary>
            public bool hard;
            public string note;
        }

        [Serializable]
        public sealed class Budgets
        {
            public Budget screenTris;
            public Budget drawCalls;
            public Budget textureMemoryMb;
            public Budget initialDownloadMb;
            public Budget mapBundleMb;
            public Budget heapMb;
            public Budget targetFps;
            public Budget minFps;
        }

        [Serializable]
        public sealed class LodRatios
        {
            public float lod1;
            public float lod2;
            public float lod3;
        }

        [Serializable]
        public sealed class ImportRules
        {
            public string textureCompression;
            public int maxTextureSize;
            public string meshCompression;
            public bool readWriteEnabled;
            public LodRatios lodRatios;
        }

        [Serializable]
        private sealed class Root
        {
            public Budgets budgets;
            public ImportRules importRules;
            public Entry[] assets;
        }

        private static Root _cache;

        private static Root Load()
        {
            if (_cache != null) return _cache;

            var path = Path.GetFullPath(Path.Combine(Application.dataPath, RelativePath));
            if (!File.Exists(path))
            {
                throw new FileNotFoundException(
                    $"에셋 카탈로그를 찾을 수 없다: {path}\n" +
                    "Unity 프로젝트가 저장소 안에 있어야 한다 — 카탈로그는 복사본이 아니라 원본을 읽는다.");
            }

            _cache = JsonUtility.FromJson<Root>(File.ReadAllText(path));
            if (_cache?.assets == null || _cache.assets.Length == 0)
            {
                throw new InvalidDataException($"카탈로그가 비어 있다: {path}");
            }

            return _cache;
        }

        /// <summary>카탈로그를 다시 읽는다. JSON을 고친 뒤 에디터를 껐다 켜지 않기 위한 것</summary>
        [MenuItem("SOLDIER/에셋/카탈로그 다시 읽기")]
        public static void Reload()
        {
            _cache = null;
            Debug.Log($"[에셋] 카탈로그 {Entries.Length}항목 · 동시 표시 예산 {Limits.screenTris.value:N0} tris");
        }

        public static Entry[] Entries => Load().assets;
        public static Budgets Limits => Load().budgets;
        public static ImportRules Rules => Load().importRules;

        public static Entry Find(string id) => Entries.FirstOrDefault(e => e.id == id);

        public static Entry[] InCategory(string category) =>
            Entries.Where(e => e.category == category).ToArray();

        /// <summary>19.0 로드맵 순서. 이 마일스톤까지 만들어져 있어야 하는 것들</summary>
        public static Entry[] RequiredBy(string milestone)
        {
            var order = new[] { "M0", "M1", "M2", "M3", "M4", "M5" };
            var limit = Array.IndexOf(order, milestone);
            return Entries
                .Where(e => Array.IndexOf(order, e.milestone) <= limit && limit >= 0)
                .ToArray();
        }
    }
}
