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

        // ────────────────────────────────────────────────── WC — 캐릭터도 조명 받게

        /// <summary>
        /// 월드 정적 스프라이트와 **같은 셰이더 이름**(`BaseScene.WorldLitShaderName`).
        /// 상수를 공유하지 않는 이유: 저쪽은 에디터 전용(`BaseScene.cs`)이라 이
        /// 런타임 어셈블리(WebGL 빌드 포함)에서 참조할 수 없다 — 문자열이
        /// 갈라지면 사고이므로, 바꿀 일이 생기면 두 파일 다 고쳐야 한다.
        /// </summary>
        private const string LitShaderName = "Universal Render Pipeline/2D/Sprite-Lit-Default";

        /// <summary>
        /// 캐릭터 전체가 공유하는 런타임 Lit 머티리얼. `BaseScene.EnsureWorldLitMaterial`이
        /// 씬 빌드 시각에 이 셰이더를 Always Included Shaders에 등록해 두므로(에디터
        /// 전용 코드, 여기서는 안 건드린다) WebGL 빌드에서도 `Shader.Find`가 성공한다.
        ///
        /// 노멀맵은 아직 없다(아트 워커 이번 배치 범위 밖) — 그래도 Lit 셰이더는
        /// 법선 없이도 `Light2D`의 색·세기를 곱해 받으므로, 어두운 방에서 캐릭터가
        /// 소품·타일맵과 함께 어두워진다. 이게 이번 작업의 목표다.
        ///
        /// 셰이더를 못 찾으면(패키지 문제 등) `null`을 반환해 렌더러가 기본
        /// 머티리얼(unlit)에 남는다 — 지금 화면이 이 작업이 깨질 때의 유일하게
        /// 안전한 실패 방식이다(`BaseScene.ApplyWorldLighting`과 같은 원칙).
        /// </summary>
        private static Material _litMaterial;
        private static bool _litMaterialSearched;

        private static Material LitMaterial
        {
            get
            {
                if (_litMaterialSearched) return _litMaterial;
                _litMaterialSearched = true;
                var shader = Shader.Find(LitShaderName);
                if (shader == null) return null;
                _litMaterial = new Material(shader) { name = "SAD_CharacterLit_Runtime" };
                return _litMaterial;
            }
        }

        /// <summary>
        /// 8레이어 + 눈 오버레이가 매달리는 자식. **루트(`transform`)가 아니다.**
        ///
        /// `LocalPlayer`는 이 컴포넌트와 같은 오브젝트에 `Rigidbody2D`(그리고 있다면
        /// Collider2D)가 붙는다. 완료 팝(D-3 #4)이 루트를 스케일하면 콜라이더까지
        /// 순간적으로 15% 커져 벽에 끼이는 물리 사고가 난다 — 그림만 스케일하려고
        /// 한 겹 더 둔다.
        /// </summary>
        private Transform _visual;

        private readonly SpriteRenderer[] _renderers = new SpriteRenderer[Layers.Length];
        private readonly SpriteLibrary.Sheet[] _sheets = new SpriteLibrary.Sheet[Layers.Length];

        private SpriteLibrary.Clip _clip;
        private Facing _facing = Facing.South;
        private float _clock;
        private bool _finished;

        /// <summary>지금 재생 중인 클립 이름. 같은 클립을 다시 걸어도 되감기지 않는다</summary>
        public string Playing => _clip?.name;

        // ────────────────────────────────────────────────── D-3 #2·#7 재생 속도

        /// <summary>idle 프레임당 166ms(fps 6)는 초조하다 — 표준 400~500ms대로 절반 늦춘다.
        /// `chars.py`는 건드리지 않는다(발주 금지) — 여기서 재생 배속만 낮춘다</summary>
        private const float IdleFpsScale = 0.5f;

        /// <summary>D-3 #7 멤버별 미세 변주 배율(0.85~1.15). idle·걷기에만 곱한다</summary>
        private float _paceScale = 1f;

        /// <summary>
        /// D-3 #7 — 멤버별 미세 변주. idle·걷기 재생 속도만 0.85~1.15배로 흔든다
        /// ("이 사람은 좀 빠르다"). 발주 문구가 "idle·걷기"로 한정했으므로 run·작업
        /// 클립에는 곱하지 않는다.
        /// </summary>
        public void SetPaceSeed(int seed)
        {
            var t = (seed % 1000) / 999f;
            _paceScale = Mathf.Lerp(0.85f, 1.15f, t);
        }

        /// <summary>
        /// D-3 #1 — 스폰 위상 어긋내기. **`Play`로 클립을 건 직후에 불러야 한다**
        /// (`Play`가 시계를 0으로 되돌리므로, 먼저 부르면 값이 지워진다).
        ///
        /// idle의 프레임 수로 나눈 나머지를 그대로 시작 위상으로 쓴다 — 4인의 애니메이션
        /// 시계가 같은 박자로 맞물리는 것("로봇 군단", BENCHMARK.md §3)을 막는 게 목적이라
        /// 위상의 정확한 값보다 "서로 다르다"가 중요하다.
        /// </summary>
        public void OffsetPhase(int seed)
        {
            if (_clip == null || _clip.frames <= 1) return;
            _clock = seed % _clip.frames;
        }

        /// <summary>
        /// .NET `string.GetHashCode`는 실행마다 값이 달라진다(해시 DoS 방어) — 같은
        /// 이유로 `Board.cs`·`ZoneMap.cs`가 이미 자체 구현을 하나씩 들고 있다. 여기서도
        /// 같은 알고리즘을 쓴다(공유 유틸을 새로 만드는 대신 — 세 번째 반복이라 별도
        /// 파일을 낼 법도 하지만, 이번 발주는 이 4개 파일 밖을 건드리지 않는다).
        /// </summary>
        public static int StableHash(string text)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            unchecked
            {
                var hash = 17;
                foreach (var c in text) hash = hash * 31 + c;
                return hash & 0x7FFFFFFF;
            }
        }

        // ────────────────────────────────────────────────── D-3 #4 완료 팝

        private const float PunchScaleAmount = 1.15f;
        private const float PunchDuration = 0.15f;
        private float _punchT = -1f;

        /// <summary>
        /// D-3 #4 — 완료 반응 팝. `Vfx.questComplete` 링이 터지는 순간 `Vfx`가 이 메서드를
        /// 불러 몸도 같이 반응하게 한다(지금까지는 파티클만 반응했다). `_visual`만
        /// 스케일하므로 물리 콜라이더에는 영향이 없다.
        /// </summary>
        public void PunchScale() => _punchT = 0f;

        // ────────────────────────────────────────────────── D-3 #6 걷기 컨택트 스쿼시

        private const float StepSquashX = 1.05f;
        private const float StepSquashY = 0.9f;
        private const float StepSquashDuration = 0.08f;
        private float _stepSquashT = -1f;
        private int _lastWalkFrame = -1;

        /// <summary>D-3 #6 — 걷기 접지 신호. `Vfx`가 구독해 그 순간에만 먼지를 버스트로
        /// 뿌린다(이전에는 이동 중 연속 방출이었다)</summary>
        public event System.Action StepContact;

        // ────────────────────────────────────────────────── D-3 #3 눈 깜빡임

        private SpriteRenderer _eyeL;
        private SpriteRenderer _eyeR;
        private const float BlinkMinGap = 2f;
        private const float BlinkMaxGap = 5f;
        private const float BlinkExposeSeconds = 0.1f;
        private float _blinkAt = Random.Range(BlinkMinGap, BlinkMaxGap);
        private bool _blinking;

        private static Sprite _eyeSprite;

        /// <summary>
        /// 1×1 스킨색 텍스처를 런타임에 한 번만 굽는다(캐릭터 인스턴스 전체가 공유).
        ///
        /// 색은 `tools/sprites/palette.py`의 `"skin0": "#E0B48C"` — `SetLook`의 기본
        /// 스킨(`skin = "skin0"`)과 맞춘 값이다. 지금 코드베이스에서 `SetLook`을 부르는
        /// 자리(`SquadView.Spawn`·`ZoneWorld`)가 전부 스킨 인자를 안 주고 기본값을
        /// 쓰므로 상수로 고정해도 어긋나지 않는다 — 나중에 실제로 스킨 변형을 쓰게
        /// 되면 이 상수도 같이 손봐야 한다.
        /// </summary>
        private static Sprite EyeSprite
        {
            get
            {
                if (_eyeSprite != null) return _eyeSprite;
                var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
                tex.SetPixel(0, 0, new Color32(0xE0, 0xB4, 0x8C, 0xFF));
                tex.Apply();
                _eyeSprite = Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), SpritePpu);
                return _eyeSprite;
            }
        }

        // chars.py CELL_H(48) · Sprite2DImport.cs PPU(32) · FootPivot(16,2)
        private const float CellHeightPx = 48f;
        private const float SpritePpu = 32f;
        private static readonly Vector2 FootPivotPx = new Vector2(16f, 2f);

        /// <summary>
        /// chars.py 파츠 좌표(픽셀, Pillow 기준 top-down) → 로컬 유닛 오프셋(피벗 기준, Y-up).
        ///
        /// chars.py의 (x,y,w,h)는 위에서 아래로 y를 잰다(`render()`가 `PX.rect`에 그대로
        /// 넘긴다 — 확인함, chars.py:426-434). 반면 `Sprite2DImport.cs`의 `FootPivot`은
        /// "하단 중앙, 발 접지점"이라고 적어 놓고 y=2를 **바닥에서 위로** 2px로 쓴다.
        /// 그래서 `CellHeightPx`에서 파츠 y를 빼 방향을 맞추고, 피벗을 뺀 뒤 PPU로
        /// 나눠 유닛으로 바꾼다. `chars.py`·`Sprite2DImport.cs`는 읽기만 했고 고치지 않았다.
        /// </summary>
        private static Vector2 PartCenterToLocal(float pillowX, float pillowY) =>
            new Vector2((pillowX - FootPivotPx.x) / SpritePpu,
                        (CellHeightPx - pillowY - FootPivotPx.y) / SpritePpu);

        // chars.py S_PARTS["body"]: (14,19,1,1,"eye","head") · (17,19,1,1,"eye","head")
        private static readonly Vector2 EyeSouthL = PartCenterToLocal(14.5f, 19.5f);
        private static readonly Vector2 EyeSouthR = PartCenterToLocal(17.5f, 19.5f);
        // chars.py E_PARTS["body"]: (18,19,1,1,"eye","head")
        private static readonly Vector2 EyeEast = PartCenterToLocal(18.5f, 19.5f);
        // §5.1 W는 E를 X미러링 — 눈도 피벗의 x(=중심선) 기준으로 반사
        private static readonly Vector2 EyeWest = new Vector2(-EyeEast.x, EyeEast.y);
        // N_PARTS["body"]에는 eye 항목이 없다(뒤통수라 안 보인다) — 그래서 EyeNorth는 없다

        // ────────────────────────────────────────────────── D-3 #5 상태 틴트

        /// <summary>
        /// SpriteRenderer.color 곱셈만 쓴다(셰이더 없음, 발주 제약). **곱은 모든 채널을
        /// 같은 비로 줄이면 명도만 바뀌고 채도는 그대로다** — 진짜 채도 연산(HSV)은
        /// 픽셀마다 다른 값이 필요해 단일 곱으로는 불가능하다. "채도 −20%"는 무채색
        /// 쪽으로 살짝 미는 근사이며, 정확한 채도 감산이 아니라 "탁해 보임"을 흉내낸 것임을
        /// 이 주석으로 남긴다.
        /// </summary>
        private static readonly Color FrostbiteTint = Color.Lerp(Color.white, new Color(0.55f, 0.75f, 1f), 0.10f);
        private static readonly Color ExhaustedTint = Color.Lerp(Color.white, new Color(0.75f, 0.75f, 0.70f), 0.20f);

        /// <summary>
        /// 정적 소품과 **같은 공식**이어야 한다. 씬 빌더가 같은 값을 쓴다
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

            if (_visual == null)
            {
                var visualGo = new GameObject("visual");
                visualGo.transform.SetParent(transform, false);
                _visual = visualGo.transform;
            }

            for (var i = 0; i < Layers.Length; i += 1)
            {
                if (_renderers[i] != null) continue;
                var go = new GameObject(Layers[i]);
                go.transform.SetParent(_visual, false);
                var renderer = go.AddComponent<SpriteRenderer>();
                renderer.sortingOrder = i;   // 그룹 안에서의 위아래
                // WC — 8레이어 전부 Lit. 발밑 그림자("그림자")는 이 8레이어에
                // 안 속한다 — `WorldDepthShadowManager`가 `rig.transform`(루트) 밑에
                // 별도 자식으로 붙이므로 여기서 만드는 렌더러 목록에 애초에 안 잡힌다
                if (LitMaterial != null) renderer.sharedMaterial = LitMaterial;
                _renderers[i] = renderer;
            }

            // D-3 #3 — 눈 깜빡임 오버레이. 8레이어 전부보다 위에 그린다(얼굴을 가릴
            // 레이어가 없으므로 순서만 맞으면 항상 보인다)
            if (_eyeL == null) _eyeL = CreateEyeRenderer("eyeL");
            if (_eyeR == null) _eyeR = CreateEyeRenderer("eyeR");
        }

        private SpriteRenderer CreateEyeRenderer(string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_visual, false);
            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = EyeSprite;
            renderer.sortingOrder = Layers.Length;
            renderer.enabled = false;
            // WC — 눈 오버레이도 8레이어와 같은 재질이어야 한다. Lit이 아니면 방이
            // 어두워져도 눈만 원래 밝기로 둥둥 떠 보인다
            if (LitMaterial != null) renderer.sharedMaterial = LitMaterial;
            return renderer;
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
            _lastWalkFrame = -1;   // D-3 #6 — 새 클립이니 걷기 접지 판정도 새로 시작한다
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

        /// <summary>상태 이상이 idle을 대체한다 (§5.5 19~20 shiver / pant) +
        /// D-3 #5 상태 틴트(동상=한랭 블루, 탈진=탁한 무채색)</summary>
        public void SetDistress(bool frostbite, bool exhausted)
        {
            if (_frostbite == frostbite && _exhausted == exhausted) return;
            _frostbite = frostbite;
            _exhausted = exhausted;

            var tint = Color.white;
            if (frostbite) tint *= FrostbiteTint;
            if (exhausted) tint *= ExhaustedTint;
            ApplyTint(tint);

            // 서 있는 중이면 바로 갈아 끼운다. 걷는 중이면 멈출 때 `Step`이 집는다
            if (_clip != null && (_clip.name == "idle" || _clip.name == "shiver" ||
                                  _clip.name == "pant"))
            {
                Play(IdleClip);
            }
        }

        private void ApplyTint(Color tint)
        {
            foreach (var renderer in _renderers)
            {
                if (renderer != null) renderer.color = tint;
            }
            if (_eyeL != null) _eyeL.color = tint;
            if (_eyeR != null) _eyeR.color = tint;
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

            UpdateBlink();
            UpdateVisualScale();

            if (_clip == null) return;

            var frame = 0;
            if (_clip.frames > 1)
            {
                var fps = (float)_clip.fps;
                if (_clip.name == "idle") fps *= IdleFpsScale;                          // D-3 #2
                if (_clip.name == "idle" || _clip.name == "walk") fps *= _paceScale;    // D-3 #7
                _clock += Time.deltaTime * fps;
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

            // D-3 #6 — 걷기 접지(컨택트) 프레임 감지.
            // 근거: chars.py의 `_walk_cycle(1, 1)`(walk 클립, chars.py:154-161) 6프레임 —
            //   0 {} · 1 legL 든다(amp) · 2 legL **최고점**(amp*2) · 3 {} · 4 legR 든다 · 5 legR **최고점**
            // 2·5번 다음 프레임(3·0번)이 다리를 곧바로 {}(접지)로 돌려놓으므로, 2·5번이
            // "다리가 가장 높이 들려 있다가 바로 다음에 땅을 짚는" 무게 정점 프레임이다.
            // walk에만 건다 — run도 같은 포즈 함수를 쓰지만 발주 문구가 "걷기 클립"으로 한정했다.
            if (_clip.name == "walk" && frame != _lastWalkFrame && (frame == 2 || frame == 5))
            {
                _stepSquashT = 0f;
                StepContact?.Invoke();
            }
            _lastWalkFrame = frame;

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

        /// <summary>
        /// D-3 #4·#6 — 완료 팝과 걷기 컨택트 스쿼시를 같은 `_visual.localScale`에
        /// 곱으로 합친다. 둘이 겹칠 일은 드물지만(완료 팝은 퀘스트 완료 순간, 스쿼시는
        /// 걷는 동안) 곱으로 합쳐 두면 겹쳐도 값이 안 튄다.
        /// </summary>
        private void UpdateVisualScale()
        {
            var scaleX = 1f;
            var scaleY = 1f;

            if (_punchT >= 0f)
            {
                _punchT += Time.deltaTime;
                var t = Mathf.Clamp01(_punchT / PunchDuration);
                var k = Mathf.Sin(t * Mathf.PI); // 0→1→0 — 눌렀다 되돌아온다
                var s = Mathf.Lerp(1f, PunchScaleAmount, k);
                scaleX *= s;
                scaleY *= s;
                if (_punchT >= PunchDuration) _punchT = -1f;
            }

            if (_stepSquashT >= 0f)
            {
                _stepSquashT += Time.deltaTime;
                var t = Mathf.Clamp01(_stepSquashT / StepSquashDuration);
                var k = Mathf.Sin(t * Mathf.PI);
                scaleX *= Mathf.Lerp(1f, StepSquashX, k);
                scaleY *= Mathf.Lerp(1f, StepSquashY, k);
                if (_stepSquashT >= StepSquashDuration) _stepSquashT = -1f;
            }

            if (_visual != null) _visual.localScale = new Vector3(scaleX, scaleY, 1f);
        }

        /// <summary>
        /// D-3 #3 — 눈 깜빡임. 좌표는 위쪽 `PartCenterToLocal`/`EyeSouthL` 등 상수가
        /// `chars.py`의 eye 파츠 정의를 읽어 가져온 값이다(파일 자체는 미수정).
        ///
        /// 스킨색 1×1을 눈 위에 얹어 "감은 눈"을 흉내낸다 — 새 프레임 없이(147칸 금지
        /// 카드) 표정이 생긴다. 평소엔 꺼두고 2~5초마다 0.1초만 켠다.
        /// </summary>
        private void UpdateBlink()
        {
            if (_eyeL == null && _eyeR == null) return;

            _blinkAt -= Time.deltaTime;
            if (_blinkAt <= 0f)
            {
                _blinking = !_blinking;
                _blinkAt = _blinking ? BlinkExposeSeconds : Random.Range(BlinkMinGap, BlinkMaxGap);
            }

            // 뒤통수(N)에는 그릴 눈이 없다 — chars.py N_PARTS["body"]에 eye 항목이 없다
            var visible = _blinking && _facing != Facing.North;
            var south = _facing == Facing.South;

            if (_eyeL != null)
            {
                _eyeL.transform.localPosition = south ? EyeSouthL : (_facing == Facing.West ? EyeWest : EyeEast);
                _eyeL.enabled = visible;
            }
            if (_eyeR != null)
            {
                _eyeR.transform.localPosition = EyeSouthR;
                _eyeR.enabled = visible && south;   // 옆모습은 E_PARTS 정의상 눈이 한 개뿐
            }
        }
    }
}
