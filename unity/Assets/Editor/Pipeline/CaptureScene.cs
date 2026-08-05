using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SoldierADay.EditorTools
{
    /// <summary>
    /// 씬을 실제로 렌더해 PNG로 남긴다.
    ///
    /// "안 보인다"의 원인은 컬링·위치·크기·재질 어느 쪽이든 될 수 있고,
    /// 로그로는 구분이 안 된다. 그림 한 장이면 어느 쪽인지 바로 갈린다.
    /// </summary>
    public static class CaptureScene
    {
        public static void Run()
        {
            var scenePath = Arg("-scene") ?? "Assets/Scenes/M0_Real.unity";
            var outPath = Arg("-out") ?? "/tmp/m0real.png";
            var width = int.Parse(Arg("-width") ?? "1280");
            var height = int.Parse(Arg("-height") ?? "800");

            EditorSceneManager.OpenScene(scenePath);
            var camera = Object.FindFirstObjectByType<Camera>();
            if (camera == null) { Debug.LogError("[캡처] 카메라 없음"); EditorApplication.Exit(1); return; }

            // 측정용 카메라는 씬에 박혀 있지만, 무엇이 서 있는지 보려면 가까이 가야 한다.
            // 측정과 확인은 다른 일이므로 씬을 고치지 않고 이 자리에서만 옮긴다.
            var pos = Arg("-pos");
            var look = Arg("-look");
            if (pos != null) camera.transform.position = ParseVec(pos);
            if (look != null) camera.transform.rotation = Quaternion.LookRotation(ParseVec(look) - camera.transform.position);

            // 2D 씬용. 직교 반높이를 키우면 부대 전체가 한 장에 들어온다
            var ortho = Arg("-ortho");
            if (ortho != null) camera.orthographicSize = float.Parse(ortho);

            // B2 전수 점검 전용 — `WorldLighting`/`WeatherGrading`은 [ExecuteAlways]가
            // 아니라서 Update/LateUpdate가 Edit 모드(-batchmode)에서는 아예 안 돈다.
            // 즉 아무 플래그 없이 캡처하면 씬 빌더가 심어둔 **기준 세기 그대로**(낮/밤
            // 게이팅 미적용) 찍힌다 — 사실상 "실내등도 항상 켜진" 상태다.
            // 실제 낮 플레이 화면과 비교하려면 이 스위치로 그 게이팅을 흉내 내야 한다.
            //   -simulateDay  : nightBoost=true(방·복도 등)만 끈다 — 실제 낮 게임 화면과 동일
            //   -lightsOff    : 로컬 광원을 전부 끈다 — WorldLighting 전체 토글 OFF와 동일
            // B2 진단 — 전역광(Global Light2D) 자체가 렌더에 기여하는지 격리 테스트.
            // 로컬 Point 광원 ON/OFF가 픽셀 단위로 완전히 같게 나와서(§HANDOFF 참고),
            // 2D 라이팅 파이프라인 자체가 죽었는지(전역광도 무효) 아니면 Point 광원만
            // 문제인지 갈라야 한다.
            var globalOff = HasFlag("-globalOff");
            var globalIntensity = Arg("-globalIntensity");
            if (globalOff || globalIntensity != null)
            {
                foreach (var l in Object.FindObjectsByType<UnityEngine.Rendering.Universal.Light2D>(FindObjectsSortMode.None))
                {
                    if (l.lightType != UnityEngine.Rendering.Universal.Light2D.LightType.Global) continue;
                    if (globalOff) l.enabled = false;
                    if (globalIntensity != null) l.intensity = float.Parse(globalIntensity);
                }
            }

            var simulateDay = HasFlag("-simulateDay");
            // **배치 캡처의 함정**: URP `Light2D`는 `LateUpdate`에서만 `UpdateMesh()`를
            // 부른다(패키지 Light2D.cs). 배치모드는 플레이 모드가 아니라 `LateUpdate`가
            // 돌지 않아 **Point 광원 메시가 아예 안 만들어지고**, 그래서 캡처에서만
            // 로컬 조명이 통째로 사라진다 — 실제 게임(런타임)과 다른 결과가 나온다.
            // `-bakeLights`는 그 내부 메서드를 리플렉션으로 한 번 돌려 런타임과 같은
            // 상태를 만든다. 진단 전용이며 씬을 저장하지 않는다.
            if (HasFlag("-bakeLights"))
            {
                var baked = 0;
                foreach (var l in Object.FindObjectsByType<UnityEngine.Rendering.Universal.Light2D>(FindObjectsSortMode.None))
                {
                    if (l.lightType == UnityEngine.Rendering.Universal.Light2D.LightType.Global) continue;
                    var t = l.GetType();
                    var um = t.GetMethod("UpdateMesh", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    var ub = t.GetMethod("UpdateBoundingSphere", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    if (um != null) um.Invoke(l, new object[] { true });
                    if (ub != null) ub.Invoke(l, null);
                    baked += 1;
                }
                Debug.Log($"[캡처] 광원 메시 강제 생성 {baked}개");
            }

            var lightsOff = HasFlag("-lightsOff");
            if (simulateDay || lightsOff)
            {
                var worldLighting = Object.FindFirstObjectByType<SoldierADay.Net.WorldLighting>();
                if (worldLighting == null)
                {
                    Debug.LogWarning("[캡처] WorldLighting 없음 — 조명 시뮬레이션 건너뜀");
                }
                else
                {
                    for (var i = 0; i < worldLighting.lights.Length; i += 1)
                    {
                        var light = worldLighting.lights[i];
                        if (light == null) continue;
                        if (lightsOff) { light.enabled = false; continue; }
                        var boost = i < worldLighting.nightBoost.Length && worldLighting.nightBoost[i];
                        light.enabled = !boost;
                    }
                }
            }

            // B2 진단 전용 — 라이트 ON/OFF가 렌더에 전혀 영향을 안 주는 것으로 보여서
            // (동일 좌표·동일 ortho 캡처 3장이 픽셀 단위로 완전히 같았다) 원인 후보를
            // 로그로 남긴다: 광원 트랜스폼·반경·활성 여부, 그리고 실제로 셰이더가
            // Lit으로 바뀌었는지(재질 이름). 씬은 건드리지 않고 읽기만 한다.
            if (HasFlag("-diagLighting"))
            {
                var worldLighting = Object.FindFirstObjectByType<SoldierADay.Net.WorldLighting>();
                if (worldLighting == null)
                {
                    Debug.Log("[진단] WorldLighting 없음");
                }
                else
                {
                    Debug.Log($"[진단] lights.Length={worldLighting.lights.Length}");
                    var filter = Arg("-diagZone");
                    for (var i = 0; i < worldLighting.lights.Length; i += 1)
                    {
                        var l = worldLighting.lights[i];
                        if (l == null) { Debug.Log($"[진단] light[{i}] = null"); continue; }
                        if (filter != null && !l.name.Contains(filter)) continue;
                        Debug.Log($"[진단] light[{i}] name={l.name} pos={l.transform.position} " +
                                  $"enabled={l.enabled} intensity={l.intensity} type={l.lightType} " +
                                  $"inner={l.pointLightInnerRadius} outer={l.pointLightOuterRadius} " +
                                  $"blendStyle={l.blendStyleIndex} sortingLayers=[{string.Join(",", l.targetSortingLayers)}] " +
                                  $"color={l.color}");
                    }
                }

                var grid = GameObject.Find("Grid");
                if (grid != null)
                {
                    var wallFace = grid.transform.Find("TM_WallFace");
                    if (wallFace != null)
                    {
                        var r = wallFace.GetComponentInChildren<SpriteRenderer>();
                        if (r != null)
                            Debug.Log($"[진단] WallFace 재질={r.sharedMaterial?.name} 셰이더={r.sharedMaterial?.shader?.name} " +
                                      $"sortingLayer={r.sortingLayerName}");
                    }
                    foreach (var tr in grid.GetComponentsInChildren<UnityEngine.Tilemaps.TilemapRenderer>(true))
                    {
                        Debug.Log($"[진단] Tilemap={tr.name} 재질={tr.sharedMaterial?.name} " +
                                  $"셰이더={tr.sharedMaterial?.shader?.name} sortingLayer={tr.sortingLayerName}");
                    }
                }

                var zoneContainer = GameObject.Find("구역");
                if (zoneContainer != null)
                {
                    var r = zoneContainer.GetComponentInChildren<SpriteRenderer>();
                    if (r != null)
                        Debug.Log($"[진단] 구역 소품 샘플={r.name} 재질={r.sharedMaterial?.name} " +
                                  $"셰이더={r.sharedMaterial?.shader?.name} sortingLayer={r.sortingLayerName}");
                }
            }

            var rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);

            // SRP에서 Camera.Render()는 지원되지 않는 레거시 경로라 결과가 실행마다
            // 흔들린다 — 정렬이 뒤집히고 색이 이중 감마로 어두워지는 "유령 버그"를
            // 이걸로 몇 시간 쫓았다. 실제 파이프라인을 태우는 정식 API로 그린다.
            var request = new UnityEngine.Rendering.RenderPipeline.StandardRequest { destination = rt };
            if (UnityEngine.Rendering.RenderPipeline.SupportsRenderRequest(camera, request))
            {
                UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(camera, request);
            }
            else
            {
                camera.targetTexture = rt;
                camera.Render();
            }

            RenderTexture.active = rt;
            var image = new Texture2D(width, height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            image.Apply();

            camera.targetTexture = null;
            RenderTexture.active = null;

            File.WriteAllBytes(outPath, image.EncodeToPNG());
            Debug.Log($"[캡처] {scenePath} → {outPath} ({width}x{height})");
            EditorApplication.Exit(0);
        }

        private static Vector3 ParseVec(string text)
        {
            var parts = text.Split(',');
            return new Vector3(float.Parse(parts[0]), float.Parse(parts[1]), float.Parse(parts[2]));
        }

        private static string Arg(string key)
        {
            var args = System.Environment.GetCommandLineArgs();
            for (var i = 0; i < args.Length - 1; i += 1) if (args[i] == key) return args[i + 1];
            return null;
        }

        /// <summary>값 없는 부울 스위치(예: `-simulateDay`)용. `Arg`는 다음 토큰을 값으로
        /// 소비하므로 이런 플래그에는 못 쓴다</summary>
        private static bool HasFlag(string key)
        {
            var args = System.Environment.GetCommandLineArgs();
            foreach (var a in args) if (a == key) return true;
            return false;
        }
    }
}
