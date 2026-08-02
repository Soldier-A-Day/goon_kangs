using SoldierADay.Protocol;
using UnityEngine;

namespace SoldierADay.Net
{
    /// <summary>
    /// 화면 실험대 (`docs/WORKORDER.md` T단계).
    ///
    /// **T단계의 문제**: 점호 8종·마감 큐·하달·퇴소/전역·토스트·게이지 같은 전체화면·
    /// 상태 UI는 전부 서버가 그 순간을 만들어야만 볼 수 있다 — 조건 D 실패를
    /// 보려면 실제로 위생을 20 밑으로 떨어뜨리고 점호까지 살아남아야 한다.
    /// `BoardLab`(F9)이 미니게임 판을 서버 없이 떼어낸 것과 같은 이유로, 여기서는
    /// 그 화면들을 **가짜 스냅샷·가짜 이벤트로 즉시** 띄운다.
    ///
    /// ── 그리기는 전부 실물이다 ───────────────────────────────────────────
    /// 이 파일은 화면을 하나도 새로 그리지 않는다. `Hud.LabScreens`(전용
    /// `HudScreens` 인스턴스)가 `DrawRollCall`·`DrawSleep`·`DrawRankReview`·
    /// `DrawDelegation`·`DrawSchedule`를 그대로 돌리고, `Hud`가 새로 연 `Debug*`
    /// 통로(`DebugNotify`·`DebugDrawToasts`·`DebugCondition`·`DebugBodyHeat`·
    /// `DebugConnectionBanner`)가 토스트·게이지·배너를 그대로 돌린다.
    /// `HudEnding`은 `client` 대신 `rejected`·`status`·`snapshot`을 직접 받는
    /// 오버로드로 퇴소·전역·분대해산·연결거절 화면을 그린다. 실물과 다른 곳은
    /// 데이터를 만드는 이 파일뿐이다 — 어긋나면 여기 숫자가 틀린 것이지 화면이
    /// 거짓말한 것이 아니다.
    ///
    /// ── 서버로 아무것도 안 보낸다 ────────────────────────────────────────
    /// 하달 창을 재사용하면 카드 클릭·확정 버튼이 곧바로 `Client.Send`를 부르는
    /// 코드까지 같이 따라온다. `HudScreens.LabPreviewOnly`가 그 두 전송만 막고
    /// (`Hud.LabScreens`가 생성 시점에 켠다), 나머지는 손대지 않는다 — 화면
    /// 하나를 위해 다른 아홉 개의 동작을 바꾸지 않는다.
    ///
    /// ── 소유권 ───────────────────────────────────────────────────────────
    /// 이 파일이 화면 실험대의 전부다. `Hud.cs`·`HudScreens.cs`에는 이 랩이
    /// 실물을 재사용할 수 있도록 `Debug*` 통로만 얹었다(둘 다 기존 그리기를
    /// 한 줄도 바꾸지 않았다 — 새 메서드로만 노출했다).
    /// </summary>
    public static class ScreenLab
    {
        private static bool _open;
        private static int _lastFrame = -1;

        public static bool IsOpen => _open;

        private enum Item
        {
            RollCallPass, RollCallReliefUsed, RollCallReliefExhausted,
            RollCallFailA, RollCallFailB, RollCallFailC, RollCallFailD,
            DayEndChain,
            Delegation,
            Schedule,
            Discharged, Cleared, Disbanded, Rejected,
            ToastSurprise, ToastDiscipline, ToastDelegated, ToastVetoed, ToastEvacuated,
            ToastReturned, ToastReassigned, ToastSupply, ToastForcedSleep, ToastFrostbite,
            ToastRelief, ToastReliefRefused, ToastCondition, ToastCrisis, ToastRescue,
            ToastWeather, ToastHidden,
            BannerReconnecting, BannerFailed,
            GaugeStamina, GaugeHydration, GaugeFatigue, GaugeMental, GaugeHygiene, GaugeHygieneDanger,
            WarmthNormal, WarmthRisk, WarmthFrostbitten,
        }

        private static readonly (Item item, string label)[] Menu =
        {
            (Item.RollCallPass, "점호 — 전 조건 통과"),
            (Item.RollCallReliefUsed, "점호 — 통과(구제권 사용)"),
            (Item.RollCallReliefExhausted, "점호 — 통과(구제권 소진)"),
            (Item.RollCallFailA, "점호 — 실패 A(필수 미달)"),
            (Item.RollCallFailB, "점호 — 실패 B(합동 미달)"),
            (Item.RollCallFailC, "점호 — 실패 C(군기 미달)"),
            (Item.RollCallFailD, "점호 — 실패 D(복장·위생)"),
            (Item.DayEndChain, "마감 큐 — 판정→승급→취침 연쇄"),
            (Item.Delegation, "하달 창"),
            (Item.Schedule, "일과표"),
            (Item.Discharged, "종료 — 퇴소"),
            (Item.Cleared, "종료 — 전역"),
            (Item.Disbanded, "종료 — 분대 해산"),
            (Item.Rejected, "종료 — 연결 거절"),
            (Item.ToastSurprise, "토스트 — 돌발"),
            (Item.ToastDiscipline, "토스트 — 군기 변동"),
            (Item.ToastDelegated, "토스트 — 하달"),
            (Item.ToastVetoed, "토스트 — 하달 거부"),
            (Item.ToastEvacuated, "토스트 — 후송"),
            (Item.ToastReturned, "토스트 — 복귀(이병 강등)"),
            (Item.ToastReassigned, "토스트 — 분대장 재배정"),
            (Item.ToastSupply, "토스트 — 보급 도착"),
            (Item.ToastForcedSleep, "토스트 — 강제 취침"),
            (Item.ToastFrostbite, "토스트 — 동상 해제"),
            (Item.ToastRelief, "토스트 — 간부 구제 발동"),
            (Item.ToastReliefRefused, "토스트 — 구제 발동 거부"),
            (Item.ToastCondition, "토스트 — 컨디션 위험"),
            (Item.ToastCrisis, "토스트 — 위기 발생"),
            (Item.ToastRescue, "토스트 — 구조 성공"),
            (Item.ToastWeather, "토스트 — 기온 변화"),
            (Item.ToastHidden, "토스트 — 히든 해금"),
            (Item.BannerReconnecting, "배너 — 재접속 중(2/4차)"),
            (Item.BannerFailed, "배너 — 연결 끊김"),
            (Item.GaugeStamina, "게이지 — 체력 위험"),
            (Item.GaugeHydration, "게이지 — 수분 위험"),
            (Item.GaugeFatigue, "게이지 — 피로 위험"),
            (Item.GaugeMental, "게이지 — 정신력 위험"),
            (Item.GaugeHygiene, "게이지 — 청결 정상"),
            (Item.GaugeHygieneDanger, "게이지 — 청결 위험"),
            (Item.WarmthNormal, "보온 게이지 — 정상"),
            (Item.WarmthRisk, "보온 게이지 — 동상 위험"),
            (Item.WarmthFrostbitten, "보온 게이지 — 동상"),
        };

        private static int _index;
        /// <summary>토스트류는 "즉시 표시" 항목이라 목록 클릭마다 다시 쏜다.
        /// 화면류(점호·마감 큐·하달·일과표·종료)는 계속 그 상태를 유지해야 하므로
        /// 별도 플래그로 "지금 이게 떠 있다"를 기억한다</summary>
        private static Item? _screenItem;

        public static void Draw(HudTheme theme, Hud hud)
        {
            var freshFrame = Time.frameCount != _lastFrame;
            if (freshFrame) _lastFrame = Time.frameCount;

            if (freshFrame && Input.GetKeyDown(KeyCode.F10)) Toggle(hud);
            if (!_open) return;

            if (freshFrame) HandleKeys(hud);

            DrawPanel(theme, hud);
        }

        private static void Toggle(Hud hud)
        {
            _open = !_open;
            if (!_open) hud.LabScreens.DebugClose();
        }

        private static void HandleKeys(Hud hud)
        {
            if (Input.GetKeyDown(KeyCode.DownArrow)) _index = (_index + 1) % Menu.Length;
            if (Input.GetKeyDown(KeyCode.UpArrow)) _index = (_index - 1 + Menu.Length) % Menu.Length;
            if (Input.GetKeyDown(KeyCode.Return)) Run(hud, Menu[_index].item);
            // 화면류를 보고 있는 동안 Space로 큐를 넘긴다 — 실전 UpdateDayEndAdvance와
            // 같은 손맛이다(랩은 Update()를 안 타므로 이 통로로 대신한다)
            if (Input.GetKeyDown(KeyCode.Space)) hud.LabScreens.DebugAdvanceDayEnd();

            // 전체화면 미리보기(점호·하달·일과표·종료)는 실물 그대로 화면을 다
            // 덮는다 — 그게 정직한 재현이다. 대신 Esc로 목록으로 돌아온다
            if (Input.GetKeyDown(KeyCode.Escape) && _screenItem.HasValue)
            {
                hud.LabScreens.DebugClose();
                _screenItem = null;
            }
        }

        /* ──────────────────────────────────────────────────────── 그리기 */

        private static void DrawPanel(HudTheme theme, Hud hud)
        {
            var full = new Rect(0f, 0f, HudTheme.ViewWidth, HudTheme.ViewHeight);
            theme.Fill(full, HudTheme.Dim, 0.55f);

            // 상단 얇은 머리띠 — 실물 요소(토스트는 y48~, 페이즈바는 y48~)와
            // 겹치지 않는 자리다. 목록은 오른쪽 세로 띠에 둔다
            var head = new Rect(0f, 0f, HudTheme.ViewWidth, 30f);
            theme.Fill(head, HudTheme.Paper2, 0.96f);
            GUI.Label(new Rect(12f, 4f, 900f, 22f),
                "ScreenLab — 화면 실험대  [F10]   [↑↓] 고르기  [ENTER] 열기  " +
                "[SPACE] 마감 큐 다음  [ESC] 전체화면 미리보기 닫기",
                theme.At(theme.Label, 13, HudTheme.Ink));

            var listW = 340f;
            var list = new Rect(HudTheme.ViewWidth - listW - 12f, 40f, listW, HudTheme.ViewHeight - 80f);
            theme.Fill(list, HudTheme.Paper3, 0.94f);
            theme.Border(list, HudTheme.Heat, 2f);

            const float rowH = 24f;
            for (var i = 0; i < Menu.Length; i += 1)
            {
                var row = new Rect(list.x + 4f, list.y + 4f + i * (rowH + 1f), list.width - 8f, rowH);
                if (row.yMax > list.yMax - 4f) break;

                var selected = i == _index;
                var hot = row.Contains(BoardInput.Read().Mouse);
                theme.Fill(row, selected ? HudTheme.AccentW : hot ? HudTheme.Paper2 : HudTheme.Paper3);
                if (selected) theme.Border(row, HudTheme.Accent);

                GUI.Label(row, Menu[i].label,
                    theme.At(theme.Small, 13, selected ? HudTheme.Accent : HudTheme.Ink, TextAnchor.MiddleLeft));

                if (hot && Input.GetMouseButtonDown(0)) { _index = i; Run(hud, Menu[i].item); }
            }

            // 지금 떠 있는 화면류 — 목록보다 먼저 그려야 목록·머리띠가 위로 온다
            if (_screenItem.HasValue) DrawScreenItem(theme, hud, _screenItem.Value);
        }

        private static void Run(Hud hud, Item item)
        {
            hud.LabScreens.DebugClose();
            _screenItem = null;

            switch (item)
            {
                case Item.RollCallPass:
                    hud.LabScreens.DebugOpenRollCall(FakeJudgement(true, null, 0, 3));
                    _screenItem = item;
                    return;
                case Item.RollCallReliefUsed:
                    hud.LabScreens.DebugOpenRollCall(FakeJudgement(true, null, 1, 1));
                    _screenItem = item;
                    return;
                case Item.RollCallReliefExhausted:
                    hud.LabScreens.DebugOpenRollCall(FakeJudgement(true, null, 1, 0));
                    _screenItem = item;
                    return;
                case Item.RollCallFailA:
                    hud.LabScreens.DebugOpenRollCall(FakeJudgement(false, "A", 0, 0));
                    _screenItem = item;
                    return;
                case Item.RollCallFailB:
                    hud.LabScreens.DebugOpenRollCall(FakeJudgement(false, "B", 0, 0));
                    _screenItem = item;
                    return;
                case Item.RollCallFailC:
                    hud.LabScreens.DebugOpenRollCall(FakeJudgement(false, "C", 0, 0));
                    _screenItem = item;
                    return;
                case Item.RollCallFailD:
                    hud.LabScreens.DebugOpenRollCall(FakeJudgement(false, "D", 0, 0));
                    _screenItem = item;
                    return;

                case Item.DayEndChain:
                    hud.LabScreens.DebugOpenDayEndChain(
                        FakeJudgement(true, null, 0, 3), FakeRankReview(hud), FakeSleepSettle(),
                        FakeSnapshot(hud));
                    _screenItem = item;
                    return;

                case Item.Delegation:
                    _screenItem = item;
                    hud.LabScreens.DebugOpenDelegation();
                    return;

                case Item.Schedule:
                    _screenItem = item;
                    hud.LabScreens.DebugOpenSchedule();
                    return;

                case Item.Discharged: case Item.Cleared: case Item.Disbanded: case Item.Rejected:
                    _screenItem = item;
                    return;

                case Item.ToastSurprise:
                    hud.DebugNotify("취사장 물난리", "동파로 취사장이 잠겼다 — 하던 일이 끊겼다", HudTheme.Alert, 6.2f);
                    return;
                case Item.ToastDiscipline:
                    hud.DebugNotify("군기", "62 · 양호", HudTheme.Alert);
                    return;
                case Item.ToastDelegated:
                    hud.DebugNotify("하달", "김상병 → 이일병", HudTheme.Heat);
                    return;
                case Item.ToastVetoed:
                    hud.DebugNotify("거부", "이일병", HudTheme.Ink2);
                    return;
                case Item.ToastEvacuated:
                    hud.DebugNotify("후송", "박일병 — 탈진, 남은 셋이 몫을 나눈다", HudTheme.Alert, 5.6f);
                    return;
                case Item.ToastReturned:
                    hud.DebugNotify("복귀", "박일병, 이병으로 — 계급 초기화", HudTheme.Alert, 6.6f);
                    return;
                case Item.ToastReassigned:
                    hud.DebugNotify("재배정", "분대장이 이일병에게 — 세면장 청소", HudTheme.Heat);
                    return;
                case Item.ToastSupply:
                    hud.DebugNotify("보급 도착", "전투화 · 방한복 · 건전지 외 2건", HudTheme.Accent);
                    return;
                case Item.ToastForcedSleep:
                    hud.DebugNotify("강제 취침", "이일병, 이 칸 일과 잠김", HudTheme.Heat);
                    return;
                case Item.ToastFrostbite:
                    hud.DebugNotify("동상 해제", "박일병 — 의무병 최상병", HudTheme.Accent);
                    return;
                case Item.ToastRelief:
                    hud.DebugNotify("간부 구제 발동", "오늘 저녁 발동 — 그날 미달 1건을 상쇄한다", HudTheme.Heat);
                    return;
                case Item.ToastReliefRefused:
                    hud.DebugNotify("구제 발동 거부", "이미 오늘 한 번 썼다", HudTheme.Alert);
                    return;
                case Item.ToastCondition:
                    hud.DebugNotify("위험", "이일병 탈진 직전 — 그대로 두면 실려 나간다", HudTheme.Alert);
                    return;
                case Item.ToastCrisis:
                    hud.DebugNotify("위기", "박일병이 쓰러졌다 — 40초 안에 가라", HudTheme.Alert, 5.6f);
                    return;
                case Item.ToastRescue:
                    hud.DebugNotify("구조", "김상병이 박일병을 살렸다", HudTheme.Accent);
                    return;
                case Item.ToastWeather:
                    hud.DebugNotify("기온", "한랭 · 체감 −8°C", HudTheme.Cold);
                    return;
                case Item.ToastHidden:
                    hud.DebugNotify("히든", "관물대 밑 편지", HudTheme.Accent);
                    return;

                case Item.BannerReconnecting:
                    _screenItem = item;
                    return;
                case Item.BannerFailed:
                    _screenItem = item;
                    return;

                case Item.GaugeStamina: case Item.GaugeHydration: case Item.GaugeFatigue:
                case Item.GaugeMental: case Item.GaugeHygiene: case Item.GaugeHygieneDanger:
                    _screenItem = item;
                    return;

                case Item.WarmthNormal: case Item.WarmthRisk: case Item.WarmthFrostbitten:
                    _screenItem = item;
                    return;
            }
        }

        /// <summary>지속형 항목(화면·배너·게이지)을 매 프레임 다시 그린다 — 토스트는
        /// `Run`에서 한 번 쏘고 나면 `Hud._toasts`가 알아서 페이드아웃까지 맡는다</summary>
        private static void DrawScreenItem(HudTheme theme, Hud hud, Item item)
        {
            switch (item)
            {
                case Item.RollCallPass: case Item.RollCallReliefUsed: case Item.RollCallReliefExhausted:
                case Item.RollCallFailA: case Item.RollCallFailB: case Item.RollCallFailC: case Item.RollCallFailD:
                case Item.DayEndChain: case Item.Delegation: case Item.Schedule:
                    hud.LabScreens.Draw(theme, FakeSnapshot(hud));
                    return;

                case Item.Discharged:
                    HudEnding.Draw(theme, null, SnapshotStatusValues.Discharged, FakeFailedSnapshot(hud),
                        NoOp, NoOp);
                    return;
                case Item.Cleared:
                    HudEnding.Draw(theme, null, SnapshotStatusValues.Cleared, FakeSnapshot(hud), NoOp, NoOp);
                    return;
                case Item.Disbanded:
                    HudEnding.Draw(theme, null, SnapshotStatusValues.Disbanded, FakeSnapshot(hud), NoOp, NoOp);
                    return;
                case Item.Rejected:
                    HudEnding.Draw(theme, "서버가 연결을 거절했다 — 토큰 만료", null, null, NoOp, NoOp);
                    return;

                case Item.BannerReconnecting:
                    hud.DebugConnectionBanner("무전 재접속 중 (2/4차) — 자리를 지키십시오", HudTheme.Heat);
                    return;
                case Item.BannerFailed:
                    hud.DebugConnectionBanner("연결이 끊겼습니다 — 새로고침으로 다시 붙을 수 있습니다", HudTheme.Alert);
                    return;

                case Item.GaugeStamina:
                    hud.DebugCondition(FakeStatSnapshot(hud, "stamina", 18d));
                    return;
                case Item.GaugeHydration:
                    hud.DebugCondition(FakeStatSnapshot(hud, "hydration", 20d));
                    return;
                case Item.GaugeFatigue:
                    hud.DebugCondition(FakeStatSnapshot(hud, "fatigue", 82d));
                    return;
                case Item.GaugeMental:
                    hud.DebugCondition(FakeStatSnapshot(hud, "mental", 12d));
                    return;
                case Item.GaugeHygiene:
                    hud.DebugCondition(FakeStatSnapshot(hud, "hygiene", 70d));
                    return;
                case Item.GaugeHygieneDanger:
                    hud.DebugCondition(FakeStatSnapshot(hud, "hygiene", 12d));
                    return;

                case Item.WarmthNormal:
                    hud.DebugBodyHeat(FakeWarmthSnapshot(hud, 70_000d, false));
                    return;
                case Item.WarmthRisk:
                    hud.DebugBodyHeat(FakeWarmthSnapshot(hud, 12_000d, false));
                    return;
                case Item.WarmthFrostbitten:
                    hud.DebugBodyHeat(FakeWarmthSnapshot(hud, 0d, true));
                    return;
            }

            // 토스트는 지속형이 아니다 — 매 프레임 이 스위치에 걸리지 않고
            // `Hud.DebugDrawToasts()`가 실전과 같은 `_toasts` 리스트를 그린다
            hud.DebugDrawToasts();
        }

        private static void NoOp() { }

        /* ──────────────────────────────────────────────────────── 가짜 데이터 */

        private static readonly (string id, string name, string role, string rank)[] Squad =
        {
            ("m1", "김상병", SnapshotMembersItemRoleValues.Rifle, SnapshotMembersItemRankValues.Corporal),
            ("m2", "이일병", SnapshotMembersItemRoleValues.Comms, SnapshotMembersItemRankValues.Pfc),
            ("m3", "박일병", SnapshotMembersItemRoleValues.Medic, SnapshotMembersItemRankValues.Private),
            ("m4", "최상병", SnapshotMembersItemRoleValues.Admin, SnapshotMembersItemRankValues.Corporal),
        };

        /// <summary>
        /// "나"의 id는 `Squad[0]`의 자리 이름("m1")이 아니라 **실제 접속한 내 memberId**를
        /// 써야 한다. `DrawDelegation`·`DrawSchedule`·`DrawSleep`은 전부
        /// `Client.MemberId`(=`hud.client.MemberId`, 실전 그대로)로 "내 것"을 걸러내므로,
        /// 가짜 스냅샷의 0번 대원 id가 그 값과 다르면 화면이 텅 비어 보인다 —
        /// 연결 전(빈 문자열)에는 "m1"로 대신한다
        /// </summary>
        private static string MeId(Hud hud) =>
            !string.IsNullOrEmpty(hud.client != null ? hud.client.MemberId : null) ? hud.client.MemberId : "m1";

        private static SnapshotMembersItem FakeMember(string id, string name, string role, string rank,
            double stamina = 70, double hydration = 70, double fatigue = 30, double mental = 70,
            double hygiene = 70, double satiety = 70) => new SnapshotMembersItem
        {
            id = id, name = name, role = role, rank = rank,
            presence = SnapshotMembersItemPresenceValues.Player,
            zone = "Z02", serviceScore = 120, choresReceived = 0,
            inventory = new[] { "combatUniform", "boots" },
            missingGear = System.Array.Empty<string>(),
            stats = new SnapshotMembersItemStats
            {
                stamina = stamina, hydration = hydration, fatigue = fatigue,
                mental = mental, hygiene = hygiene, satiety = satiety,
            },
        };

        private static SnapshotMembersItem[] FakeMembers(Hud hud)
        {
            var meId = MeId(hud);
            var members = new SnapshotMembersItem[Squad.Length];
            for (var i = 0; i < Squad.Length; i += 1)
            {
                var (id, name, role, rank) = Squad[i];
                // 0번 자리만 실제 내 id로 바꿔 낀다 — 이름·보직·계급은 그대로 둔다
                members[i] = FakeMember(i == 0 ? meId : id, name, role, rank);
            }
            return members;
        }

        private static SnapshotQuestsItem[] FakeQuests(string meId) => new[]
        {
            new SnapshotQuestsItem
            {
                id = "q1", kind = SnapshotQuestsItemKindValues.Chore, label = "관물대 정돈",
                ownerId = meId, required = true, phase = "morning", zone = "Z02",
                status = SnapshotQuestsItemStatusValues.Pending, minActors = 1,
            },
            new SnapshotQuestsItem
            {
                id = "q2", kind = SnapshotQuestsItemKindValues.Chore, label = "세면장 청소",
                ownerId = meId, required = false, phase = "morning", zone = "Z03",
                status = SnapshotQuestsItemStatusValues.Pending, minActors = 1,
            },
            new SnapshotQuestsItem
            {
                id = "q3", kind = SnapshotQuestsItemKindValues.Joint, label = "탄약고 재물조사",
                ownerId = null, required = true, phase = "afternoon", zone = "Z05",
                status = SnapshotQuestsItemStatusValues.Active, minActors = 2,
                jointDone = 3, jointTotal = 8,
            },
        };

        private static Snapshot FakeSnapshot(Hud hud)
        {
            var meId = MeId(hud);
            return new Snapshot
            {
                type = "snapshot", seq = 1, status = SnapshotStatusValues.Running,
                day = 5, totalDays = 18,
                phase = new SnapshotPhase
                {
                    id = SnapshotPhaseIdValues.Afternoon, index = 3, label = "오후 일과",
                    clock = "14:00", elapsedMs = 30_000d, durationMs = 120_000d,
                    delegationWindowMsLeft = 18_000d,
                },
                weather = new SnapshotWeather { band = "cold", label = "한랭", feelsLike = -6d, rain = false },
                discipline = new SnapshotDiscipline { value = 58, band = "양호" },
                supply = new SnapshotSupply { points = 3, isSupplyDay = true, pendingClaim = System.Array.Empty<string>() },
                reliefsRemaining = 2,
                members = FakeMembers(hud),
                quests = FakeQuests(meId),
                headline = "한파 속 물자 부족 D-5",
            };
        }

        /// <summary>퇴소 화면(`FailureDetail`)이 읽는 `lastJudgement`·`firstFailure`까지 채운 버전</summary>
        private static Snapshot FakeFailedSnapshot(Hud hud)
        {
            var snapshot = FakeSnapshot(hud);
            snapshot.status = SnapshotStatusValues.Discharged;
            snapshot.lastJudgement = new SnapshotLastJudgement
            {
                day = 5, passed = false, failedAt = SnapshotLastJudgementFailedAtValues.A,
                requiredDone = 3, requiredTotal = 5,
            };
            snapshot.firstFailure = new SnapshotFirstFailure
            {
                condition = SnapshotFirstFailureConditionValues.A, day = 3,
                memberName = "이일병", value = 3, threshold = 5, questLabel = "관물대 정돈",
            };
            return snapshot;
        }

        private static Snapshot FakeStatSnapshot(Hud hud, string statId, double value)
        {
            var snapshot = FakeSnapshot(hud);
            var me = snapshot.members[0];
            switch (statId)
            {
                case "stamina": me.stats.stamina = value; break;
                case "hydration": me.stats.hydration = value; break;
                case "fatigue": me.stats.fatigue = value; break;
                case "mental": me.stats.mental = value; break;
                case "hygiene": me.stats.hygiene = value; break;
                default: me.stats.satiety = value; break;
            }
            return snapshot;
        }

        private static Snapshot FakeWarmthSnapshot(Hud hud, double warmthRemainingMs, bool frostbitten)
        {
            var snapshot = FakeSnapshot(hud);
            snapshot.members[0].warmthRemainingMs = warmthRemainingMs;
            snapshot.members[0].frostbitten = frostbitten;
            return snapshot;
        }

        private static ServerEvent FakeJudgement(bool passed, string failedAt,
            double reliefsUsed, double reliefsRemaining) => new ServerEvent
        {
            type = ServerEventTypeValues.DayJudged, day = 5,
            passed = passed, failedAt = failedAt ?? "",
            reliefsUsed = reliefsUsed, reliefsRemaining = reliefsRemaining,
        };

        private static ServerEvent FakeRankReview(Hud hud) => new ServerEvent
        {
            type = ServerEventTypeValues.RankReviewed, day = 5, isRetry = false, require = 400,
            outcomes = new[]
            {
                new ServerEventOutcomesItem
                {
                    memberId = MeId(hud), promoted = true,
                    from = SnapshotMembersItemRankValues.Corporal, to = SnapshotMembersItemRankValues.Sergeant,
                    score = 420, require = 400, trustBonus = 60,
                },
                new ServerEventOutcomesItem
                {
                    memberId = "m2", promoted = false,
                    from = SnapshotMembersItemRankValues.Pfc, to = SnapshotMembersItemRankValues.Pfc,
                    score = 260, require = 400, trustBonus = 20,
                },
            },
        };

        private static ServerEvent FakeSleepSettle() => new ServerEvent
        {
            type = ServerEventTypeValues.SleepSettled,
            guardIds = new[] { "m3" },
        };
    }
}
