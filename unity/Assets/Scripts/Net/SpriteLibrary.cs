using System;
using System.Collections.Generic;
using UnityEngine;

namespace SoldierADay.Net
{
    /// <summary>
    /// 씬이 들고 있는 스프라이트 색인 (§5.2 Sprite Library / Sprite Resolver 대응).
    ///
    /// 캐릭터는 런타임에 태어난다 — 분대원이 몇 명인지도, 보직이 무엇인지도
    /// 스냅샷이 오기 전에는 모른다. 그래서 씬 빌더가 시트를 미리 잘라 여기 묶어두고,
    /// 태어나는 쪽(`SquadView` · `LocalPlayer`)이 이름으로 찾아 쓴다.
    ///
    /// **런타임 파일 IO는 없다.** WebGL에서 파일을 읽으려면 통째로 다른 경로가
    /// 필요해지고, 그러면 에디터와 빌드가 다르게 도는 원인이 된다.
    /// </summary>
    public sealed class SpriteLibrary : MonoBehaviour
    {
        /// <summary>
        /// 시트 하나 = 레이어의 변형 하나. 셀은 행(클립×방향) × 열(프레임)이다.
        ///
        /// `Sprite[]`를 평평하게 펴는 이유는 Unity 직렬화가 2차원 배열을
        /// 지원하지 않기 때문이다.
        /// </summary>
        [Serializable]
        public sealed class Sheet
        {
            public string layer;
            public string variant;
            public int cols;
            public Sprite[] cells;

            public Sprite At(int row, int frame)
            {
                if (cells == null || cols <= 0 || row < 0) return null;
                var index = row * cols + Mathf.Clamp(frame, 0, cols - 1);
                return index >= 0 && index < cells.Length ? cells[index] : null;
            }
        }

        /// <summary>§5.5 클립 한 줄. 재생에 필요한 것만 남긴다</summary>
        [Serializable]
        public sealed class Clip
        {
            public string name;
            public int frames;
            public int fps;
            public bool loop;
            /// <summary>방향별 행 번호. 없는 방향은 −1이며 그때는 정면으로 떨어진다</summary>
            public int rowS = -1;
            public int rowN = -1;
            public int rowE = -1;

            public int Row(Facing facing) => facing switch
            {
                Facing.North => rowN >= 0 ? rowN : rowS,
                Facing.East or Facing.West => rowE >= 0 ? rowE : rowS,
                _ => rowS,
            };
        }

        public Sheet[] sheets = Array.Empty<Sheet>();
        public Clip[] clips = Array.Empty<Clip>();

        /// <summary>퀘스트·문 표식. 월드에 띄우는 마커라 픽셀 스프라이트다</summary>
        public Sprite markerQuest;
        public Sprite markerDoor;

        private Dictionary<string, Sheet> _sheets;
        private Dictionary<string, Clip> _clips;

        private void Awake() => Index();

        private void Index()
        {
            if (_sheets != null) return;

            _sheets = new Dictionary<string, Sheet>(sheets.Length);
            foreach (var sheet in sheets)
            {
                if (sheet == null) continue;
                _sheets[Key(sheet.layer, sheet.variant)] = sheet;
            }

            _clips = new Dictionary<string, Clip>(clips.Length);
            foreach (var clip in clips)
            {
                if (clip != null) _clips[clip.name] = clip;
            }
        }

        public static string Key(string layer, string variant) =>
            string.IsNullOrEmpty(variant) ? layer : layer + "_" + variant;

        public Sheet Find(string layer, string variant)
        {
            Index();
            return _sheets.TryGetValue(Key(layer, variant), out var sheet) ? sheet : null;
        }

        public Clip FindClip(string name)
        {
            Index();
            return _clips.TryGetValue(name, out var clip) ? clip : null;
        }
    }

    /// <summary>§5.1 4방향. W는 E를 X미러링한 것이라 별도 스프라이트가 없다</summary>
    public enum Facing { South, North, East, West }
}
