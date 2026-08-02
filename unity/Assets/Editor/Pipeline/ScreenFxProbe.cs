using System.IO;
using SoldierADay.Net;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SoldierADay.EditorTools
{
    /// <summary>
    /// 풀스크린 셰이더 6종을 실제로 그려본다 (SAD-ART-001 §9.2).
    ///
    /// 셰이더는 **컴파일이 통과해도 아무것도 안 그릴 수 있다.** 렌더 그래프에서
    /// 패스가 큐에 안 들어가거나, 머티리얼이 비어 있거나, 블릿 대상이 백버퍼여서
    /// 조용히 건너뛰어도 로그에는 한 줄도 안 나온다. 그래서 강도를 1로 박고
    /// 한 장씩 찍어 눈으로 본다.
    ///
    ///     Unity -batchmode -projectPath unity \
    ///       -executeMethod SoldierADay.EditorTools.ScreenFxProbe.Run
    ///
    /// `-nographics`로는 못 돈다 — 화면을 안 그리는 모드에서 화면 효과를 검사할 수는 없다.
    /// </summary>
    public static class ScreenFxProbe
    {
        private const string Flag = "sad.fxprobe";
        private const string OutDir = "/tmp/sad_fx";

        private static double _entered;
        private static int _shot = -1;
        private static bool _captured;
        private static float _haze;

        /// <summary>(파일 이름, 그 효과를 켜는 법)</summary>
        private static readonly string[] Shots =
        {
            "00_off", "01_heat", "02_frost", "03_mask",
            "04_night", "05_pulse", "06_fade", "07_all",
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

        private static void Tick()
        {
            var effects = Object.FindFirstObjectByType<ScreenEffects>();
            var grading = Object.FindFirstObjectByType<WeatherGrading>();
            if (effects == null || grading == null) return;

            // 서버 스냅샷이 값을 덮어쓰지 않게 컴포넌트를 떼어 놓는다 —
            // 여기서 보려는 것은 셰이더이지 상태 전달이 아니다.
            //
            // `ScreenEffects`는 못 끈다(그게 머티리얼을 채우는 쪽이다). 대신
            // 아지랑이 값을 **매 프레임 다시 민다** — 스냅샷이 10Hz로 들어와
            // 밴드에서 다시 계산해버리기 때문이다. 한 번만 넣었더니 그림이
            // 하나도 안 바뀌었고, 셰이더가 아니라 이 덮어쓰기가 원인이었다.
            grading.enabled = false;
            SetPrivate(effects, "_heatHaze", _haze);

            var elapsed = EditorApplication.timeSinceStartup - _entered;

            // 한 장에 1.5초. **켠 직후에 찍으면 안 된다** — 방독면·야시장비는
            // 부드럽게 넘어오도록 시간을 두고 보간되므로(§9.2), 다음 프레임에
            // 찍으면 아직 0에 가깝다. 실제로 그렇게 찍어서 아무것도 안 나왔다.
            // 창을 열자마자 설정하고, 다 넘어온 뒤인 끝에서 찍는다.
            const double Window = 1.5;
            var index = Mathf.FloorToInt((float)((elapsed - 2.0) / Window));
            if (index < 0) return;
            if (index >= Shots.Length)
            {
                SessionState.SetBool(Flag, false);
                Debug.Log($"[fx] {Shots.Length}장 → {OutDir}");
                EditorApplication.Exit(0);
                return;
            }

            if (index != _shot)
            {
                _shot = index;
                _captured = false;
                Setup(effects, grading, index);
                return;
            }

            // 창의 끝자락 — 보간이 다 끝난 뒤
            if (_captured) return;
            if ((elapsed - 2.0) - index * Window < Window * 0.8) return;
            _captured = true;

            var slots = "";
            for (var i = 0; i < ScreenEffectsFeature.SlotCount; i += 1)
            {
                var m = ScreenEffectsFeature.Materials[i];
                slots += m == null ? "· " : $"{i}:{m.GetFloat("_Strength"):F2}/{m.GetFloat("_Amount"):F2}/{m.GetFloat("_Coverage"):F2} ";
            }
            // 켜진 칸을 같이 적는다. 그림이 안 바뀌었을 때 "셰이더가 이상한가"와
            // "애초에 안 켰나"를 가르는 것이 이 한 줄이다
            Debug.Log($"[fx] {Shots[index]} — {slots}");

            Capture($"{OutDir}/{Shots[index]}.png");
        }

        private static void Setup(ScreenEffects effects, WeatherGrading grading, int index)
        {
            // 지난 장의 설정을 끈다
            effects.maskOn = false;
            effects.nightVision = false;
            effects.fadeOut = 0f;
            SetPrivate(grading, "<HeatStress>k__BackingField", 0f);
            SetPrivate(grading, "<FrostBite>k__BackingField", 0f);

            switch (Shots[index])
            {
                case "01_heat": SetPrivate(grading, "<HeatStress>k__BackingField", 0f); break;
                case "02_frost": SetPrivate(grading, "<FrostBite>k__BackingField", 1f); break;
                case "03_mask": effects.maskOn = true; break;
                case "04_night": effects.nightVision = true; break;
                case "05_pulse": SetPrivate(grading, "<HeatStress>k__BackingField", 1f); break;
                case "06_fade": effects.fadeOut = 0.85f; break;
                case "07_all":
                    effects.maskOn = true;
                    SetPrivate(grading, "<FrostBite>k__BackingField", 0.7f);
                    break;
            }

            // 아지랑이는 밴드가 만든다(혹서 이상). 여기서는 직접 민다
            _haze = Shots[index] == "01_heat" || Shots[index] == "07_all" ? 1f : 0f;

        }

        private static void SetPrivate(object target, string field, float value)
        {
            var info = target.GetType().GetField(field,
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Public);
            info?.SetValue(target, value);
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
