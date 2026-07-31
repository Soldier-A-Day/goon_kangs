using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SoldierADay.EditorTools
{
    /// <summary>M0_Real 씬에 무엇이 어디에 있는지 실제로 확인한다. 추측 대신 값을 본다.</summary>
    public static class InspectReal
    {
        public static void Run()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/M0_Real.unity");
            var camera = Object.FindFirstObjectByType<Camera>();

            foreach (var skinned in Object.FindObjectsByType<SkinnedMeshRenderer>(FindObjectsSortMode.None))
            {
                var b = skinned.bounds;
                var local = skinned.localBounds;
                var onScreen = camera != null
                    ? GeometryUtility.TestPlanesAABB(GeometryUtility.CalculateFrustumPlanes(camera), b)
                    : false;
                var screen = camera != null ? camera.WorldToScreenPoint(b.center) : Vector3.zero;

                Debug.Log(
                    $"[검사] {skinned.transform.parent?.name}/{skinned.name}\n" +
                    $"  월드 바운드 중심 {b.center} 크기 {b.size}\n" +
                    $"  로컬 바운드 중심 {local.center} 크기 {local.size}\n" +
                    $"  루트본 {(skinned.rootBone != null ? skinned.rootBone.name : "없음")} " +
                    $"@ {(skinned.rootBone != null ? skinned.rootBone.position.ToString() : "-")}\n" +
                    $"  절두체 안 {onScreen} · 화면좌표 {screen}");
            }

            if (camera != null)
            {
                Debug.Log($"[검사] 카메라 {camera.transform.position} 회전 {camera.transform.eulerAngles} FOV {camera.fieldOfView} · 해상도 {Screen.width}x{Screen.height}");
            }
            EditorApplication.Exit(0);
        }
    }
}
