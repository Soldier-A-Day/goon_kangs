using System.Collections.Generic;
using UnityEngine;

namespace SoldierADay.Net
{
    /// <summary>
    /// 저장소 최초의 소리 재생기 (A-2 · D-4 1단계 일부).
    ///
    /// ── 런타임 싱글턴 ─────────────────────────────────────────────────────
    /// 씬에 아무것도 놓지 않는다. 첫 `Play` 호출에서 `new GameObject`로 스스로를
    /// 만들고 `DontDestroyOnLoad`로 살아남는다 — 프리팹도 인스펙터 배선도 없다.
    ///
    /// ── 소스는 `Resources/Audio`에 있다 ──────────────────────────────────
    /// 씬 수정 없이 런타임에 클립을 찾으려면 `Resources.Load`뿐이고, 그게 되는
    /// 유일한 폴더가 `Resources`다. `tools/audio/gen_sfx.py`가 여기로 직접
    /// WAV를 생성한다.
    ///
    /// ── WebGL 오디오 잠금 ─────────────────────────────────────────────────
    /// 브라우저는 첫 사용자 제스처 전에는 오디오 컨텍스트를 열지 않는다. 재시도
    /// 큐 같은 건 안 만든다(과공학) — 잠긴 동안 들어온 요청은 그냥 버리고,
    /// 첫 입력이 들어온 프레임부터는 평범하게 재생된다. 그거면 충분하다.
    /// </summary>
    public sealed class Sfx : MonoBehaviour
    {
        private const int PoolSize = 6;
        private static readonly string[] Keys = { "tap", "success", "miss", "warning" };

        private static Sfx _instance;

        private readonly Dictionary<string, AudioClip> _clips = new Dictionary<string, AudioClip>();
        private AudioSource[] _pool;
        private int _next;
        private bool _unlocked;

        /// <summary>키 하나로 짧은 효과음 재생. 없는 키·잠긴 상태면 조용히 무시한다</summary>
        public static void Play(string key, float volume = 1f)
        {
            var self = Instance;
            if (!self._unlocked && !self.TryUnlock())
            {
                // 첫 유저 입력 전이다 — 이번 요청은 버린다. 재시도하지 않는다
                return;
            }

            self.PlayClip(key, volume);
        }

        private static Sfx Instance
        {
            get
            {
                if (_instance != null) return _instance;
                var go = new GameObject("Sfx");
                Object.DontDestroyOnLoad(go);
                _instance = go.AddComponent<Sfx>();
                _instance.Init();
                return _instance;
            }
        }

        private void Init()
        {
            _pool = new AudioSource[PoolSize];
            for (var i = 0; i < PoolSize; i += 1)
            {
                var src = gameObject.AddComponent<AudioSource>();
                src.playOnAwake = false;
                src.spatialBlend = 0f;
                src.loop = false;
                _pool[i] = src;
            }

            foreach (var key in Keys)
            {
                var clip = Resources.Load<AudioClip>($"Audio/{key}");
                if (clip != null) _clips[key] = clip;
            }
        }

        /// <summary>실제 유저 입력이 있었는지 본다. 있었으면 그 프레임부터 잠금 해제</summary>
        private bool TryUnlock()
        {
            if (!Input.anyKeyDown && !Input.GetMouseButtonDown(0) &&
                !Input.GetMouseButtonDown(1) && Input.touchCount == 0)
            {
                return false;
            }

            _unlocked = true;
            return true;
        }

        private void PlayClip(string key, float volume)
        {
            if (!_clips.TryGetValue(key, out var clip) || clip == null) return;

            var src = _pool[_next];
            _next = (_next + 1) % _pool.Length;
            src.pitch = 1f;
            src.PlayOneShot(clip, Mathf.Clamp01(volume));
        }
    }
}
