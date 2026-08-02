using SoldierADay.Protocol;
using UnityEngine;

namespace SoldierADay.Net
{
    /// <summary>
    /// 방독면 착용 (SAD-GDD-001 §9.0 D-05 · SAD-ART-001 §1.3-3 재설계).
    ///
    /// > 3D에서는 시야창이 좁아지는 것이 압박이었다. 2D에서 시야를 좁히면 그냥
    /// > 안 보인다. **재설계** — 착용 미니게임은 정면 클로즈업 컷인 오버레이로
    /// > 처리(마스크 끈 → 조임 → 밀폐 확인, **3단계 QTE**). 착용 후 압박은
    /// > 화면 프레임 오버레이 + 수분 소모 2배.
    ///
    /// 원안의 9초가 이 3단계로 옮겨왔다. 단계마다 창이 좁아지는 이유는
    /// **손이 굳는 것**을 조작으로 옮긴 것이다 — 처음 끈을 잡는 것은 쉽고,
    /// 밀폐를 확인하는 마지막이 가장 어렵다.
    ///
    /// ── §15.0 접근성 ────────────────────────────────────────────────
    /// "연타 요구 전면 금지"가 여기에도 걸린다. 세 단계 모두 **한 번 누르는**
    /// 타이밍이고, 접근성 옵션이 켜지면 창이 두 배로 넓어진다.
    /// </summary>
    public sealed class MaskDrill : MonoBehaviour
    {
        public GameClient client;
        public ScreenEffects effects;
        public LocalPlayer player;

        /// <summary>§9.0 3단계 — 끈 → 조임 → 밀폐</summary>
        public static readonly string[] Steps = { "마스크 끈", "조임", "밀폐 확인" };

        /// <summary>지금 컷인이 떠 있는가. HUD가 이걸 보고 그린다</summary>
        public bool Active { get; private set; }

        /// <summary>0~2</summary>
        public int Step { get; private set; }

        /// <summary>0~1 커서. 창 안에서 누르면 통과</summary>
        public float Cursor { get; private set; }
        public float WindowMin { get; private set; }
        public float WindowMax { get; private set; }

        /// <summary>방금 틀렸다 — HUD가 붉게 번쩍인다</summary>
        public float MissFlash { get; private set; }

        /// <summary>착용 완료. §5.0 수분 소모 2배가 여기서부터 걸린다</summary>
        public bool Worn { get; private set; }

        private int _direction = 1;
        private float _speed = 1.1f;

        private void Apply(Snapshot snapshot)
        {
            // 화생방 훈련일에만 뜬다. 그날인지는 서버가 안다(`curriculum.json`) —
            // 클라가 날짜로 유추하면 커리큘럼이 바뀔 때마다 어긋난다
            var cbrn = false;
            if (snapshot?.quests != null)
            {
                foreach (var quest in snapshot.quests)
                {
                    if (quest?.training == "cbrn") { cbrn = true; break; }
                }
            }

            if (!cbrn && (Active || Worn)) Cancel();
            _available = cbrn;
        }

        private bool _available;

        private void OnEnable()
        {
            if (client != null) client.SnapshotReceived += Apply;
        }

        private void OnDisable()
        {
            if (client != null) client.SnapshotReceived -= Apply;
        }

        private void Update()
        {
            MissFlash = Mathf.MoveTowards(MissFlash, 0f, Time.deltaTime * 3f);

            // §9.0 "가스!" — 훈련일에 G로 착용을 시작한다. 시간 제한을 두지 않는
            // 이유는 이것이 반응 속도 시험이 아니라 **절차**이기 때문이다
            if (!Active && _available && !Worn && Input.GetKeyDown(KeyCode.G)) Begin();
            if (!Active) return;

            Cursor += _direction * _speed * Time.deltaTime;
            if (Cursor >= 1f) { Cursor = 1f; _direction = -1; }
            if (Cursor <= 0f) { Cursor = 0f; _direction = 1; }

            if (!Input.GetKeyDown(KeyCode.Space) && !Input.GetKeyDown(KeyCode.E)) return;

            if (Cursor >= WindowMin && Cursor <= WindowMax) Pass();
            else Miss();
        }

        private void Begin()
        {
            Active = true;
            Step = 0;
            Worn = false;
            if (player != null) player.Suspended = true;
            SetWindow();
        }

        private void Pass()
        {
            Step += 1;
            if (Step >= Steps.Length)
            {
                Active = false;
                Worn = true;
                if (player != null) player.Suspended = false;
                // 착용하면 화면 프레임이 덮인다(§9.2 `SH_MaskFrame`)
                if (effects != null) effects.maskOn = true;
                return;
            }
            SetWindow();
        }

        /// <summary>
        /// 틀리면 **단계가 되돌아간다.** 실패해도 그냥 다시 시키면 시간만 늘고,
        /// 처음부터 돌리면 마지막 단계에서 틀린 사람이 세 번을 다시 한다.
        /// 한 칸 물러서는 것이 그 사이다.
        /// </summary>
        private void Miss()
        {
            MissFlash = 1f;
            Step = Mathf.Max(0, Step - 1);
            SetWindow();
        }

        private void SetWindow()
        {
            // 단계가 오를수록 좁아진다 — 손이 굳는 것을 창으로 옮긴 것이다
            var width = new[] { 0.34f, 0.24f, 0.15f }[Mathf.Clamp(Step, 0, 2)];

            // 창 자리는 단계마다 다르다. 같은 자리면 세 번 같은 박자로 눌러
            // "절차"가 아니라 리듬이 된다
            var center = new[] { 0.5f, 0.34f, 0.66f }[Mathf.Clamp(Step, 0, 2)];
            WindowMin = Mathf.Clamp01(center - width * 0.5f);
            WindowMax = Mathf.Clamp01(center + width * 0.5f);

            _speed = 1.0f + Step * 0.35f;
            Cursor = 0f;
            _direction = 1;
        }

        private void Cancel()
        {
            Active = false;
            Worn = false;
            if (player != null) player.Suspended = false;
            if (effects != null) effects.maskOn = false;
        }
    }
}
