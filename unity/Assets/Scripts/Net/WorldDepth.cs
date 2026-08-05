using UnityEngine;

namespace SoldierADay.Net
{
    /// <summary>
    /// 벽 높이(오블리크) 배치 · 런타임 캐스트 섀도우 · 바닥 AO (SAD-ART-001 W2 발주).
    ///
    /// 벽이 32×32 평면뿐이라 방이 "위에서 본 도면"으로 읽힌다. 여기서는 실제
    /// 지오메트리·콜라이더를 바꾸지 않고 그림만 얹어 깊이 단서 세 가지를 만든다:
    ///
    ///  1. 벽 아래 26px 오블리크 정면 — `BaseScene`이 씬 빌드 시각에 굽는다(정적)
    ///  2. 소품·캐릭터 아래 캐스트 섀도우 — 우하단 고정, 대상 높이에 비례
    ///  3. 벽에 인접한 바닥 AO — `BaseScene`이 타일맵으로 굽는다(정적)
    ///
    /// 이 파일은 **런타임 조립**(플레이 빌드에도 포함)이라 `UnityEditor`를 참조하지
    /// 않는다. 씬에 저장돼야 하는 것(AO 타일 · 소품 그림자 스프라이트)의 자산화는
    /// `BaseScene`(에디터) 쪽 책임이고, 여기서는 그 텍스처를 만드는 순수 함수만 준다 —
    /// 그래야 캐릭터(런타임, 저장 안 됨)와 소품(에디터, 저장됨)이 같은 픽셀을 쓰면서도
    /// 코드가 갈라지지 않는다.
    /// </summary>
    public static class WorldDepth
    {
        /* ══════════════════════════════════ 강도 · 형태 상수 — 전부 여기 한 곳 */

        /// <summary>캐스트 섀도우 알파(반투명 검정)</summary>
        public const float ShadowAlpha = 0.32f;

        /// <summary>바닥 AO 알파. §W2 "알파 0.35 이하 권장" — 실시간 조명(W3)이 들어오면 여기만 낮추면 된다</summary>
        public const float AoAlpha = 0.3f;

        /// <summary>그림자 자식 오브젝트 이름. 매니저가 "이미 붙었는지" 판별하는 표식이기도 하다</summary>
        public const string ShadowChildName = "그림자";

        /// <summary>우하단 45°(광원 좌상단) — 대상 높이(유닛)에 곱해 오프셋을 만든다</summary>
        public static readonly Vector2 ShadowOffsetFactor = new Vector2(0.16f, -0.11f);

        /// <summary>그림자 폭 비율(대상 높이 대비)</summary>
        public const float ShadowWidthFactor = 0.62f;

        /// <summary>그림자 세로 압축 비율(대상 높이 대비) — "살짝 눌린" 형태</summary>
        public const float ShadowSquashFactor = 0.30f;

        /// <summary>§5.1 캐릭터 실측 14×34px → 유닛. 개별 렌더러 바운즈를 못 읽으므로(CharacterRig 비공개) 고정값을 쓴다</summary>
        public const float DefaultCharacterHeight = 34f / CameraRig.PPU;

        private const int ShadowTexWidth = 24;
        private const int ShadowTexHeight = 16;

        private static Sprite _runtimeShadowSprite;

        /* ══════════════════════════════════════════════════ 텍스처(순수 함수) */

        /// <summary>
        /// 부드러운 타원 알파 텍스처. 에디터(소품 굽기)·런타임(캐릭터) 둘 다 이 픽셀을 쓴다 —
        /// 그림자 생김새를 한 곳에서만 정의하기 위해서다.
        /// </summary>
        public static Texture2D BuildShadowTexture()
        {
            var tex = new Texture2D(ShadowTexWidth, ShadowTexHeight, TextureFormat.RGBA32, false)
            {
                name = "WorldDepth_Shadow",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };

            var pixels = new Color32[ShadowTexWidth * ShadowTexHeight];
            var cx = (ShadowTexWidth - 1) * 0.5f;
            var cy = (ShadowTexHeight - 1) * 0.5f;

            for (var y = 0; y < ShadowTexHeight; y += 1)
            {
                for (var x = 0; x < ShadowTexWidth; x += 1)
                {
                    var nx = (x - cx) / (ShadowTexWidth * 0.5f);
                    var ny = (y - cy) / (ShadowTexHeight * 0.5f);
                    var d = Mathf.Sqrt(nx * nx + ny * ny);
                    var a = Mathf.Clamp01(1f - d);
                    a *= a;   // 가장자리를 더 부드럽게(선형이면 테두리가 또렷하게 잘려 보인다)
                    pixels[y * ShadowTexWidth + x] = new Color(0f, 0f, 0f, a);
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply(false, true);
            return tex;
        }

        /// <summary>위 텍스처를 스프라이트로 감싼다. PPU는 카메라·타일과 같은 값을 쓴다</summary>
        public static Sprite BuildShadowSprite(Texture2D texture) =>
            Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height),
                          new Vector2(0.5f, 0.5f), CameraRig.PPU);

        /// <summary>
        /// AO 타일이 쓸 1×1 흰 텍스처. 알파를 텍스처가 아니라 `Tile.color`로 주는 이유는
        /// §W2 "강도를 상수로 빼라"다 — 텍스처를 다시 굽지 않고 값 하나만 바꾸면 된다.
        /// </summary>
        public static Texture2D BuildAoTexture()
        {
            var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false)
            {
                name = "WorldDepth_AO",
                filterMode = FilterMode.Point,
            };
            tex.SetPixel(0, 0, Color.white);
            tex.Apply(false, true);
            return tex;
        }

        public static Sprite BuildAoSprite(Texture2D texture) =>
            Sprite.Create(texture, new Rect(0f, 0f, 1f, 1f), new Vector2(0.5f, 0.5f), 1f);

        /* ══════════════════════════════════════════════════════ 그림자 배치 */

        /// <summary>
        /// 소품·캐릭터에 공통으로 그림자를 붙인다. 방향·비율은 상수로 고정하고
        /// 대상 높이만 다르게 받는다 — 우하단으로 살짝 밀리고 세로로 눌린 타원 하나.
        ///
        /// **회전하지 않는다.** 진짜 투사 그림자(광원 각도로 늘어나는)는 메쉬 변형이
        /// 필요해 드로우콜·복잡도가 늘고, 이 게임 규모에서는 눌린 타원만으로도
        /// "바닥에 붙어 있다"는 단서로 충분하다(§W2 성능 제약).
        /// </summary>
        private static SpriteRenderer AttachShadow(Transform host, Sprite sprite, float heightUnits, int sortingOrder)
        {
            var go = new GameObject(ShadowChildName);
            go.transform.SetParent(host, false);
            go.transform.localPosition = new Vector3(
                heightUnits * ShadowOffsetFactor.x,
                heightUnits * ShadowOffsetFactor.y,
                0f);

            var width = Mathf.Max(0.12f, heightUnits * ShadowWidthFactor);
            var squash = Mathf.Max(0.06f, heightUnits * ShadowSquashFactor);
            go.transform.localScale = new Vector3(width, squash, 1f);

            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.color = new Color(0f, 0f, 0f, ShadowAlpha);
            renderer.sortingOrder = sortingOrder;
            return renderer;
        }

        /// <summary>런타임 전용 스프라이트 — 씬에 저장되지 않으므로(캐릭터는 매 플레이 새로 생성) 지연 캐시만 한다</summary>
        private static Sprite RuntimeShadowSprite()
        {
            if (_runtimeShadowSprite == null)
                _runtimeShadowSprite = BuildShadowSprite(BuildShadowTexture());
            return _runtimeShadowSprite;
        }

        /// <summary>
        /// 캐릭터 그림자(런타임). `WorldDepthShadowManager`가 새 캐릭터를 찾을 때마다 부른다.
        ///
        /// 그룹(`SortingGroup`) 안 상대 순서만 정하면 된다 — `CharacterRig.LateUpdate`가
        /// 그룹 전체의 절대 위치를 매 프레임 갱신하므로, 그림자가 몸(레이어 0)보다
        /// 뒤(-1)이기만 하면 캐릭터가 움직여도 항상 발밑에 붙어 따라간다.
        /// </summary>
        public static void AttachCharacterShadow(Transform host) =>
            AttachShadow(host, RuntimeShadowSprite(), DefaultCharacterHeight, -1);

        /// <summary>
        /// 소품 그림자(에디터, 정적). 스프라이트는 `BaseScene`이 굽고 넘겨준다 — 씬에
        /// 저장돼야 하는 자산화(`AssetDatabase`)는 이 파일이 아니라 에디터 쪽 책임이다.
        ///
        /// 소품은 캐릭터처럼 공유 `SortingGroup`이 없으므로, 그림자의 **절대**
        /// `sortingOrder`를 소품보다 1 낮게 고정한다. 소품은 움직이지 않으니 한 번
        /// 계산하면 계속 맞는다.
        /// </summary>
        public static void AttachStaticShadow(Transform host, Sprite sprite, float heightUnits, int hostSortingOrder) =>
            AttachShadow(host, sprite, Mathf.Max(heightUnits, 0.4f), hostSortingOrder - 1);
    }

    /// <summary>
    /// 씬에 있는 캐릭터(플레이어·분대원)를 찾아 캐스트 섀도우를 붙인다.
    ///
    /// 분대원은 스냅샷을 받은 뒤 `SquadView`가 늦게 만들어서 씬 빌드 시점에는 아직
    /// 없다 — 그래서 소품처럼 미리 구워둘 수 없고, 등장을 기다렸다 붙여야 한다.
    /// 매 프레임 훑으면(§W2 성능 제약 "화면 밖 오브젝트에 매 프레임 연산 금지") 낭비이므로
    /// `scanInterval`마다만 훑고, 이미 붙은 것은 자식 이름(`WorldDepth.ShadowChildName`)으로
    /// 걸러 다시 만들지 않는다.
    /// </summary>
    public sealed class WorldDepthShadowManager : MonoBehaviour
    {
        [Tooltip("새 캐릭터를 찾는 주기(초). 캐릭터 수가 적어 낮은 주기로도 충분하다")]
        public float scanInterval = 1f;

        private float _clock;

        private void Update()
        {
            _clock += Time.deltaTime;
            if (_clock < scanInterval) return;
            _clock = 0f;

            var rigs = FindObjectsByType<CharacterRig>(FindObjectsSortMode.None);
            foreach (var rig in rigs)
            {
                if (rig == null) continue;
                if (rig.transform.Find(WorldDepth.ShadowChildName) != null) continue;
                WorldDepth.AttachCharacterShadow(rig.transform);
            }
        }
    }
}
