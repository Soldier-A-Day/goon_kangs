using System.Collections.Generic;
using SoldierADay.Protocol;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace SoldierADay.Net
{
    /// <summary>
    /// 쌓인 눈 (SAD-ART-001 §6.3 "눈 처리").
    ///
    /// > `TS_Snow`를 `TM_GroundDeco` 위에 오버레이 타일맵으로 얹고, 한랭 이하
    /// > 밴드에서만 활성화한다. 제설 공통 일과 수행 시 해당 타일을 지우는 것으로
    /// > **작업 결과가 눈에 보인다.**
    ///
    /// 이게 중요한 이유는 **일과 중 유일하게 화면에 흔적이 남는 것**이기 때문이다.
    /// 관물대를 정돈해도 게이지만 차고 관물대는 그대로다. 제설만은 하고 나면
    /// 눈이 없다 — 그 하나가 "내가 뭘 했다"를 만든다.
    ///
    /// 판정은 서버에 있다. 여기서는 스냅샷이 "그 일과가 끝났다"고 말할 때
    /// 타일을 지울 뿐이고, 클라이언트가 완료를 선언하지 않는다(ARCH-02).
    /// </summary>
    public sealed class SnowCover : MonoBehaviour
    {
        public GameClient client;
        public Tilemap tilemap;

        /// <summary>구역 하나에 깔린 눈. 제설이 끝나면 통째로 지운다</summary>
        [System.Serializable]
        public struct Patch
        {
            public string zone;
            /// <summary>x, y가 번갈아 든 평평한 배열 — JSON 쪽과 같은 모양</summary>
            public int[] cells;
            public TileBase tile;
        }

        public Patch[] patches = System.Array.Empty<Patch>();

        /// <summary>이미 치운 구역. 스냅샷이 10Hz라 매번 지우면 헛일이다</summary>
        private readonly HashSet<string> _cleared = new HashSet<string>();

        /// <summary>지금 눈이 깔려 있는가 — 밴드가 정한다</summary>
        private bool _lying;

        private void OnEnable()
        {
            if (client != null) client.SnapshotReceived += Apply;
            Show(false);
        }

        private void OnDisable()
        {
            if (client != null) client.SnapshotReceived -= Apply;
        }

        private void Apply(Snapshot snapshot)
        {
            var band = snapshot?.weather?.band;
            var cold = band == SnapshotWeatherBandValues.Cold ||
                       band == SnapshotWeatherBandValues.ExtremeCold;

            if (cold != _lying)
            {
                _lying = cold;
                // 밴드가 다시 추워지면 **치운 기록도 지운다.** 밤새 또 내린 것이고,
                // 그러지 않으면 D-1에 한 번 치운 연병장이 18일 내내 깨끗하다
                if (cold) _cleared.Clear();
                Show(cold);
            }

            if (!cold || snapshot?.quests == null) return;

            foreach (var quest in snapshot.quests)
            {
                if (quest == null) continue;
                if (quest.status != SnapshotQuestsItemStatusValues.Done) continue;
                if (!IsSnowWork(quest.label)) continue;
                Clear(quest.zone);
            }
        }

        /// <summary>
        /// 제설 일과인가.
        ///
        /// 이름으로 고른다. 서버가 "이건 제설이다"를 따로 보내지 않는 이유는
        /// **그게 판정이 아니기 때문**이다 — 눈이 지워지든 말든 진척도 완료도
        /// 똑같이 흘러간다. 순전히 표현이라 클라가 정한다.
        /// </summary>
        private static bool IsSnowWork(string label) =>
            !string.IsNullOrEmpty(label) && label.Contains("제설");

        private void Clear(string zone)
        {
            if (string.IsNullOrEmpty(zone)) return;
            if (!_cleared.Add(zone)) return;

            foreach (var patch in patches)
            {
                if (patch.zone != zone) continue;
                Paint(patch, null);
            }
        }

        private void Show(bool on)
        {
            foreach (var patch in patches)
            {
                if (on && _cleared.Contains(patch.zone)) continue;
                Paint(patch, on ? patch.tile : null);
            }
        }

        private void Paint(Patch patch, TileBase tile)
        {
            if (tilemap == null || patch.cells == null) return;
            for (var i = 0; i + 1 < patch.cells.Length; i += 2)
            {
                tilemap.SetTile(new Vector3Int(patch.cells[i], patch.cells[i + 1], 0), tile);
            }
        }
    }
}
