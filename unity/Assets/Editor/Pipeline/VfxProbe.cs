using System.IO;
using SoldierADay.Net;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SoldierADay.EditorTools
{
    /// <summary>
    /// 파티클 12종을 실제로 뿌려본다 (SAD-ART-001 §9.1).
    ///
    /// 파티클은 **씬에 있어도 안 보일 수 있다.** 방출기가 꺼져 있거나,
    /// 정렬 순서가 지면 뒤이거나, 머티리얼 텍스처가 안 붙었거나, 알갱이가
    /// 화면 밖에서만 나거나 — 전부 로그 없이 조용하다. 그래서 하나씩 켜고 찍는다.
    ///
    ///     Unity -batchmode -projectPath unity \
    ///       -executeMethod SoldierADay.EditorTools.VfxProbe.Run
    /// </summary>
    public static class VfxProbe
    {
        private const string Flag = "sad.vfxprobe";
        private const string OutDir = "/tmp/sad_vfx";

        private static double _entered;
        private static int _shot = -1;
        private static bool _captured;

        private static readonly string[] Shots =
        {
            "00_off", "01_snow_light", "02_snow_heavy", "03_rain",
            "04_haze", "05_breath", "06_sweat", "07_dust",
            "08_complete", "09_muzzle", "10_collapse", "11_decon",
        };

        public static void Run()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/Base.unity");
            SessionState.SetBool(Flag, true);
            EditorApplication.EnterPlaymode();
        }

        [InitializeOnLoadMethod]
        private static void Hook()
        {
            if (!SessionState.GetBool(Flag, false)) return;
            if (!EditorApplication.isPlayingOrWillChangePlaymode) return;

            _entered = EditorApplication.timeSinceStartup;
            _shot = -1;
            Directory.CreateDirectory(OutDir);
            EditorApplication.update += Tick;
        }

        /// <summary>사건 파티클은 수명이 짧다 — 터진 직후에 찍어야 한다</summary>
        private static bool IsBurst(string shot) =>
            shot.Contains("complete") || shot.Contains("muzzle") ||
            shot.Contains("collapse") || shot.Contains("decon");

        private static void Tick()
        {
            var vfx = Object.FindFirstObjectByType<Vfx>();
            if (vfx == null) return;

            // 스냅샷이 10Hz로 방출기를 다시 정한다 — 검사 중에는 컴포넌트를 끈다.
            //
            // 끄면 `LateUpdate`도 멈추므로 **입김·땀을 여기서 직접 옮겨야 한다.**
            // 처음엔 그걸 빼먹어서 둘이 월드 원점에 남았고, 부대는 (53, 25)쯤에
            // 있으니 화면 밖에서 나고 있었다 — 사진에는 아무것도 없었다.
            vfx.enabled = false;
            if (vfx.follow != null)
            {
                var head = vfx.follow.position + new Vector3(0f, 1.2f, 0f);
                if (vfx.breath != null) vfx.breath.transform.position = head;
                if (vfx.sweat != null) vfx.sweat.transform.position = head;
            }

            var elapsed = EditorApplication.timeSinceStartup - _entered;

            // 알갱이가 화면에 들어차려면 시간이 든다. 켜자마자 찍으면
            // 눈이 화면 위 한 줄에만 있다.
            //
            // 3초까지 늘린 것은 **입김 때문**이다 — §9.1이 "2초 주기"로 못박아서
            // 2초 창에서는 한 번도 안 나온 채로 찍혔다
            const double Window = 3.0;
            var index = Mathf.FloorToInt((float)((elapsed - 2.0) / Window));
            if (index < 0) return;
            if (index >= Shots.Length)
            {
                SessionState.SetBool(Flag, false);
                Debug.Log($"[vfx] {Shots.Length}장 → {OutDir}");
                EditorApplication.Exit(0);
                return;
            }

            if (index != _shot)
            {
                _shot = index;
                _captured = false;
                Setup(vfx, index);
                return;
            }

            if (_captured) return;

            // 지속 파티클은 화면에 들어차야 하므로 늦게, 사건 파티클은 사라지기
            // 전에 찍어야 하므로 일찍. 총구 화염은 수명이 0.08초다
            var at = (elapsed - 2.0) - index * Window;
            var when = IsBurst(Shots[index]) ? 0.05 : Window * 0.85;
            if (at < when) return;

            _captured = true;

            var alive = "";
            foreach (var (name, system) in Systems(vfx))
            {
                if (system == null || system.particleCount == 0) continue;
                var buffer = new ParticleSystem.Particle[system.particleCount];
                var n = system.GetParticles(buffer);
                var lo = float.MaxValue; var hi = float.MinValue;
                for (var i = 0; i < n; i += 1)
                {
                    var wy = system.main.simulationSpace == ParticleSystemSimulationSpace.World
                        ? buffer[i].position.y
                        : system.transform.TransformPoint(buffer[i].position).y;
                    lo = Mathf.Min(lo, wy); hi = Mathf.Max(hi, wy);
                }
                alive += $"{name}:{n}(y {lo:F1}~{hi:F1} 크기{buffer[0].GetCurrentSize(system):F2}) ";
            }
            var cam = Camera.main;
            var view = cam != null
                ? $"화면 y {cam.transform.position.y - CameraRig.OrthoSize:F1}~{cam.transform.position.y + CameraRig.OrthoSize:F1}"
                : "카메라 없음";
            Debug.Log($"[vfx] {Shots[index]} [{view}] — {(alive == "" ? "살아있는 알갱이 없음" : alive)}");

            Capture($"{OutDir}/{Shots[index]}.png");
        }

        private static (string, ParticleSystem)[] Systems(Vfx vfx) => new[]
        {
            ("눈", vfx.snowLight), ("눈보라", vfx.snowHeavy), ("비", vfx.rain),
            ("아지랑이", vfx.heatHaze), ("입김", vfx.breath), ("땀", vfx.sweat),
            ("먼지", vfx.dust), ("완료", vfx.questComplete), ("화염", vfx.muzzleFlash),
            ("쓰러짐", vfx.collapse), ("제독", vfx.decon),
        };

        private static void Setup(Vfx vfx, int index)
        {
            var all = new[]
            {
                vfx.snowLight, vfx.snowHeavy, vfx.rain, vfx.heatHaze,
                vfx.breath, vfx.sweat, vfx.dust,
            };
            foreach (var system in all)
            {
                if (system == null) continue;
                var emission = system.emission;
                emission.enabled = false;
            }

            ParticleSystem on = Shots[index] switch
            {
                "01_snow_light" => vfx.snowLight,
                "02_snow_heavy" => vfx.snowHeavy,
                "03_rain" => vfx.rain,
                "04_haze" => vfx.heatHaze,
                "05_breath" => vfx.breath,
                "06_sweat" => vfx.sweat,
                "07_dust" => vfx.dust,
                _ => null,
            };

            if (on != null)
            {
                var emission = on.emission;
                emission.enabled = true;
                if (!on.isPlaying) on.Play();
            }

            // 사건 계열은 카메라 앞에서 터뜨린다 — 원래 자리는 훈련 씬이라 여기 없다
            var camera = Camera.main;
            var at = camera != null ? camera.transform.position + Vector3.forward * 10f : Vector3.zero;
            switch (Shots[index])
            {
                case "08_complete": vfx.Burst(vfx.questComplete, at); break;
                case "09_muzzle": vfx.Fire(at); break;
                case "10_collapse": vfx.Collapsed(at); break;
                case "11_decon": vfx.Decontaminate(at); break;
            }
        }

        private static void Capture(string path)
        {
            var camera = Camera.main;
            if (camera == null) return;

            var rt = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32);
            camera.targetTexture = rt;
            camera.Render();
            RenderTexture.active = rt;
            var image = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
            image.Apply();
            camera.targetTexture = null;
            RenderTexture.active = null;
            File.WriteAllBytes(path, image.EncodeToPNG());
        }
    }
}
