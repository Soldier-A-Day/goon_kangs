using System.Collections.Generic;
using System.IO;
using SoldierADay.Hd2dMock;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

namespace SoldierADay.Hd2dMock.EditorTools
{
    /// <summary>
    /// HD-2D "다이어트" 목업 씬 빌더 — 판단용 실험 1장면.
    ///
    /// 본선(Assets/Scenes/Base.unity, Assets/Editor/Pipeline/*)에는 손대지 않는다.
    /// 여기서 만드는 모든 것 — 스크립트·씬·머티리얼·파이프라인 복제본 —은
    /// Assets/Hd2dMock/ 아래에만 산다. 텍스처·스프라이트는 본선 자산
    /// (Assets/Art/2d/)을 **읽기만** 한다 — "예쁘게"가 아니라 "대표성 있게"가
    /// 목적이라 실제 게임 그림을 그대로 써야 프레임 수치가 의미가 있다.
    /// </summary>
    public static class Hd2dMockSceneBuilder
    {
        private const string Art2DDir = "Assets/Art/2d";
        private const string TilesDir = Art2DDir + "/tiles";

        private const string RootDir = "Assets/Hd2dMock";
        private const string GeneratedDir = RootDir + "/Generated";
        private const string MaterialDir = GeneratedDir + "/Materials";
        private const string VolumeDir = GeneratedDir + "/Volumes";
        private const string PipelineDir = GeneratedDir + "/Pipeline";
        private const string ScenePath = RootDir + "/Hd2dMock.unity";

        // 본선 3D 렌더러 원본 — **읽기만** 한다. 복제해서 postProcessData만 채운다
        private const string SourceUrpAssetPath = "Assets/M0/URP_Asset.asset";
        private const string SourceUrpRendererPath = "Assets/M0/URP_Renderer.asset";
        private const string PackagePostProcessData =
            "Packages/com.unity.render-pipelines.universal/Runtime/Data/PostProcessData.asset";

        // 생활관 방 치수 (X: 폭, Z: 깊이). 카메라 쪽(−Z)은 열어 둔 컷어웨이 3면 룸
        private const float RoomWidth = 8f;
        private const float RoomDepth = 6f;
        private const float WallHeight = 2.4f;
        private const float WallThickness = 0.25f;

        private struct PropSpec
        {
            public string File;
            public Vector3 Position;
            public Vector2 Footprint;
        }

        private struct CharSpec
        {
            public Vector3 Position;
        }

        [MenuItem("SOLDIER/HD-2D 목업 씬 생성")]
        public static void CreateScene()
        {
            Directory.CreateDirectory(MaterialDir);
            Directory.CreateDirectory(VolumeDir);
            Directory.CreateDirectory(PipelineDir);
            AssetDatabase.Refresh();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var root = new GameObject("HD2D 목업");

            var room = new GameObject("방");
            room.transform.SetParent(root.transform, false);
            BuildFloor(room.transform);
            BuildWalls(room.transform);

            var blobMat = BuildBlobMaterial();

            var propsRoot = new GameObject("소품");
            propsRoot.transform.SetParent(root.transform, false);
            BuildProps(propsRoot.transform, blobMat);

            var charsRoot = new GameObject("캐릭터");
            charsRoot.transform.SetParent(root.transform, false);
            BuildCharacters(charsRoot.transform, blobMat);

            var lightsRoot = new GameObject("조명");
            lightsRoot.transform.SetParent(root.transform, false);
            var lights = BuildLights(lightsRoot.transform);

            var volume = BuildPostVolume(root.transform);

            var uiRoot = new GameObject("저해상도 오버레이");
            uiRoot.transform.SetParent(root.transform, false);
            var canvas = BuildOverlayCanvas(uiRoot.transform, out var rawImage);

            var camera = BuildCamera(root.transform, canvas, rawImage, out var scaler);

            var pipelineAsset = BuildPipelineCopy();
            var bootGo = new GameObject("파이프라인 부트스트랩");
            bootGo.transform.SetParent(root.transform, false);
            bootGo.AddComponent<Hd2dPipelineBootstrap>().pipelineAsset = pipelineAsset;

            var togglesGo = new GameObject("토글");
            togglesGo.transform.SetParent(root.transform, false);
            var toggles = togglesGo.AddComponent<Hd2dSceneToggles>();
            toggles.postVolume = volume;
            toggles.pointLights = lights.ToArray();
            toggles.resolutionScaler = scaler;

            var meter = togglesGo.AddComponent<Hd2dFpsMeter>();
            meter.toggles = toggles;

            _ = camera; // 카메라는 이미 씬에 배선됨 — 참조만 남겨 경고 방지

            Directory.CreateDirectory(RootDir);
            EditorSceneManager.SaveScene(scene, ScenePath);
            Debug.Log($"[HD2D 목업] 씬 생성 완료 → {ScenePath}");
        }

        /* ══════════════════════════════════════════════════ 방(바닥·벽) */

        private static void BuildFloor(Transform parent)
        {
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>($"{TilesDir}/floor_wood.png");
            var go = GameObject.CreatePrimitive(PrimitiveType.Plane);
            go.name = "바닥";
            StripCollider(go);
            go.transform.SetParent(parent, false);
            // 기본 Plane은 10×10 유닛이다. RoomWidth×RoomDepth로 맞춘다
            go.transform.localScale = new Vector3(RoomWidth / 10f, 1f, RoomDepth / 10f);

            var mat = BuildTiledMaterial("Hd2dMock_Floor", tex, new Vector2(RoomWidth, RoomDepth));
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
        }

        private static void BuildWalls(Transform parent)
        {
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>($"{TilesDir}/wall_interior_15.png");
            var mat = BuildTiledMaterial("Hd2dMock_Wall", tex, new Vector2(RoomWidth, WallHeight));

            // 카메라 쪽(−Z)은 열어 둔다 — 컷어웨이 3면 룸
            Wall(parent, "벽_북", new Vector3(0f, WallHeight * 0.5f, RoomDepth * 0.5f),
                new Vector3(RoomWidth, WallHeight, WallThickness), mat, new Vector2(RoomWidth, WallHeight));

            var sideMat = BuildTiledMaterial("Hd2dMock_Wall_Side", tex, new Vector2(RoomDepth, WallHeight));
            Wall(parent, "벽_서", new Vector3(-RoomWidth * 0.5f, WallHeight * 0.5f, 0f),
                new Vector3(WallThickness, WallHeight, RoomDepth + WallThickness), sideMat,
                new Vector2(RoomDepth, WallHeight));
            Wall(parent, "벽_동", new Vector3(RoomWidth * 0.5f, WallHeight * 0.5f, 0f),
                new Vector3(WallThickness, WallHeight, RoomDepth + WallThickness), sideMat,
                new Vector2(RoomDepth, WallHeight));
        }

        private static void Wall(Transform parent, string name, Vector3 position, Vector3 size,
                                 Material mat, Vector2 tiling)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            StripCollider(go);
            go.transform.SetParent(parent, false);
            go.transform.position = position;
            go.transform.localScale = size;
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
        }

        private static void StripCollider(GameObject go)
        {
            var collider = go.GetComponent<Collider>();
            if (collider != null) Object.DestroyImmediate(collider);
        }

        private static Material BuildTiledMaterial(string name, Texture2D tex, Vector2 tiling)
        {
            var path = $"{MaterialDir}/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.SetTexture("_BaseMap", tex);
            mat.SetTextureScale("_BaseMap", tiling);
            mat.SetFloat("_Smoothness", 0.08f);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        /* ══════════════════════════════════════════════════════ 소품 */

        /// <summary>생활관 대표 소품 8개 — 관물대·침상·게시판·총기거치대·세면대·난로</summary>
        private static readonly PropSpec[] Props =
        {
            new PropSpec { File = "props/prop_13032.png", Position = new Vector3(-3.4f, 0f, -2.0f), Footprint = new Vector2(0.8f, 0.5f) }, // 관물대
            new PropSpec { File = "props/prop_13032.png", Position = new Vector3(-3.4f, 0f, 0.0f), Footprint = new Vector2(0.8f, 0.5f) },  // 관물대
            new PropSpec { File = "props/prop_38298.png", Position = new Vector3(-1.4f, 0f, 2.5f), Footprint = new Vector2(1.6f, 0.9f) },  // 침상
            new PropSpec { File = "props/prop_38298.png", Position = new Vector3(1.4f, 0f, 2.5f), Footprint = new Vector2(1.6f, 0.9f) },   // 침상
            new PropSpec { File = "props/prop_65561.png", Position = new Vector3(3.4f, 0f, 2.6f), Footprint = new Vector2(1.4f, 0.4f) },   // 게시판
            new PropSpec { File = "props/prop_72913.png", Position = new Vector3(0.0f, 0f, 2.6f), Footprint = new Vector2(1.4f, 0.4f) },   // 총기 거치대
            new PropSpec { File = "props/prop_01316.png", Position = new Vector3(3.4f, 0f, -2.0f), Footprint = new Vector2(0.8f, 0.5f) },  // 세면대
            new PropSpec { File = "props/prop_74646.png", Position = new Vector3(0.2f, 0f, -1.2f), Footprint = new Vector2(0.7f, 0.5f) },  // 난로
        };

        private static void BuildProps(Transform parent, Material blobMat)
        {
            foreach (var spec in Props)
            {
                var sprite = LoadSprite(spec.File);
                if (sprite == null)
                {
                    Debug.LogWarning($"[HD2D 목업] 소품 스프라이트 없음: {spec.File}");
                    continue;
                }

                var go = new GameObject(sprite.name);
                go.transform.SetParent(parent, false);
                go.transform.position = spec.Position;

                var renderer = go.AddComponent<SpriteRenderer>();
                renderer.sprite = sprite;
                go.AddComponent<Hd2dBillboard>();

                BuildBlobShadow(parent, spec.Position, spec.Footprint, blobMat);
            }
        }

        private static void BuildBlobShadow(Transform parent, Vector3 basePosition, Vector2 footprint,
                                            Material blobMat)
        {
            var blob = GameObject.CreatePrimitive(PrimitiveType.Quad);
            blob.name = "발밑그림자";
            StripCollider(blob);
            blob.transform.SetParent(parent, false);
            blob.transform.position = basePosition + new Vector3(0f, 0.008f, 0f);
            blob.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            blob.transform.localScale = new Vector3(footprint.x, footprint.y, 1f);
            blob.GetComponent<MeshRenderer>().sharedMaterial = blobMat;
        }

        private static Material BuildBlobMaterial()
        {
            const string path = MaterialDir + "/Hd2dMock_Blob.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.SetFloat("_Surface", 1f); // Transparent
            mat.SetFloat("_Blend", 0f);   // Alpha
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.SetInt("_ZWrite", 0);
            mat.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            mat.renderQueue = (int)RenderQueue.Transparent;
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.SetColor("_BaseColor", new Color(0f, 0f, 0f, 0.38f));
            EditorUtility.SetDirty(mat);
            return mat;
        }

        /* ══════════════════════════════════════════════════ 캐릭터 */

        private static readonly CharSpec[] Chars =
        {
            new CharSpec { Position = new Vector3(1.6f, 0f, 1.3f) },
            new CharSpec { Position = new Vector3(-2.6f, 0f, -0.6f) },
        };

        /// <summary>레이어 순서: 몸 → 하의 → 상의 → 머리(빌보드 앞쪽일수록 −Z)</summary>
        private static readonly (string file, float zOffset)[] CharLayers =
        {
            ("chars/body_skin0.png", 0.03f),
            ("chars/legs_field.png", 0.02f),
            ("chars/torso_field.png", 0.01f),
            ("chars/head_cap.png", 0.0f),
        };

        private static void BuildCharacters(Transform parent, Material blobMat)
        {
            foreach (var spec in Chars)
            {
                var go = new GameObject("병사");
                go.transform.SetParent(parent, false);
                go.transform.position = spec.Position;
                go.AddComponent<Hd2dBillboard>();

                foreach (var (file, zOffset) in CharLayers)
                {
                    var sprite = LoadCharCell(file, 0, 0); // idle_S, 프레임 0
                    if (sprite == null)
                    {
                        Debug.LogWarning($"[HD2D 목업] 캐릭터 시트 못 읽음: {file}");
                        continue;
                    }

                    var layer = new GameObject(sprite.name);
                    layer.transform.SetParent(go.transform, false);
                    layer.transform.localPosition = new Vector3(0f, 0f, -zOffset);
                    layer.AddComponent<SpriteRenderer>().sprite = sprite;
                }

                BuildBlobShadow(parent, spec.Position, new Vector2(0.5f, 0.35f), blobMat);
            }
        }

        private static Sprite LoadSprite(string file) =>
            AssetDatabase.LoadAssetAtPath<Sprite>($"{Art2DDir}/{file}");

        /// <summary>캐릭터 시트에서 (row, col) 셀 하나를 찾는다 — 이름 규칙은 `{파일}_{row}_{col}`</summary>
        private static Sprite LoadCharCell(string file, int row, int col)
        {
            var path = $"{Art2DDir}/{file}";
            var assets = AssetDatabase.LoadAllAssetsAtPath(path);
            if (assets == null) return null;

            var wanted = $"{Path.GetFileNameWithoutExtension(file)}_{row}_{col}";
            foreach (var asset in assets)
            {
                if (asset is Sprite sprite && sprite.name == wanted) return sprite;
            }
            return null;
        }

        /* ══════════════════════════════════════════════════════ 조명 */

        private struct LightSpec
        {
            public Vector3 Position;
            public Color Color;
            public float Intensity;
            public float Range;
        }

        private static readonly LightSpec[] LightSpecs =
        {
            new LightSpec { Position = new Vector3(0.2f, 1.6f, -1.2f), Color = new Color(1f, 0.65f, 0.35f), Intensity = 3.2f, Range = 4.5f }, // 난로
            new LightSpec { Position = new Vector3(0f, 1.8f, 1.2f), Color = new Color(1f, 0.85f, 0.65f), Intensity = 2.6f, Range = 5f },      // 중앙
            new LightSpec { Position = new Vector3(-3.0f, 1.7f, -1.0f), Color = new Color(0.8f, 0.85f, 1f), Intensity = 2.0f, Range = 4f },   // 관물대 쪽
        };

        private static List<Light> BuildLights(Transform parent)
        {
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.18f, 0.19f, 0.24f);
            RenderSettings.fog = false;

            var lights = new List<Light>();
            foreach (var spec in LightSpecs)
            {
                var go = new GameObject("Light_Point");
                go.transform.SetParent(parent, false);
                go.transform.position = spec.Position;

                var light = go.AddComponent<Light>();
                light.type = LightType.Point;
                light.color = spec.Color;
                light.intensity = spec.Intensity;
                light.range = spec.Range;
                // 실시간 섀도맵 금지(§ HD-2D 목업) — 소품 밑 블롭만 그림자 역할을 한다
                light.shadows = LightShadows.None;
                lights.Add(light);
            }
            return lights;
        }

        /* ══════════════════════════════════════════════════ 후처리 */

        private static Volume BuildPostVolume(Transform parent)
        {
            var path = $"{VolumeDir}/Hd2dMock_Post.asset";
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, path);
            }

            if (!profile.TryGet<Bloom>(out var bloom)) bloom = profile.Add<Bloom>(true);
            bloom.threshold.overrideState = true; bloom.threshold.value = 0.9f;
            bloom.intensity.overrideState = true; bloom.intensity.value = 0.25f;
            bloom.scatter.overrideState = true; bloom.scatter.value = 0.55f;
            bloom.highQualityFiltering.overrideState = true; bloom.highQualityFiltering.value = false;

            // 틸트시프트 흉내 — URP Gaussian DoF 저품질(§ HD-2D 목업 지시)
            if (!profile.TryGet<DepthOfField>(out var dof)) dof = profile.Add<DepthOfField>(true);
            dof.mode.overrideState = true; dof.mode.value = DepthOfFieldMode.Gaussian;
            dof.gaussianStart.overrideState = true; dof.gaussianStart.value = 6f;
            dof.gaussianEnd.overrideState = true; dof.gaussianEnd.value = 13f;
            dof.gaussianMaxRadius.overrideState = true; dof.gaussianMaxRadius.value = 0.9f;
            dof.highQualitySampling.overrideState = true; dof.highQualitySampling.value = false;

            if (!profile.TryGet<ColorAdjustments>(out var grading)) grading = profile.Add<ColorAdjustments>(true);
            grading.postExposure.overrideState = true; grading.postExposure.value = 0.05f;
            grading.contrast.overrideState = true; grading.contrast.value = 8f;
            grading.saturation.overrideState = true; grading.saturation.value = -6f;
            grading.colorFilter.overrideState = true; grading.colorFilter.value = new Color(1f, 0.97f, 0.9f);

            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();

            var go = new GameObject("후처리 볼륨");
            go.transform.SetParent(parent, false);
            var volume = go.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.sharedProfile = profile;
            return volume;
        }

        /* ══════════════════════════════════════════════════ 카메라·UI */

        private static Canvas BuildOverlayCanvas(Transform parent, out RawImage rawImage)
        {
            var canvasGo = new GameObject("Canvas_LowRes");
            canvasGo.transform.SetParent(parent, false);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            canvasGo.AddComponent<CanvasScaler>();
            canvasGo.AddComponent<GraphicRaycaster>();

            var imgGo = new GameObject("RawImage");
            imgGo.transform.SetParent(canvasGo.transform, false);
            rawImage = imgGo.AddComponent<RawImage>();
            rawImage.raycastTarget = false;
            var rt = rawImage.rectTransform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            return canvas;
        }

        private static Camera BuildCamera(Transform parent, Canvas overlayCanvas, RawImage rawImage,
                                          out Hd2dResolutionScaler scaler)
        {
            var go = new GameObject("Main Camera");
            go.tag = "MainCamera";
            go.transform.SetParent(parent, false);
            go.transform.position = new Vector3(0f, 5.5f, -6.5f);
            go.transform.rotation = Quaternion.Euler(38f, 0f, 0f); // 옥토패스 앵글(30~40° 틸트)

            var camera = go.AddComponent<Camera>();
            camera.orthographic = false;
            camera.fieldOfView = 42f;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 60f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.03f, 0.035f, 0.05f);

            go.AddComponent<AudioListener>();
            go.AddComponent<Hd2dCameraPan>();

            var camData = camera.GetUniversalAdditionalCameraData();
            camData.renderPostProcessing = true;
            camData.antialiasing = AntialiasingMode.None;

            scaler = go.AddComponent<Hd2dResolutionScaler>();
            scaler.targetCamera = camera;
            scaler.overlayCanvas = overlayCanvas;
            scaler.display = rawImage;
            scaler.scale = 0.66f;

            return camera;
        }

        /* ══════════════════════════════════════════════ 파이프라인 복제 */

        /// <summary>
        /// 본선 3D URP 렌더러/에셋을 Assets/Hd2dMock/Generated/Pipeline/ 아래로
        /// 복제하고, 복제본에만 postProcessData를 채운다. 원본은 절대 건드리지 않는다.
        /// </summary>
        private static UniversalRenderPipelineAsset BuildPipelineCopy()
        {
            var rendererDst = $"{PipelineDir}/Hd2dMock_Renderer.asset";
            var assetDst = $"{PipelineDir}/Hd2dMock_URPAsset.asset";

            if (AssetDatabase.LoadAssetAtPath<Object>(rendererDst) == null)
            {
                if (!AssetDatabase.CopyAsset(SourceUrpRendererPath, rendererDst))
                {
                    Debug.LogError($"[HD2D 목업] 렌더러 복제 실패: {SourceUrpRendererPath} → {rendererDst}");
                }
            }
            if (AssetDatabase.LoadAssetAtPath<Object>(assetDst) == null)
            {
                if (!AssetDatabase.CopyAsset(SourceUrpAssetPath, assetDst))
                {
                    Debug.LogError($"[HD2D 목업] URP 에셋 복제 실패: {SourceUrpAssetPath} → {assetDst}");
                }
            }
            AssetDatabase.Refresh();

            var rendererCopy = AssetDatabase.LoadAssetAtPath<ScriptableRendererData>(rendererDst);
            var postProcessData = AssetDatabase.LoadAssetAtPath<PostProcessData>(PackagePostProcessData);
            if (rendererCopy != null && postProcessData != null)
            {
                var so = new SerializedObject(rendererCopy);
                var prop = so.FindProperty("postProcessData");
                if (prop != null)
                {
                    prop.objectReferenceValue = postProcessData;
                    so.ApplyModifiedPropertiesWithoutUndo();
                    EditorUtility.SetDirty(rendererCopy);
                }
            }
            else
            {
                Debug.LogWarning("[HD2D 목업] postProcessData를 못 채웠다 — 후처리가 안 보일 수 있다");
            }

            var assetCopy = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(assetDst);
            if (assetCopy != null && rendererCopy != null)
            {
                var so = new SerializedObject(assetCopy);
                var list = so.FindProperty("m_RendererDataList");
                if (list != null && list.arraySize > 0)
                {
                    list.GetArrayElementAtIndex(0).objectReferenceValue = rendererCopy;
                    so.ApplyModifiedPropertiesWithoutUndo();
                    EditorUtility.SetDirty(assetCopy);
                }
            }

            AssetDatabase.SaveAssets();
            return assetCopy;
        }
    }
}
