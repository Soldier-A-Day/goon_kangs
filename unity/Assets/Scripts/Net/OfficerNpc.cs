using UnityEngine;

namespace SoldierADay.Net
{
    /// <summary>
    /// §E3 보직별 간부 NPC — 서 있기만 하는 연출용 인물의 런타임 자기수복.
    ///
    /// `CharacterRig._library`·`_clip`은 직렬화되지 않는 사설 필드다(§5.2). 씬
    /// 빌더(`BaseScene.BuildOfficerNpcs`)가 씬을 만드는 그 순간에는 `Bind`·
    /// `SetLook`·`Play`를 걸어 두지만, 그 값은 씬 파일에 남지 않는다 — 씬을
    /// 저장하고 실제로 다시 열면(에디터 재생, WebGL 부팅) 전부 빈 상태로
    /// 되돌아간다.
    ///
    /// 플레이어는 `ZoneWorld.Apply`가 첫 스냅샷에서 다시 입혀 주고, 분대원은
    /// `SquadView.Spawn`이 그때그때 새로 만들어 문제가 없다. 이 간부들은 서버
    /// 스냅샷을 안 타므로 그 경로에 얹힐 수 없다 — 그래서 직접 `Start`에서
    /// 한 번 더 입는다. 그 이후로는 아무것도 하지 않는다(§요구 5·6 "걷지
    /// 않는다" · "매 프레임 도는 코드를 새로 만들지 마라").
    /// </summary>
    [RequireComponent(typeof(CharacterRig))]
    public sealed class OfficerNpc : MonoBehaviour
    {
        public string role;
        public string rank;

        private void Start()
        {
            var library = FindFirstObjectByType<SpriteLibrary>();
            if (library == null) return;

            var rig = GetComponent<CharacterRig>();
            rig.Bind(library);
            rig.SetLook(role, rank);
            rig.Play("idle");
        }
    }
}
