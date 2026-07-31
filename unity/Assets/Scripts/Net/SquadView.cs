using System.Collections.Generic;
using SoldierADay.Protocol;
using UnityEngine;

namespace SoldierADay.Net
{
    /// <summary>
    /// 스냅샷을 화면으로 옮기는 층.
    ///
    /// **아무것도 계산하지 않는다.** 서버가 말한 구역에 세우고, 서버가 말한
    /// 이동 잔여 시간에 맞춰 걸어가게 할 뿐이다. 여기서 "이쯤이면 도착했겠지"를
    /// 스스로 정하면 클라마다 다른 위치를 믿게 되고 ARCH-02가 무너진다.
    ///
    /// 보간은 표시의 몫이다(17.0 NET-01은 10Hz 스냅샷을 규정한다 — 그대로 찍으면
    /// 초당 10번 순간이동한다). 보간이 판정에 영향을 주지 않는 이유는 명확하다:
    /// 다음 스냅샷이 오면 **서버가 말한 자리로 무조건 수렴한다.**
    /// </summary>
    public sealed class SquadView : MonoBehaviour
    {
        [Tooltip("스냅샷 사이를 잇는 속도. 높을수록 서버 값에 빨리 붙는다")]
        public float followSpeed = 4f;

        [Tooltip("분대원 프리팹 — 없으면 캡슐로 대신한다")]
        public GameObject soldierPrefab;

        public Material material;

        private readonly Dictionary<string, Transform> _bodies = new Dictionary<string, Transform>();
        private readonly Dictionary<string, Vector3> _targets = new Dictionary<string, Vector3>();

        /// <summary>마지막으로 반영한 스냅샷의 구역. 오버레이가 읽는다</summary>
        public readonly Dictionary<string, string> ZoneOf = new Dictionary<string, string>();

        public void Apply(Snapshot snapshot)
        {
            if (snapshot?.members == null) return;

            // 같은 구역에 몇 명이 있는지 세어 자리를 나눈다. 겹쳐 서면
            // 스냅샷이 제대로 오는지조차 눈으로 확인할 수 없다.
            var crowd = new Dictionary<string, int>();
            foreach (var member in snapshot.members)
            {
                if (member?.zone == null) continue;
                crowd.TryGetValue(member.zone, out var count);
                crowd[member.zone] = count + 1;
            }

            var seen = new Dictionary<string, int>();
            foreach (var member in snapshot.members)
            {
                if (member?.id == null || member.zone == null) continue;

                seen.TryGetValue(member.zone, out var index);
                seen[member.zone] = index + 1;

                ZoneOf[member.id] = member.zone;
                _targets[member.id] = ZoneLayout.SlotIn(member.zone, index, crowd[member.zone]);

                if (!_bodies.ContainsKey(member.id)) Spawn(member);
            }
        }

        private void Spawn(SnapshotMembersItem member)
        {
            GameObject body;
            if (soldierPrefab != null)
            {
                body = Instantiate(soldierPrefab, transform);
            }
            else
            {
                body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                body.transform.SetParent(transform, false);
                body.transform.localScale = new Vector3(0.5f, 0.9f, 0.5f);
                if (material != null) body.GetComponent<MeshRenderer>().sharedMaterial = material;
            }

            body.name = $"{member.role}:{member.name}";
            body.transform.position = _targets[member.id];
            _bodies[member.id] = body.transform;
        }

        private void Update()
        {
            foreach (var pair in _targets)
            {
                if (!_bodies.TryGetValue(pair.Key, out var body)) continue;

                // 지수 감쇠. 프레임 레이트가 흔들려도 같은 시간에 같은 만큼 좁혀진다 —
                // deltaTime 곱하기만 쓰면 저프레임에서 뒤처지고 그게 위치 차이로 보인다.
                body.position = Vector3.Lerp(
                    body.position, pair.Value, 1f - Mathf.Exp(-followSpeed * Time.deltaTime));
            }
        }

        public Transform BodyOf(string memberId) =>
            _bodies.TryGetValue(memberId, out var body) ? body : null;
    }
}
