using UnityEngine;

namespace SoldierADay.Net
{
    /// <summary>
    /// 벽 높이(오블리크) 배치 · 접지 그림자 · 바닥 AO (SAD-ART-001 W2 발주, §WB 개정).
    ///
    /// 벽이 32×32 평면뿐이라 방이 "위에서 본 도면"으로 읽힌다. 여기서는 실제
    /// 지오메트리·콜라이더를 바꾸지 않고 그림만 얹어 깊이 단서 세 가지를 만든다:
    ///
    ///  1. 벽 정면(오블리크) — `BaseScene`이 씬 빌드 시각에 굽는다(정적)
    ///  2. 소품·캐릭터 아래 접지 그림자 — §WB "실내 천장등이 주광"이므로 오프셋은
    ///     0에 가깝고, 소품은 대상 알파 실루엣에서 폭·모양을 그대로 뽑는다(공용 작은
    ///     타원 금지). 캐릭터는 개별 렌더러 바운즈를 못 읽어(CharacterRig 비공개)
    ///     여전히 눌린 타원이지만 오프셋은 같은 규칙(0)을 따른다
    ///  3. 벽에 인접한 바닥 AO — `BaseScene`이 벽 쪽마다 낱개 그라디언트 스프라이트로
    ///     굽는다(정적, §W4부터 타일맵 균일 알파 대신 이 방식을 쓴다 — 계단 현상 방지)
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

        /// <summary>캐스트 섀도우 알파(반투명 검정). §W4에서 0.32→0.42 — AO를 낮추는 김에
        /// 그림자가 바닥과 구분되도록 소폭 또렷하게 했다(모양은 그대로, 부드러운 타원)</summary>
        public const float ShadowAlpha = 0.42f;

        /// <summary>
        /// 바닥 AO **최대**(벽 접촉부) 알파. §W4 "0.2 이하 권장" — 0.3은 과했다.
        /// 실제 강도는 이 값에 `BuildAoEdgeTexture`의 제곱 그라디언트가 곱해져 벽에서
        /// 멀어질수록 0까지 부드럽게 빠진다(타일 단위 하드에지 금지). 조명(W3)이 이미
        /// 구석을 어둡게 하므로 AO는 거들기만 하면 된다 — 0으로 두면 완전히 꺼진다.
        /// </summary>
        public const float AoAlpha = 0.18f;

        /// <summary>그림자 자식 오브젝트 이름. 매니저가 "이미 붙었는지" 판별하는 표식이기도 하다</summary>
        public const string ShadowChildName = "그림자";

        /// <summary>
        /// §WB — 예전엔 우하단 45°(좌상단 태양광 가정)로 밀었다. 여기는 실내이고 주광은
        /// 천장등이라 옆으로 밀 이유가 없다. 대상 발밑(원점) 그대로 0으로 둔다.
        /// </summary>
        public static readonly Vector2 ShadowOffsetFactor = Vector2.zero;

        /// <summary>그림자 폭 비율(대상 높이 대비) — 캐릭터(실측 불가) 전용 타원에만 쓴다</summary>
        public const float ShadowWidthFactor = 0.62f;

        /// <summary>그림자 세로 압축 비율(대상 높이 대비) — "살짝 눌린" 형태. 캐릭터 전용</summary>
        public const float ShadowSquashFactor = 0.30f;

        /// <summary>§5.1 캐릭터 실측 14×34px → 유닛. 개별 렌더러 바운즈를 못 읽으므로(CharacterRig 비공개) 고정값을 쓴다</summary>
        public const float DefaultCharacterHeight = 34f / CameraRig.PPU;

        private const int ShadowTexWidth = 24;
        private const int ShadowTexHeight = 16;

        /// <summary>실루엣 하단 몇 %를 "발밑 폭"으로 볼지(§WB). 낮추면 맨 아랫단 폭만
        /// 반영하고, 높이면 몸통 폭까지 반영해 더 뭉툭해진다</summary>
        private const float FootBandFrac = 0.4f;

        /// <summary>접지 그림자 텍스처 높이(px, 눌린 두께) — 강도(눌림 정도)는 이 상수
        /// 하나로만 조절한다. 폭은 대상 실측을 그대로 쓰므로 별도 상수가 필요 없다</summary>
        private const int FootShadowHeight = 10;

        /// <summary>좌우 하드에지 완화용 박스 블러 반경(px)</summary>
        private const int FootShadowBlurRadius = 2;

        /// <summary>
        /// 접지 그림자 접촉 오프셋(유닛). 대상 밑변과 완전히 겹치도록 아주 살짝(1px)
        /// 위로 올려 반올림 오차로 미세한 틈이 뜨는 것을 막는다 — §WB "오프셋은 거의
        /// 0(1~2px 이내)". 옆으로는 절대 밀지 않는다(x=0).
        /// </summary>
        private const float FootShadowContactNudge = 1f / CameraRig.PPU;

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

        /* ══════════════════════════════ 접지 그림자(소품, 실루엣 기반) — §WB */

        /// <summary>
        /// 대상 스프라이트의 알파 실루엣에서 접지 그림자 텍스처를 뽑는다. 공용 작은
        /// 타원(예전 방식) 대신 폭을 원본 픽셀 폭 그대로 담는다 — 같은 PPU로 스프라이트를
        /// 만들면(`BuildFootShadowSprite`) 세계 크기가 자동으로 대상과 일치해서, 넓은
        /// 소품(침상·관물대)은 넓게, 좁은 소품은 좁게 나온다.
        ///
        /// 실루엣 전체가 아니라 텍스처 y=0 쪽(스프라이트 "발밑" 밴드, `FootBandFrac`)만
        /// 보고 열(column)별 평균 알파를 구한 뒤 그 프로필을 세로로 눌러 쌓는다 — 그래서
        /// 각진 소품은 각지게, 폭이 좁아지는 부분이 있으면 그만큼 좁게 나온다.
        ///
        /// `Sprite.texture`가 Read/Write 꺼짐이어도 항상 읽어야 하므로 `RenderTexture`로
        /// 한 번 복제해서 `ReadPixels`한다(GPU 블릿은 Read/Write 설정과 무관하다) — 씬
        /// 빌드(에디터) 시각에만 부르는 무거운 경로이고, 호출부가 소스 스프라이트별로
        /// 캐시해서 같은 아트 파일마다 한 번만 부른다.
        /// </summary>
        public static Texture2D BuildFootShadowTexture(Sprite source)
        {
            var srcTex = source.texture;
            var rect = source.textureRect;
            var x0 = Mathf.RoundToInt(rect.x);
            var y0 = Mathf.RoundToInt(rect.y);
            var srcW = Mathf.Max(1, Mathf.RoundToInt(rect.width));
            var srcH = Mathf.Max(1, Mathf.RoundToInt(rect.height));

            var pixels = ReadPixelsSafely(srcTex);

            // 열별 알파(하단 밴드 평균) — 텍스처는 아래가 y=0(Unity 관례)이므로
            // y=0..bandH가 바로 스프라이트의 "발밑" 부분이다
            var bandH = Mathf.Clamp(Mathf.RoundToInt(srcH * FootBandFrac), 1, srcH);
            var colAlpha = new float[srcW];
            for (var x = 0; x < srcW; x += 1)
            {
                var sum = 0f;
                for (var y = 0; y < bandH; y += 1)
                {
                    var px = Mathf.Clamp(x0 + x, 0, srcTex.width - 1);
                    var py = Mathf.Clamp(y0 + y, 0, srcTex.height - 1);
                    sum += pixels[py * srcTex.width + px].a / 255f;
                }
                colAlpha[x] = sum / bandH;
            }

            // 좌우 하드에지 완화(박스 블러) — 실루엣 경계가 계단처럼 뚝 끊기지 않게
            var blurred = new float[srcW];
            for (var x = 0; x < srcW; x += 1)
            {
                var sum = 0f;
                var n = 0;
                for (var k = -FootShadowBlurRadius; k <= FootShadowBlurRadius; k += 1)
                {
                    var xx = x + k;
                    if (xx < 0 || xx >= srcW) continue;
                    sum += colAlpha[xx];
                    n += 1;
                }
                blurred[x] = n > 0 ? sum / n : colAlpha[x];
            }

            var tex = new Texture2D(srcW, FootShadowHeight, TextureFormat.RGBA32, false)
            {
                name = "WorldDepth_FootShadow",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };

            var outPixels = new Color32[srcW * FootShadowHeight];
            for (var oy = 0; oy < FootShadowHeight; oy += 1)
            {
                // oy가 클수록(텍스처 위쪽) 대상 밑변에 가깝다 — 접지부가 가장 진하고
                // 바깥(oy=0)으로 갈수록 부드럽게 빠진다. 스모스텝 — 선형이면 가장자리가
                // 또렷하게 잘려 보인다(§W2와 같은 이유)
                var t = FootShadowHeight <= 1 ? 1f : oy / (float)(FootShadowHeight - 1);
                var rowFactor = t * t * (3f - 2f * t);
                for (var x = 0; x < srcW; x += 1)
                {
                    var a = blurred[x] * rowFactor;
                    outPixels[oy * srcW + x] = new Color(0f, 0f, 0f, a);
                }
            }

            tex.SetPixels32(outPixels);
            tex.Apply(false, true);
            return tex;
        }

        /// <summary>
        /// 원본 텍스처를 CPU에서 읽는다. `Texture2D.GetPixels`는 Read/Write가 꺼진
        /// 임포트 자산에서 예외를 던지므로, `RenderTexture`로 한 번 복제해 GPU 블릿
        /// 경로로 우회한다 — 어떤 임포트 설정에서도 항상 동작한다.
        /// </summary>
        private static Color32[] ReadPixelsSafely(Texture2D srcTex)
        {
            var rt = RenderTexture.GetTemporary(srcTex.width, srcTex.height, 0, RenderTextureFormat.ARGB32);
            var prevActive = RenderTexture.active;
            Graphics.Blit(srcTex, rt);
            RenderTexture.active = rt;

            var readable = new Texture2D(srcTex.width, srcTex.height, TextureFormat.RGBA32, false);
            readable.ReadPixels(new Rect(0f, 0f, srcTex.width, srcTex.height), 0, 0);
            readable.Apply(false, false);

            RenderTexture.active = prevActive;
            RenderTexture.ReleaseTemporary(rt);

            var pixels = readable.GetPixels32();
            Object.DestroyImmediate(readable);
            return pixels;
        }

        /// <summary>피벗을 "위쪽 중앙"으로 둔다 — 이 점이 대상 밑변에 닿아야 하므로.
        /// PPU는 반드시 원본 소품과 같은 값을 써야 폭이 그대로 일치한다(호출부 책임)</summary>
        public static Sprite BuildFootShadowSprite(Texture2D texture, float ppu) =>
            Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height),
                          new Vector2(0.5f, 1f), ppu);

        private const int AoTexSize = 32;

        /// <summary>
        /// §W4 바닥 AO 그라디언트 텍스처. **타일 단위 균일 알파는 그라디언트가 될 수
        /// 없다**(계단 현상의 원인이었다) — 대신 위(벽 쪽, +Y)가 진하고 아래(바닥 안쪽,
        /// -Y)로 갈수록 0까지 부드럽게 빠지는 세로 그라디언트를 굽는다. 제곱 커브를 쓰는
        /// 이유는 선형이면 그라디언트 끝에서 여전히 눈에 띄는 경계가 생기기 때문이다.
        ///
        /// 이 텍스처 자체는 "북쪽에 벽이 있다"(위가 진하다) 방향 하나만 표현한다 —
        /// 나머지 3방향은 `BaseScene.PlaceAoEdge`가 90°씩 돌려서 재사용한다.
        /// 실제 강도(색)는 여기서 굽지 않고 `AoAlpha`를 렌더러 색으로 곱한다 — 그래야
        /// 텍스처를 다시 굽지 않고 상수 하나만 바꿔도(0까지) 강도가 바뀐다.
        /// </summary>
        public static Texture2D BuildAoEdgeTexture()
        {
            var tex = new Texture2D(AoTexSize, AoTexSize, TextureFormat.RGBA32, false)
            {
                name = "WorldDepth_AOEdge",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };

            var pixels = new Color32[AoTexSize * AoTexSize];
            for (var y = 0; y < AoTexSize; y += 1)
            {
                // 텍스처 좌표는 아래가 0이다(Unity 관례) — y가 클수록(위, +Y) 벽에 가깝다
                var t = y / (float)(AoTexSize - 1);
                var a = t * t;
                for (var x = 0; x < AoTexSize; x += 1)
                    pixels[y * AoTexSize + x] = new Color(0f, 0f, 0f, a);
            }

            tex.SetPixels32(pixels);
            tex.Apply(false, true);
            return tex;
        }

        /// <summary>1유닛 셀을 정확히 채우는 Center 피벗 스프라이트로 감싼다 — 회전만으로
        /// 4방향 전부를 커버하려면 피벗이 셀 중심이어야 한다</summary>
        public static Sprite BuildAoEdgeSprite(Texture2D texture) =>
            Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height),
                          new Vector2(0.5f, 0.5f), texture.width);

        /* ══════════════════════════════════════════════════════ 그림자 배치 */

        /// <summary>
        /// 캐릭터 전용 타원 그림자를 붙인다. 개별 렌더러 바운즈를 못 읽어(CharacterRig
        /// 비공개) 실루엣 기반 접지 그림자(§WB, 소품 쪽)를 쓸 수 없는 유일한 경로다 —
        /// 대신 오프셋을 0으로 두고(§WB) 대상 원점(발 위치)에 그대로 겹쳐 최소한
        /// "발밑에 붙어 있다"는 단서를 준다.
        ///
        /// **회전하지 않는다.** 진짜 투사 그림자(광원 각도로 늘어나는)는 메쉬 변형이
        /// 필요해 드로우콜·복잡도가 늘고, 이 게임 규모에서는 눌린 타원만으로도 충분하다
        /// (§W2 성능 제약).
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
        /// 소품 접지 그림자(에디터, 정적) — §WB 실루엣 기반. `footSprite`는 `BaseScene`이
        /// `BuildFootShadowTexture`/`BuildFootShadowSprite`로 굽고 넘겨준다 — 씬에 저장돼야
        /// 하는 자산화(`AssetDatabase`)는 이 파일이 아니라 에디터 쪽 책임이다.
        ///
        /// 스프라이트가 이미 대상과 같은 폭·PPU, "위쪽 중앙" 피벗으로 구워져 있으므로
        /// 스케일 왜곡 없이 그대로 붙이면 대상 밑변에 자동으로 맞는다. 세로로 아주
        /// 살짝(`FootShadowContactNudge`)만 올려 반올림 오차로 뜨는 틈을 막는다.
        ///
        /// 소품은 캐릭터처럼 공유 `SortingGroup`이 없으므로, 그림자의 **절대**
        /// `sortingOrder`를 소품보다 1 낮게 고정한다. 소품은 움직이지 않으니 한 번
        /// 계산하면 계속 맞는다.
        /// </summary>
        public static void AttachStaticShadow(Transform host, Sprite footSprite, int hostSortingOrder)
        {
            var go = new GameObject(ShadowChildName);
            go.transform.SetParent(host, false);
            go.transform.localPosition = new Vector3(0f, FootShadowContactNudge, 0f);

            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = footSprite;
            renderer.color = new Color(0f, 0f, 0f, ShadowAlpha);
            renderer.sortingOrder = hostSortingOrder - 1;
        }
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
