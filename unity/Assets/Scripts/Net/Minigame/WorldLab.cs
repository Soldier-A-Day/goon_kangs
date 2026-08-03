using SoldierADay.Protocol;
using UnityEngine;

namespace SoldierADay.Net
{
    /// <summary>
    /// 월드 실험대 (`docs/WORKORDER.md` T단계).
    ///
    /// **T단계의 문제**: 구역 25개를 다 밟아 보려면 걸어서 가야 하고, 날씨 6밴드 ×
    /// 주야를 다 보려면 서버가 그 기온을 굴려줄 때까지 기다려야 하고, 캐릭터
    /// 보직 4종 × 계급 4종 조합을 다 보려면 그만큼의 계정이 필요하다.
    ///
    /// 여기서는 F11로 그 벽을 건너뛴다 — 구역은 순간이동, 날씨·주야는 표시만
    /// 덮어쓰고, VFX·SFX는 그 자리에서 바로 쏜다.
    ///
    /// ── 서버로 나가는 것은 구역 이동뿐이다 ─────────────────────────────────
    /// 구역 목록을 클릭하면 `LocalPlayer`를 그 자리로 순간이동시킨다 — 그 뒤는
    /// `ZoneWorld.Update()`가 실전과 똑같이 발밑 구역을 다시 읽어 `client.Move`를
    /// 부른다(§소유권, `ZoneWorld.cs`는 건드리지 않는다). 랩이 직접 `Move`를
    /// 보내지 않는 이유가 이것이다 — 실전 경로를 그대로 태워야 서버 상태와
    /// 어긋나지 않는다.
    ///
    /// ── 날씨·주야 오버라이드는 표시만 바꾼다 ────────────────────────────────
    /// 서버가 굴린 진짜 밴드·시간대는 `Snapshot.weather`·`phase`에 그대로 남아
    /// 있다(안 건드린다). 여기서 만지는 것은 `WeatherGrading`이 화면에 입히는
    /// Volume 가중치·주야 오버레이뿐이고, 오버라이드 중에는 배지를 반드시
    /// 띄운다 — 안 그러면 "지금 진짜 한파인가?"를 화면만으로 구분할 수 없다.
    /// 오버라이드를 끄면 다음 프레임부터 `WeatherGrading.Update()`가 다시
    /// 진짜 값으로 되돌린다(거기 있는 로직은 한 줄도 안 건드렸다).
    ///
    /// ── 캐릭터 뷰어는 내 캐릭터를 빌린다 ────────────────────────────────────
    /// 미리보기 전용 인형을 새로 세우지 않고, 지금 조작 중인 `LocalPlayer.Rig`에
    /// `SetLook`·`SetDistress`(둘 다 실물 코드, `CharacterRig.cs`)를 직접 걸어
    /// 잠깐 다른 보직·상태로 보이게 한다. 다음 스냅샷이 오면 `ZoneWorld.Apply`가
    /// 매번 내 진짜 보직으로 다시 씌우므로(10Hz) 랩을 닫으면 자연히 원래대로
    /// 돌아간다 — 되돌리는 코드를 따로 안 짜도 된다.
    ///
    /// ── 소유권 ───────────────────────────────────────────────────────────
    /// 이 파일이 월드 실험대의 전부다. `ZoneWorld`·`WeatherGrading`·`Vfx`·
    /// `CharacterRig`·`Sfx`는 공개 필드·메서드만 읽고 부를 뿐 손대지 않는다.
    /// </summary>
    public static class WorldLab
    {
        private enum Tab { Zones, Weather, Vfx, Character, Sound }

        private static bool _open;
        private static int _lastFrame = -1;
        private static Tab _tab = Tab.Zones;

        private static SoldierADay.Net.Vfx _vfx;
        private static bool _vfxSearched;

        public static bool IsOpen => _open;

        public static void Draw(HudTheme theme, Hud hud)
        {
            var freshFrame = Time.frameCount != _lastFrame;
            if (freshFrame) _lastFrame = Time.frameCount;

            // F11은 macOS가 미션 컨트롤류로 가로채 브라우저까지 안 내려오는
            // 환경이 흔하다 — F7을 정식 키로 얹고 F11도 그대로 받는다
            if (freshFrame && (Input.GetKeyDown(KeyCode.F11) || Input.GetKeyDown(KeyCode.F7))) Toggle();
            if (!_open) return;

            if (freshFrame) HandleKeys();

            // 날씨 오버라이드는 실전 `WeatherGrading.Update()`가 매 프레임 값을
            // 되돌리려 하므로, 여기서도 매 프레임 다시 덮어써야 유지된다 —
            // OnGUI가 그 뒤에 도니 이번 프레임 렌더에는 늦어도 다음 프레임부터는
            // 계속 이긴다(±1프레임 지연은 배지가 켜져 있으니 무해하다)
            if (_weatherOverride) ApplyWeatherOverride(hud);

            DrawPanel(theme, hud);
        }

        private static void Toggle()
        {
            _open = !_open;
        }

        private static void HandleKeys()
        {
            if (Input.GetKeyDown(KeyCode.Alpha1)) _tab = Tab.Zones;
            if (Input.GetKeyDown(KeyCode.Alpha2)) _tab = Tab.Weather;
            if (Input.GetKeyDown(KeyCode.Alpha3)) _tab = Tab.Vfx;
            if (Input.GetKeyDown(KeyCode.Alpha4)) _tab = Tab.Character;
            if (Input.GetKeyDown(KeyCode.Alpha5)) _tab = Tab.Sound;
        }

        /* ──────────────────────────────────────────────────────── 그리기 */

        private static void DrawPanel(HudTheme theme, Hud hud)
        {
            var full = new Rect(0f, 0f, HudTheme.ViewWidth, HudTheme.ViewHeight);
            theme.Fill(full, HudTheme.Dim, 0.75f);

            var margin = 36f;
            var panel = new Rect(margin, margin,
                HudTheme.ViewWidth - margin * 2f, HudTheme.ViewHeight - margin * 2f);
            theme.Fill(panel, HudTheme.Paper2);
            theme.Border(panel, HudTheme.Accent, 3f);

            const float headerH = 48f;
            theme.Header(panel, headerH);
            GUI.Label(new Rect(panel.x + 16f, panel.y + 8f, 500f, 32f),
                "WorldLab — 월드 실험대  [F7/F11]",
                theme.At(theme.Title, 22, HudTheme.Ink));
            GUI.Label(new Rect(panel.xMax - 420f, panel.y + 14f, 404f, 24f),
                "[1~5] 탭 전환",
                theme.At(theme.Label, 13, HudTheme.Ink2, TextAnchor.MiddleRight));

            var top = panel.y + headerH + 16f;

            var tabsY = top;
            var tabs = new (Tab tab, string label)[]
            {
                (Tab.Zones, "1. 구역 텔레포트"), (Tab.Weather, "2. 날씨 · 주야"),
                (Tab.Vfx, "3. VFX 발사"), (Tab.Character, "4. 캐릭터 뷰어"), (Tab.Sound, "5. 소리 시청"),
            };
            var tabX = panel.x + 16f;
            foreach (var (tab, label) in tabs)
            {
                var w = 190f;
                var rect = new Rect(tabX, tabsY, w, 34f);
                var on = tab == _tab;
                var hot = rect.Contains(BoardInput.Read().Mouse);
                theme.Fill(rect, on ? HudTheme.AccentW : hot ? HudTheme.Paper2 : HudTheme.Paper3);
                theme.Border(rect, on ? HudTheme.Accent : HudTheme.Rule);
                GUI.Label(rect, label, theme.At(theme.Small, 14,
                    on ? HudTheme.Accent : HudTheme.Ink, TextAnchor.MiddleCenter));
                if (hot && Input.GetMouseButtonDown(0)) _tab = tab;
                tabX += w + 8f;
            }

            var body = new Rect(panel.x + 16f, tabsY + 44f, panel.width - 32f, panel.yMax - (tabsY + 44f) - 16f);
            theme.Fill(body, HudTheme.Paper3);
            theme.Border(body, HudTheme.Rule2);

            switch (_tab)
            {
                case Tab.Zones: DrawZones(theme, hud, body); break;
                case Tab.Weather: DrawWeather(theme, hud, body); break;
                case Tab.Vfx: DrawVfx(theme, hud, body); break;
                case Tab.Character: DrawCharacter(theme, hud, body); break;
                case Tab.Sound: DrawSound(theme, body); break;
            }

            if (_weatherOverride)
            {
                var badge = new Rect(panel.xMax - 260f, panel.y + headerH + 8f, 244f, 30f);
                theme.Fill(badge, HudTheme.AlertW, 0.95f);
                theme.Border(badge, HudTheme.Alert);
                GUI.Label(badge, "표시 오버라이드 중 — 서버 상태 아님",
                    theme.At(theme.Small, 13, HudTheme.Alert, TextAnchor.MiddleCenter));
            }
        }

        /* ══════════════════════════════════════════════ 1. 구역 텔레포트 */

        private static void DrawZones(HudTheme theme, Hud hud, Rect area)
        {
            if (hud.world == null || hud.world.player == null)
            {
                GUI.Label(area, "world · player 참조가 없다 (씬 배선 확인)",
                    theme.At(theme.Body, 15, HudTheme.Alert, TextAnchor.MiddleCenter));
                return;
            }

            GUI.Label(new Rect(area.x + 10f, area.y + 8f, area.width - 20f, 22f),
                $"지금 위치 — {hud.world.HereName} ({hud.world.Current?.id ?? "—"})  ·  클릭하면 그 구역 스폰으로 순간이동",
                theme.At(theme.Label, 13, HudTheme.Ink2));

            var zones = hud.world.zones;
            const float rowH = 28f;
            const int cols = 3;
            var colW = (area.width - 20f) / cols;

            for (var i = 0; i < zones.Length; i += 1)
            {
                var zone = zones[i];
                if (zone == null) continue;

                var col = i % cols;
                var row = i / cols;
                var rect = new Rect(area.x + 10f + col * colW, area.y + 40f + row * (rowH + 2f), colW - 6f, rowH);
                if (rect.yMax > area.yMax - 4f) break;

                var here = hud.world.Current == zone;
                var hot = rect.Contains(BoardInput.Read().Mouse);
                theme.Fill(rect, here ? HudTheme.AccentW : hot ? HudTheme.Paper2 : HudTheme.Paper);
                if (here) theme.Border(rect, HudTheme.Accent);

                GUI.Label(rect, $"{zone.id} · {zone.zoneName}",
                    theme.At(theme.Small, 12, here ? HudTheme.Accent : HudTheme.Ink, TextAnchor.MiddleLeft));

                // **`client.Move`를 직접 부르지 않는다.** 위치만 옮기면 다음 프레임
                // `ZoneWorld.Update()`가 발밑 구역을 다시 읽어 실전과 같은 경로로
                // 서버에 알린다 — 여기서 하는 일은 발을 옮겨 놓는 것뿐이다
                if (hot && Input.GetMouseButtonDown(0)) hud.world.player.TeleportTo(zone.spawn);
            }
        }

        /* ══════════════════════════════════════════════ 2. 날씨 · 주야 */

        private static bool _weatherOverride;
        private static int _bandIndex = 2;
        private static float _nightOverride;

        private static void ApplyWeatherOverride(Hud hud)
        {
            var grading = hud.grading;
            if (grading == null) return;

            for (var i = 0; i < grading.bands.Length; i += 1)
            {
                if (grading.bands[i] == null) continue;
                grading.bands[i].weight = i == _bandIndex ? 1f : 0f;
            }
            if (grading.nightVolume != null) grading.nightVolume.weight = _nightOverride;
        }

        private static void DrawWeather(HudTheme theme, Hud hud, Rect area)
        {
            if (hud.grading == null)
            {
                GUI.Label(area, "grading(WeatherGrading) 참조가 없다 (씬 배선 확인)",
                    theme.At(theme.Body, 15, HudTheme.Alert, TextAnchor.MiddleCenter));
                return;
            }

            var y = area.y + 10f;
            var toggle = new Rect(area.x + 10f, y, 240f, 30f);
            var toggleHot = toggle.Contains(BoardInput.Read().Mouse);
            theme.Fill(toggle, _weatherOverride ? HudTheme.AlertW : toggleHot ? HudTheme.Paper2 : HudTheme.Paper);
            theme.Border(toggle, _weatherOverride ? HudTheme.Alert : HudTheme.Rule);
            GUI.Label(toggle, _weatherOverride ? "오버라이드 켜짐 — 끄기" : "오버라이드 꺼짐 — 켜기",
                theme.At(theme.Small, 13, _weatherOverride ? HudTheme.Alert : HudTheme.Ink, TextAnchor.MiddleCenter));
            if (toggleHot && Input.GetMouseButtonDown(0)) _weatherOverride = !_weatherOverride;
            y += 40f;

            GUI.Label(new Rect(area.x + 10f, y, area.width - 20f, 20f),
                "밴드 — Volume 가중치만 바꾼다(빛 색상표는 WeatherGrading 소유라 안 건드린다)",
                theme.At(theme.Label, 12, HudTheme.Ink3));
            y += 24f;

            for (var i = 0; i < WeatherGrading.BandOrder.Length; i += 1)
            {
                var band = WeatherGrading.BandOrder[i];
                var rect = new Rect(area.x + 10f + i * 160f, y, 152f, 30f);
                var on = _weatherOverride && i == _bandIndex;
                var hot = rect.Contains(BoardInput.Read().Mouse);
                theme.Fill(rect, on ? HudTheme.AccentW : hot ? HudTheme.Paper2 : HudTheme.Paper);
                theme.Border(rect, on ? HudTheme.Accent : HudTheme.Rule);
                GUI.Label(rect, HudTheme.BandLabel(band), theme.At(theme.Small, 13,
                    on ? HudTheme.Accent : HudTheme.Ink, TextAnchor.MiddleCenter));
                if (hot && Input.GetMouseButtonDown(0)) { _bandIndex = i; _weatherOverride = true; }
            }
            y += 44f;

            GUI.Label(new Rect(area.x + 10f, y, area.width - 20f, 20f), "주야",
                theme.At(theme.Label, 12, HudTheme.Ink3));
            y += 24f;

            var dayNight = new (string label, float value)[] { ("주간", 0f), ("야간(중첩)", 1f) };
            for (var i = 0; i < dayNight.Length; i += 1)
            {
                var (label, value) = dayNight[i];
                var rect = new Rect(area.x + 10f + i * 160f, y, 152f, 30f);
                var on = _weatherOverride && Mathf.Approximately(_nightOverride, value);
                var hot = rect.Contains(BoardInput.Read().Mouse);
                theme.Fill(rect, on ? HudTheme.AccentW : hot ? HudTheme.Paper2 : HudTheme.Paper);
                theme.Border(rect, on ? HudTheme.Accent : HudTheme.Rule);
                GUI.Label(rect, label, theme.At(theme.Small, 13,
                    on ? HudTheme.Accent : HudTheme.Ink, TextAnchor.MiddleCenter));
                if (hot && Input.GetMouseButtonDown(0)) { _nightOverride = value; _weatherOverride = true; }
            }
        }

        /* ══════════════════════════════════════════════ 3. VFX 발사 */

        private static void DrawVfx(HudTheme theme, Hud hud, Rect area)
        {
            if (!_vfxSearched)
            {
                _vfxSearched = true;
                _vfx = Object.FindFirstObjectByType<SoldierADay.Net.Vfx>();
            }
            if (_vfx == null)
            {
                GUI.Label(area, "Vfx 컴포넌트를 씬에서 못 찾았다",
                    theme.At(theme.Body, 15, HudTheme.Alert, TextAnchor.MiddleCenter));
                return;
            }

            var at = hud.world != null && hud.world.player != null
                ? hud.world.player.transform.position
                : Vector3.zero;

            var entries = new (string label, ParticleSystem system)[]
            {
                ("눈(약)", _vfx.snowLight), ("눈(강)", _vfx.snowHeavy), ("비", _vfx.rain),
                ("먼지", _vfx.dust), ("아지랑이", _vfx.heatHaze),
                ("입김", _vfx.breath), ("땀", _vfx.sweat),
                ("완료 링", _vfx.questComplete), ("총구 화염", _vfx.muzzleFlash),
                ("쓰러짐", _vfx.collapse), ("제독", _vfx.decon), ("증기", _vfx.steam),
            };

            GUI.Label(new Rect(area.x + 10f, area.y + 8f, area.width - 20f, 20f),
                $"내 위치에서 발사 — {entries.Length}종 (Vfx.Burst 실물 그대로)",
                theme.At(theme.Label, 13, HudTheme.Ink2));

            const float w = 210f, h = 34f;
            var cols = Mathf.Max(1, Mathf.FloorToInt((area.width - 20f) / (w + 8f)));
            for (var i = 0; i < entries.Length; i += 1)
            {
                var (label, system) = entries[i];
                var col = i % cols;
                var row = i / cols;
                var rect = new Rect(area.x + 10f + col * (w + 8f), area.y + 40f + row * (h + 8f), w, h);
                var hot = rect.Contains(BoardInput.Read().Mouse);
                var missing = system == null;
                theme.Fill(rect, missing ? HudTheme.Paper2 : hot ? HudTheme.Paper2 : HudTheme.Paper);
                theme.Border(rect, missing ? HudTheme.Rule : HudTheme.Rule2);
                GUI.Label(rect, missing ? $"{label} (미배선)" : label,
                    theme.At(theme.Small, 13, missing ? HudTheme.Ink3 : HudTheme.Ink, TextAnchor.MiddleCenter));
                if (!missing && hot && Input.GetMouseButtonDown(0)) _vfx.Burst(system, at);
            }
        }

        /* ══════════════════════════════════════════════ 4. 캐릭터 뷰어 */

        private static int _viewRole;
        private static int _viewRank;
        private static bool _viewFrostbite;
        private static bool _viewExhausted;

        private static readonly string[] Roles =
        {
            SnapshotMembersItemRoleValues.Rifle, SnapshotMembersItemRoleValues.Comms,
            SnapshotMembersItemRoleValues.Medic, SnapshotMembersItemRoleValues.Admin,
        };
        private static readonly string[] Ranks =
        {
            SnapshotMembersItemRankValues.Private, SnapshotMembersItemRankValues.Pfc,
            SnapshotMembersItemRankValues.Corporal, SnapshotMembersItemRankValues.Sergeant,
        };

        private static void DrawCharacter(HudTheme theme, Hud hud, Rect area)
        {
            var rig = hud.world != null && hud.world.player != null ? hud.world.player.Rig : null;
            if (rig == null)
            {
                GUI.Label(area, "내 CharacterRig가 아직 안 붙었다(서버 접속 전이면 정상)",
                    theme.At(theme.Body, 15, HudTheme.Alert, TextAnchor.MiddleCenter));
                return;
            }

            GUI.Label(new Rect(area.x + 10f, area.y + 8f, area.width - 20f, 40f),
                "내 캐릭터를 빌려 미리보기 — 접속 중이면 다음 스냅샷(≤100ms)이 알아서 진짜" +
                "보직으로 되돌린다. 급하면 [원복]으로 즉시 되돌려라(둘 다 접속이 있어야 한다)",
                new GUIStyle(theme.At(theme.Small, 13, HudTheme.Ink2)) { wordWrap = true });

            var y = area.y + 56f;
            GUI.Label(new Rect(area.x + 10f, y, 100f, 24f), "보직", theme.At(theme.Label, 12, HudTheme.Ink3));
            for (var i = 0; i < Roles.Length; i += 1)
            {
                var rect = new Rect(area.x + 100f + i * 120f, y, 112f, 26f);
                DrawPick(theme, rect, HudTheme.RoleTag(Roles[i]), i == _viewRole, () => _viewRole = i);
            }
            y += 34f;

            GUI.Label(new Rect(area.x + 10f, y, 100f, 24f), "계급", theme.At(theme.Label, 12, HudTheme.Ink3));
            for (var i = 0; i < Ranks.Length; i += 1)
            {
                var rect = new Rect(area.x + 100f + i * 120f, y, 112f, 26f);
                DrawPick(theme, rect, Ranks[i], i == _viewRank, () => _viewRank = i);
            }
            y += 34f;

            var frostbiteRect = new Rect(area.x + 10f, y, 160f, 28f);
            DrawPick(theme, frostbiteRect, "동상", _viewFrostbite, () => _viewFrostbite = !_viewFrostbite);
            var exhaustedRect = new Rect(area.x + 180f, y, 160f, 28f);
            DrawPick(theme, exhaustedRect, "탈진(피로·탈수)", _viewExhausted, () => _viewExhausted = !_viewExhausted);
            y += 40f;

            var applyRect = new Rect(area.x + 10f, y, 160f, 34f);
            var applyHot = applyRect.Contains(BoardInput.Read().Mouse);
            theme.Fill(applyRect, applyHot ? HudTheme.AccentW : HudTheme.Paper);
            theme.Border(applyRect, HudTheme.Accent);
            GUI.Label(applyRect, "적용", theme.At(theme.Body, 15, HudTheme.Accent, TextAnchor.MiddleCenter));
            if (applyHot && Input.GetMouseButtonDown(0))
            {
                // 실물 그리기 그대로 — `ZoneWorld.Apply`가 매 스냅샷 부르는 것과 같은 두 호출
                rig.SetLook(Roles[_viewRole], Ranks[_viewRank]);
                rig.SetDistress(_viewFrostbite, _viewExhausted);
            }

            // 서버에 붙어 있으면 다음 스냅샷(≤100ms)이 알아서 되돌리지만, 접속
            // 전(로비 없이 씬만 열어 본 경우)에는 그 되돌림이 안 온다 — 그때를
            // 위한 수동 복구다. 진짜 값은 최신 스냅샷에서 그대로 다시 읽는다
            var restoreRect = new Rect(area.x + 180f, y, 160f, 34f);
            var restoreHot = restoreRect.Contains(BoardInput.Read().Mouse);
            theme.Fill(restoreRect, restoreHot ? HudTheme.Paper2 : HudTheme.Paper);
            theme.Border(restoreRect, HudTheme.Rule);
            GUI.Label(restoreRect, "원복", theme.At(theme.Body, 15, HudTheme.Ink, TextAnchor.MiddleCenter));
            if (restoreHot && Input.GetMouseButtonDown(0))
            {
                var me = hud.client != null ? HudScreens.FindMember(hud.client.Latest, hud.client.MemberId) : null;
                if (me != null)
                {
                    rig.SetLook(me.role, me.rank);
                    rig.SetDistress(me.frostbitten, me.stats.fatigue >= 90d || me.stats.hydration <= 30d);
                }
            }
        }

        private static void DrawPick(HudTheme theme, Rect rect, string label, bool on, System.Action pick)
        {
            var hot = rect.Contains(BoardInput.Read().Mouse);
            theme.Fill(rect, on ? HudTheme.AccentW : hot ? HudTheme.Paper2 : HudTheme.Paper);
            theme.Border(rect, on ? HudTheme.Accent : HudTheme.Rule);
            GUI.Label(rect, label, theme.At(theme.Small, 13, on ? HudTheme.Accent : HudTheme.Ink, TextAnchor.MiddleCenter));
            if (hot && Input.GetMouseButtonDown(0)) pick();
        }

        /* ══════════════════════════════════════════════ 5. 소리 시청 */

        /// <summary>`Sfx.cs`의 키 배열(private)을 그대로 옮겨 적었다 — 늘어나면 여기도
        /// 같이 늘려야 한다(§소유권, `Sfx.cs`는 건드리지 않는다). D-4 1단계가
        /// `tools/audio/gen_sfx.py`로 열 개를 냈다 — 넷만 옮겨 적었던 것을 마저 채운다</summary>
        private static readonly string[] SfxKeys =
        {
            "tap", "success", "miss", "warning", "walk", "door", "open", "close", "alarm", "pop",
        };

        private static void DrawSound(HudTheme theme, Rect area)
        {
            GUI.Label(new Rect(area.x + 10f, area.y + 8f, area.width - 20f, 20f),
                $"Sfx 키 {SfxKeys.Length}종 — 처음 한 번은 브라우저 오디오 잠금 때문에 무음일 수 있다(아무 키나 눌러 이미 풀렸다면 정상)",
                theme.At(theme.Label, 13, HudTheme.Ink2));

            const float w = 160f, h = 34f;
            for (var i = 0; i < SfxKeys.Length; i += 1)
            {
                var rect = new Rect(area.x + 10f + i * (w + 8f), area.y + 40f, w, h);
                var hot = rect.Contains(BoardInput.Read().Mouse);
                theme.Fill(rect, hot ? HudTheme.Paper2 : HudTheme.Paper);
                theme.Border(rect, HudTheme.Rule2);
                GUI.Label(rect, SfxKeys[i], theme.At(theme.Small, 13, HudTheme.Ink, TextAnchor.MiddleCenter));
                if (hot && Input.GetMouseButtonDown(0)) Sfx.Play(SfxKeys[i]);
            }
        }
    }
}
