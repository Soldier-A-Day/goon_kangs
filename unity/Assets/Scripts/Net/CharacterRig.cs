using UnityEngine;
using UnityEngine.Rendering;

namespace SoldierADay.Net
{
    /// <summary>
    /// 캐릭터 한 명의 그림 (SAD-ART-001 §5.2 레이어 스택).
    ///
    /// 사람 하나가 **스프라이트 8장의 합성**이다. 전부 그려두면 4보직 × 계급 4 ×
    /// 피복 조합이 수백 장이 되지만, 레이어를 나누면 각 레이어당 클립 프레임 수만큼만
    /// 있으면 된다(§5.2 제작 이점). 그래서 여기서 하는 일은 시트를 골라 끼우는 것뿐이다.
    ///
    /// 정렬은 y로 한다. 탑다운에서 "화면 아래에 있는 것이 앞"이므로 발 위치에서
    /// 매 프레임 갱신하고, 8레이어는 `SortingGroup`으로 한 덩어리가 된다(§5.1) —
    /// 안 묶으면 머리가 옆 사람 몸통 뒤로 들어간다.
    /// </summary>
    [RequireComponent(typeof(SortingGroup))]
    public sealed class CharacterRig : MonoBehaviour
    {
        /// <summary>정적 소품과 **같은 공식**이어야 한다. 씬 빌더가 같은 값을 쓴다</summary>
        public const float SortScale = 16f;

        /// <summary>§5.2 표의 순서 그대로 아래에서 위로</summary>
        private static readonly string[] Layers =
            { "body", "legs", "torso", "outer", "gear", "head", "mark", "hand" };

        private SpriteLibrary _library;
        private SortingGroup _group;
        private readonly SpriteRenderer[] _renderers = new SpriteRenderer[Layers.Length];
        private readonly SpriteLibrary.Sheet[] _sheets = new SpriteLibrary.Sheet[Layers.Length];

        private SpriteLibrary.Clip _clip;
        private Facing _facing = Facing.South;
        private float _clock;
        private bool _finished;

        /// <summary>지금 재생 중인 클립 이름. 같은 클립을 다시 걸어도 되감기지 않는다</summary>
        public string Playing => _clip?.name;

        /// <summary>
        /// 정렬 그룹과 레이어는 **`Bind`를 기다리지 않는다.**
        ///
        /// 시트가 붙는 것은 스냅샷이 온 뒤지만(보직을 그때 안다) 그때까지도
        /// `LateUpdate`는 매 프레임 돈다. 여기서 안 만들어두면 서버가 붙기 전까지
        /// 프레임마다 NullReference가 쏟아진다 — 실제로 빌드해서 겪었다.
        /// </summary>
        private void Awake()
        {
            _group = GetComponent<SortingGroup>();
            BuildLayers();
        }

        public void Bind(SpriteLibrary library)
        {
            _library = library;
            BuildLayers();
        }

        private void BuildLayers()
        {
            if (_group == null) _group = GetComponent<SortingGroup>();

            for (var i = 0; i < Layers.Length; i += 1)
            {
                if (_renderers[i] != null) continue;
                var go = new GameObject(Layers[i]);
                go.transform.SetParent(transform, false);
                var renderer = go.AddComponent<SpriteRenderer>();
                renderer.sortingOrder = i;   // 그룹 안에서의 위아래
                _renderers[i] = renderer;
            }
        }

        /// <summary>
        /// 외형을 정한다. 스냅샷이 알려주는 것(보직·계급)과 상황이 정하는 것
        /// (피복·장구·손 소지품)이 섞여 들어온다.
        /// </summary>
        public void SetLook(string role, string rank, string cloth = "field",
                            string skin = "skin0", string outer = "none",
                            string gear = null, string head = null, string hand = null)
        {
            if (_library == null) return;

            // §5.3 보직 식별은 head · gear · hand 세 레이어가 만든다.
            // 지정하지 않으면 보직 기본값으로 떨어진다 — 그래야 스냅샷만으로
            // "누가 통신병인지"가 화면에서 읽힌다
            gear ??= role switch { "medic" => "medicBag", "admin" => "none", _ => "belt" };
            head ??= role == "admin" ? "bare" : "cap";
            hand ??= role switch
            {
                "rifle" => "rifle",
                "comms" => "radio",
                "admin" => "clipboard",
                _ => "none",
            };

            Set(0, "body", skin);
            Set(1, "legs", cloth);
            Set(2, "torso", cloth);
            Set(3, "outer", outer);
            Set(4, "gear", gear);
            Set(5, "head", head);
            Set(6, "mark", role + "_" + rank);
            Set(7, "hand", hand);
        }

        private void Set(int index, string layer, string variant) =>
            _sheets[index] = _library.Find(layer, variant);

        /// <summary>
        /// 클립을 건다. 이미 그 클립이면 아무 일도 하지 않는다 —
        /// 매 프레임 `Play("walk")`를 불러도 걷기가 첫 프레임에 얼어붙지 않아야 한다.
        /// </summary>
        public void Play(string clip)
        {
            if (_clip != null && _clip.name == clip) return;
            _clip = _library != null ? _library.FindClip(clip) : null;
            _clock = 0f;
            _finished = false;
        }

        /// <summary>단발 클립이 끝났는가. 경례·격발처럼 끝나면 idle로 돌아갈 것들</summary>
        public bool Finished => _finished;

        /// <summary>
        /// 이동 방향을 알려준다. 멈추면 (0,0) — 마지막 방향을 유지한다.
        ///
        /// 방향과 클립을 함께 정하므로 부르는 쪽은 "얼마나 빨리 움직이는가"만
        /// 알려주면 된다. 걷기/뛰기 경계를 부르는 쪽마다 다르게 두면 사람마다
        /// 다른 속도에서 뛰기 시작한다.
        /// </summary>
        public void Step(Vector2 velocity, bool running = false)
        {
            var moving = velocity.sqrMagnitude > 0.04f;
            if (moving)
            {
                _facing = Mathf.Abs(velocity.x) > Mathf.Abs(velocity.y)
                    ? (velocity.x < 0f ? Facing.West : Facing.East)
                    : (velocity.y > 0f ? Facing.North : Facing.South);
            }

            // 단발 클립(경례·격발·발신)이 도는 중에는 가로채지 않는다
            if (_clip != null && !_clip.loop && !_finished) return;

            Play(moving ? (running ? "run" : "walk") : IdleClip);
        }

        /// <summary>
        /// 지금 서 있을 때 돌 클립 (§5.5 19 `shiver` · 20 `pant`).
        ///
        /// **`Step`이 이걸 거쳐야 한다.** 예전에는 `SetDistress`가 직접 클립을
        /// 걸었는데, `Step`은 매 프레임 돌면서 안 움직이면 `"idle"`을 걸었다 —
        /// 스냅샷이 10Hz라 떨림은 한 프레임 보이고 곧바로 지워졌다. 상태를
        /// 들고 있다가 idle을 고를 때 대신 내주는 편이 어긋날 자리가 없다.
        /// </summary>
        private string IdleClip => _frostbite ? "shiver" : _exhausted ? "pant" : "idle";

        private bool _frostbite;
        private bool _exhausted;

        /// <summary>상태 이상이 idle을 대체한다 (§5.5 19~20 shiver / pant)</summary>
        public void SetDistress(bool frostbite, bool exhausted)
        {
            if (_frostbite == frostbite && _exhausted == exhausted) return;
            _frostbite = frostbite;
            _exhausted = exhausted;

            // 서 있는 중이면 바로 갈아 끼운다. 걷는 중이면 멈출 때 `Step`이 집는다
            if (_clip != null && (_clip.name == "idle" || _clip.name == "shiver" ||
                                  _clip.name == "pant"))
            {
                Play(IdleClip);
            }
        }

        public void FaceTo(Facing facing) => _facing = facing;

        private void LateUpdate()
        {
            // 발이 화면 아래일수록 앞. 정적 소품과 같은 공식이다.
            // sortingOrder는 부호 있는 16비트라 ±32767을 넘기면 래핑되어
            // 지면이 위로 올라온다 — 실제로 겪었다
            if (_group == null) return;
            _group.sortingOrder = Mathf.Clamp(
                Mathf.RoundToInt(-transform.position.y * SortScale), -32000, 32000);

            if (_clip == null) return;

            var frame = 0;
            if (_clip.frames > 1)
            {
                _clock += Time.deltaTime * _clip.fps;
                if (_clip.loop)
                {
                    frame = Mathf.FloorToInt(_clock) % _clip.frames;
                }
                else
                {
                    frame = Mathf.Min(Mathf.FloorToInt(_clock), _clip.frames - 1);
                    if (frame >= _clip.frames - 1) _finished = true;
                }
            }
            else
            {
                _finished = true;
            }

            var row = _clip.Row(_facing);
            var flip = _facing == Facing.West;   // §5.1 W는 E의 X미러링

            for (var i = 0; i < _renderers.Length; i += 1)
            {
                var renderer = _renderers[i];
                if (renderer == null) continue;
                var sheet = _sheets[i];
                renderer.sprite = sheet?.At(row, frame);
                renderer.flipX = flip;
            }
        }
    }
}
