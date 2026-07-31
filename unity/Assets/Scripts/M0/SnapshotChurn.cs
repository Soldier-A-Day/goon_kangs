using System.Text;
using SoldierADay.Protocol;
using UnityEngine;

namespace SoldierADay.M0
{
    /// <summary>
    /// 10Hz 스냅샷 수신을 재현하는 할당 부하기.
    ///
    /// **왜 이게 없으면 100분 힙 측정이 성립하지 않는가.**
    /// 정적 프록시 씬은 매 프레임 할당이 거의 0이다. 그 상태로 100분을 돌리면
    /// 힙이 평평하게 나오는데, 그건 "누수가 없다"가 아니라 **"아무것도 재지 않았다"** 이다.
    /// 표 18-2가 힙을 완화 불가로 둔 건 씬 때문이 아니라 **통신 때문**이다.
    ///
    /// 실제 클라이언트는 초당 10회 스냅샷을 받아 파싱하고 HUD 문자열을 다시 만든다.
    /// 100분이면 6만 회다. 누수는 여기서 생긴다 — 파싱마다 새 DTO 그래프가 뜨고,
    /// 어딘가 하나라도 참조를 붙들면 6만 배로 쌓인다.
    ///
    /// 그래서 **서버가 실제로 내보낸 스냅샷**(`tools/m0snapshot/gen.ts`가 뽑아
    /// `Resources/m0_snapshot.json`에 박아둔 것)을 그대로 파싱한다. 손으로 만든
    /// 가짜 JSON은 필드 수·배열 길이가 달라 할당량이 실제와 어긋난다.
    ///
    /// `seq`만 매번 바꿔 넣는다 — 같은 문자열을 계속 파싱하면 런타임이 캐시할 여지가
    /// 생기고, 실제로는 매번 다른 바이트가 오기 때문이다.
    /// </summary>
    public sealed class SnapshotChurn : MonoBehaviour
    {
        [Tooltip("초당 스냅샷 수. 서버 브로드캐스트가 10Hz다")]
        public float hz = 10f;

        [Tooltip("HUD 문자열 재구성까지 재현한다. 파싱만으로는 실제 할당의 절반이다")]
        public bool rebuildHudStrings = true;

        public long ParsedCount { get; private set; }

        private string _prefix;
        private string _suffix;
        private float _nextAt;
        private int _seq;

        // HUD는 매 스냅샷 문자열을 다시 만든다. 재사용 버퍼를 두는 건 실제 구현의
        // 선택이므로, 여기서는 최악(매번 새 StringBuilder)이 아니라 상식적인 구현을
        // 재현한다 — 측정 대상은 파싱 누수이지 문자열 전략이 아니다.
        private readonly StringBuilder _hud = new StringBuilder(512);

        private void Start()
        {
            var asset = Resources.Load<TextAsset>("m0_snapshot");
            if (asset == null)
            {
                Debug.LogError(
                    "[M0] m0_snapshot.json 없음 — `npx tsx tools/m0snapshot/gen.ts` 를 먼저 돌려야 한다");
                enabled = false;
                return;
            }

            // seq 값만 갈아끼우기 위해 앞뒤로 자른다. 매번 문자열 전체를 조립하면
            // 그 비용이 파싱 비용에 섞여 무엇을 재는지 흐려진다.
            var json = asset.text;
            var key = "\"seq\":";
            var start = json.IndexOf(key, System.StringComparison.Ordinal);
            if (start < 0)
            {
                Debug.LogError("[M0] 표본에 seq 필드가 없다 — 표본을 다시 생성해야 한다");
                enabled = false;
                return;
            }

            var valueStart = start + key.Length;
            var valueEnd = valueStart;
            while (valueEnd < json.Length && json[valueEnd] != ',' && json[valueEnd] != '}') valueEnd += 1;

            _prefix = json.Substring(0, valueStart);
            _suffix = json.Substring(valueEnd);

            Debug.Log($"[M0] 스냅샷 표본 {json.Length}바이트 · {hz}Hz 파싱 시작");
        }

        /// <summary>
        /// 밀린 만큼 따라잡는다. 프레임당 한 번만 파싱하면 **fps가 상한**이 되어
        /// 60fps에서는 100Hz를 요청해도 60Hz밖에 나오지 않는다. 가속 측정(§5)의
        /// 전제는 정해진 횟수를 소화하는 것이므로, 횟수가 프레임 수에 묶이면 안 된다.
        ///
        /// 다만 한 프레임에 몰아치는 양은 막는다 — 탭이 잠깐 멈췄다 돌아오면
        /// 수천 건이 한 프레임에 터져 그 프레임이 최악 프레임으로 기록된다.
        /// 그건 누수도 성능도 아닌 따라잡기 부채다.
        /// </summary>
        private void Update()
        {
            var interval = 1f / Mathf.Max(1f, hz);
            var budget = Mathf.Max(1, Mathf.CeilToInt(hz / 30f));

            while (Time.unscaledTime >= _nextAt && budget > 0)
            {
                _nextAt += interval;
                budget -= 1;

                _seq += 1;
                var json = _prefix + _seq + _suffix;
                var snapshot = JsonUtility.FromJson<Snapshot>(json);
                ParsedCount += 1;

                if (rebuildHudStrings) RebuildHud(snapshot);
            }

            // 예산을 다 쓰고도 밀려 있으면 부채를 탕감한다. 그러지 않으면
            // 한 번 밀린 뒤 영원히 따라잡기만 하며 실제 주기를 잃는다.
            if (Time.unscaledTime > _nextAt + 1f) _nextAt = Time.unscaledTime;
        }

        /// <summary>
        /// HUD가 매 스냅샷 하는 일. 온도 밴드·남은 시간·퀘스트 목록을 다시 그린다.
        /// 파싱만 재고 이걸 빼면 실제 할당의 절반만 재게 된다.
        /// </summary>
        private void RebuildHud(Snapshot snapshot)
        {
            if (snapshot == null) return;

            _hud.Clear();
            _hud.Append(snapshot.day).Append('/').Append(snapshot.totalDays).Append("일차 ");

            if (snapshot.phase != null)
            {
                _hud.Append(snapshot.phase.label).Append(' ');
            }

            if (snapshot.weather != null)
            {
                _hud.Append(snapshot.weather.band).Append(' ');
            }

            if (snapshot.quests != null)
            {
                var done = 0;
                foreach (var quest in snapshot.quests)
                {
                    if (quest == null) continue;
                    if (quest.status == SnapshotQuestsItemStatusValues.Done) done += 1;
                    // 목록 항목마다 라벨을 만든다 — 실제 HUD와 같은 수의 문자열이 뜬다
                    _hud.Append(quest.label).Append(':').Append(quest.status).Append(' ');
                }
                _hud.Append(done).Append('/').Append(snapshot.quests.Length);
            }

            if (snapshot.members != null)
            {
                foreach (var member in snapshot.members)
                {
                    if (member == null) continue;
                    _hud.Append(member.name).Append('(').Append(member.zone).Append(PresenceMark(member)).Append(')');
                }
            }

            // 결과를 실제로 쓴다. 버리면 컴파일러나 런타임이 통째로 들어낼 수 있다.
            if (_hud.Length == 0) Debug.Log("빈 HUD");
        }

        /// <summary>
        /// 후송된 인원은 HUD에서 구분돼야 한다. 접속 끊김은 세션 레벨이라
        /// sim의 presence에 없다 — 서버 Room이 따로 들고 있다.
        /// </summary>
        private static string PresenceMark(SnapshotMembersItem member) =>
            member.presence == SnapshotMembersItemPresenceValues.Evacuated ? "후송" : "";
    }
}
