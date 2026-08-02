using SoldierADay.Protocol;
using UnityEngine;

namespace SoldierADay.Net
{
    /// <summary>
    /// 런이 끝났거나 붙지 못했을 때 화면.
    ///
    /// **여기가 비어 있으면 화면이 아무 말도 안 한다.**
    ///
    /// 런이 끝나면 서버가 스냅샷 방송을 멈춘다(`Room.tick`이 먼저 빠져나간다).
    /// 그러면 Unity는 마지막 프레임에서 얼어붙고, 플레이어는 퇴소된 줄도 모른 채
    /// 멈춘 화면을 본다. 새로고침하면 더 나쁘다 — 토큰이 죽어 있으면 스냅샷이
    /// 한 번도 안 오므로 월드가 아예 안 세워지고, 빈 맵만 남는다.
    ///
    /// 둘 다 "서버가 더 이상 말하지 않는다"는 같은 사건이라 한 곳에서 그린다.
    /// </summary>
    public static class HudEnding
    {
        /// <summary>그렸으면 참 — 그 아래 HUD는 그릴 이유가 없다</summary>
        public static bool Draw(HudTheme theme, GameClient client,
                                System.Action onRestart, System.Action onLobby,
                                bool restarting = false, string restartError = "")
        {
            if (client == null) return false;
            return Draw(theme, client.Rejected, client.Latest?.status, client.Latest,
                onRestart, onLobby, restarting, restartError);
        }

        /// <summary>
        /// T단계 ScreenLab(F10) 전용 진입점 — `rejected`·`status`·`snapshot`을 직접 받는다.
        ///
        /// 원래는 `GameClient`에서 세 값을 꺼내 왔는데, `Rejected`·`Latest`가 private
        /// setter라 서버 연결 없이는 퇴소·전역·분대해산·연결거절 화면을 하나도 재현할
        /// 수 없었다. 그리기 코드를 복붙하는 대신(§T단계 원칙) 값을 파라미터로 뽑아
        /// 올렸다 — 동작은 그대로다. 위 오버로드가 그대로 이걸 부른다.
        /// </summary>
        public static bool Draw(HudTheme theme, string rejected, string status, Snapshot snapshot,
                                System.Action onRestart, System.Action onLobby,
                                bool restarting = false, string restartError = "")
        {
            var ended = !string.IsNullOrEmpty(status) &&
                        status != SnapshotStatusValues.Running;

            if (string.IsNullOrEmpty(rejected) && !ended) return false;

            theme.Fill(new Rect(0f, 0f, HudTheme.ViewWidth, HudTheme.ViewHeight),
                       HudTheme.Dim, 0.92f);

            // C-1 — 실패 조건(A~D)과 소대장 대사를 한 줄 더 넣느라 패널을 키웠다
            // (380→420). 다른 종료 화면(전역·해산·연결 끊김)은 그 줄이 비어 있을
            // 뿐 레이아웃은 그대로다.
            //
            // C-1(런 요약 헤드라인) — 상단에 한 줄을 더 얹느라 다시 키웠다(420→460).
            // "한파 속 물자 부족 D-5 완주" 형식으로 성공·실패 모두에 붙는다 — 실패
            // 전용 장치가 아니다(WORKORDER.md C-1).
            var panel = new Rect(HudTheme.ViewWidth * 0.5f - 380f,
                                 HudTheme.ViewHeight * 0.5f - 230f, 760f, 460f);
            theme.Fill(panel, HudTheme.Paper2);
            theme.Border(panel, rejected != null ? HudTheme.Alert : Accent(status), 2f);

            var (title, line, quote, hint) = Words(rejected, status, snapshot);

            GUI.Label(new Rect(panel.x, panel.y + 44f, panel.width, 24f),
                rejected != null ? "연결 끊김" : "런 종료",
                theme.At(theme.Label, 13, HudTheme.Ink2, TextAnchor.MiddleCenter));

            // 연결이 끊긴 화면에는 헤드라인이 없다 — 그 사건은 "이번 판이 무엇이었나"가
            // 아니라 "서버에 말을 걸 수 없다"는 얘기라 별개다.
            var headline = rejected == null ? snapshot?.headline : null;
            if (!string.IsNullOrEmpty(headline))
            {
                GUI.Label(new Rect(panel.x, panel.y + 68f, panel.width, 32f), headline,
                    theme.At(theme.Heading, 22, Accent(status), TextAnchor.MiddleCenter));
            }

            GUI.Label(new Rect(panel.x, panel.y + 122f, panel.width, 60f), title,
                theme.At(theme.Display, 44, rejected != null ? HudTheme.Alert : Accent(status),
                    TextAnchor.MiddleCenter));

            GUI.Label(new Rect(panel.x + 40f, panel.y + 196f, panel.width - 80f, 30f), line,
                theme.At(theme.Body, 17, HudTheme.Ink, TextAnchor.MiddleCenter));

            GUI.Label(new Rect(panel.x + 40f, panel.y + 232f, panel.width - 80f, 30f), hint,
                theme.At(theme.Small, 14, HudTheme.Ink3, TextAnchor.MiddleCenter));

            // C-1b — 조건별 소대장 대사. 실패 원인 위에 소대장이 한마디 얹는다
            // ("할 일을 남겼다. 부대는 그걸 안 봐준다." 등 4종, 조건 A~D에 고정).
            if (!string.IsNullOrEmpty(quote))
            {
                GUI.Label(new Rect(panel.x + 40f, panel.y + 268f, panel.width - 80f, 28f), quote,
                    theme.At(theme.Body, 15, HudTheme.Ink2, TextAnchor.MiddleCenter));
            }

            // **같은 방으로 다시 한다.**
            //
            // 로비로 내보내면 방을 새로 만들고 초대 코드를 다시 뿌려야 한다 —
            // 같이 하던 사람들이 그대로 있는데 같은 일을 두 번 하는 것이다.
            // 코드도 토큰도 자리도 살아 있으므로 런만 갈아끼우면 된다.
            //
            // 연결이 거절된 경우는 다르다. 토큰이 죽어 있어 이 방에 말을 걸
            // 수가 없으니 로비로 돌아가는 수밖에 없다.
            var mouse = BoardInput.Read().Mouse;
            var again = rejected == null;

            if (again)
            {
                if (Button(theme, new Rect(panel.center.x - 260f, panel.yMax - 96f, 250f, 52f),
                           "이 방에서 다시", HudTheme.Accent, mouse)) onRestart?.Invoke();
                if (Button(theme, new Rect(panel.center.x + 10f, panel.yMax - 96f, 250f, 52f),
                           "로비로 나가기", HudTheme.Rule, mouse)) onLobby?.Invoke();
            }
            else if (Button(theme, new Rect(panel.center.x - 130f, panel.yMax - 96f, 260f, 52f),
                            "로비로 — 다시 들어가기", HudTheme.Alert, mouse))
            {
                onLobby?.Invoke();
            }

            // **버튼을 눌렀는데 화면이 아무 말도 안 하면 안 눌린 것과 같다.**
            //
            // 재시작은 서버가 진행 중(409)·방 청소됨(404)·토큰 만료(401) 등으로
            // 거절할 수 있다. 그 결과를 여기서 안 그리면 실패가 그대로 사라져
            // 플레이어는 버튼이 고장난 줄 안다.
            if (again && restarting)
            {
                GUI.Label(new Rect(panel.x, panel.yMax - 40f, panel.width, 24f),
                    "다시 시작 중…",
                    theme.At(theme.Small, 14, HudTheme.Ink3, TextAnchor.MiddleCenter));
            }
            else if (again && !string.IsNullOrEmpty(restartError))
            {
                GUI.Label(new Rect(panel.x, panel.yMax - 40f, panel.width, 24f),
                    $"다시 시작 실패 — {restartError}",
                    theme.At(theme.Small, 14, HudTheme.Alert, TextAnchor.MiddleCenter));
            }

            return true;
        }

        /// <summary>눌렸으면 참. 마우스가 위에 있으면 밝아진다</summary>
        private static bool Button(HudTheme theme, Rect rect, string label, Color accent, Vector2 mouse)
        {
            var hot = rect.Contains(mouse);
            theme.Fill(rect, hot ? HudTheme.AccentW : HudTheme.Paper3);
            theme.Border(rect, hot ? accent : HudTheme.Rule, 2f);
            GUI.Label(rect, label,
                theme.At(theme.Heading, 19, hot ? accent : HudTheme.Ink, TextAnchor.MiddleCenter));
            return hot && Input.GetMouseButtonDown(0);
        }

        private static Color Accent(string status) => status switch
        {
            SnapshotStatusValues.Cleared => HudTheme.Accent,
            _ => HudTheme.Alert,
        };

        /// <summary>
        /// 무슨 일이 있었는지 한 줄로 + (퇴소라면) 소대장의 한마디.
        ///
        /// 판정이 왜 깨졌는지는 스냅샷의 `lastJudgement`가 들고 있지만, 런이
        /// 끝나면 그 스냅샷이 마지막이라 값이 그대로 남아 있다.
        /// </summary>
        private static (string, string, string, string) Words(string rejected, string status, Snapshot snapshot)
        {
            if (rejected != null)
            {
                return ("붙지 못했다", rejected, "",
                    "서버가 잠들었다 깨어나면 발급했던 토큰이 죽는다 — 로비에서 다시 들어가야 한다");
            }

            if (status == SnapshotStatusValues.Discharged)
            {
                var (line, quote) = FailureDetail(snapshot);
                return ("퇴소", line, quote,
                    "같은 방에서 다시 하면 인원도 코드도 그대로다 — 일과표만 새로 뽑힌다");
            }

            return status switch
            {
                SnapshotStatusValues.Cleared =>
                    ("전역", "18일을 끝까지 버텼다", "", "기록은 전적에 남는다"),
                SnapshotStatusValues.Disbanded =>
                    ("분대 해산", "남은 인원으로는 하루를 끝낼 수 없다", "",
                     "1~3인 방은 대리가 필수를 메우지만 합동은 사람이 시작해야 한다"),
                _ =>
                    ("퇴소", "점호 판정을 통과하지 못했다", "",
                     "같은 방에서 다시 하면 인원도 코드도 그대로다 — 일과표만 새로 뽑힌다"),
            };
        }

        /// <summary>
        /// C-1(실패의 언어화) — `judge.ts`의 `firstFailure`가 이미 계산해 둔
        /// `lastJudgement.failedAt`을 조건별 수치·대사로 옮긴다.
        ///
        /// **스냅샷에 실려 오는 값만 쓴다.** A는 `requiredDone`/`requiredTotal`,
        /// B는 합동 퀘스트의 `jointDone`/`jointTotal`(둘 다 이미 있는 퀘스트
        /// 필드), C는 `discipline.value`·`band`, D는 `firstFailure`의 위생 실측치 —
        /// 전부 스냅샷이 이미 들고 있다. 위생 판정 문턱(20)처럼 서버 규칙에만
        /// 있고 프로토콜에 안 실린 수치는 여기서 지어내지 않는다(ARCH-02).
        ///
        /// `firstFailure`(C-1)는 이 조건이 **처음** 결정타로 지목된 날을 함께
        /// 들고 온다 — 최종 실패일보다 이르면 "그날부터"를 덧붙여 "다 3일 전부터
        /// 삐걱댔다"를 한 줄로 보여준다("D조건: 위생 18/20 — 3일차 세면장 미완료가
        /// 결정타" 같은 문구가 여기서 나온다).
        /// </summary>
        private static (string, string) FailureDetail(Snapshot snapshot)
        {
            var j = snapshot?.lastJudgement;
            if (j == null || string.IsNullOrEmpty(j.failedAt))
                return ("점호 판정을 통과하지 못했다", "");

            var f = snapshot.firstFailure;
            var since = SinceClause(f, j);

            switch (j.failedAt)
            {
                case SnapshotLastJudgementFailedAtValues.A:
                    return (
                        $"A조건(필수 일과) 미달 — {j.requiredDone:0} / {j.requiredTotal:0}건 완료{since}",
                        "\"할 일을 남겼다. 부대는 그걸 안 봐준다.\"");

                case SnapshotLastJudgementFailedAtValues.B:
                {
                    var joint = HudScreens.FindJoint(snapshot);
                    var detail = joint != null ? $" — {joint.jointDone:0} / {joint.jointTotal:0}건" : "";
                    return (
                        $"B조건(합동 일과) 미달{detail}{since}",
                        "\"혼자 잘해서 되는 게 아니라고 했다.\"");
                }

                case SnapshotLastJudgementFailedAtValues.C:
                {
                    var disc = snapshot.discipline != null
                        ? $"{snapshot.discipline.value:0} · {snapshot.discipline.band}"
                        : "—";
                    return (
                        $"C조건(군기) 미달 — 군기 {disc}{since}",
                        "\"기본이 안 됐다.\"");
                }

                case SnapshotLastJudgementFailedAtValues.D:
                    return (
                        $"D조건(복장·위생) 미달 — {DGearDetail(snapshot, f)}",
                        "\"왜 미리 말 안 했나.\"");

                default:
                    return ("점호 판정을 통과하지 못했다", "");
            }
        }

        /// <summary>
        /// 조건 D의 결정타 문구. `firstFailure`가 위생 실측치와 인원을 들고 오면
        /// "김소총 위생 15/20 — 3일차 '세면 · 양치' 미완료가 결정타"처럼 조립하고,
        /// (구 스냅샷 등으로) 없으면 예전처럼 `missingGear` 배열로 되짚는다.
        ///
        /// `threshold`가 0이면 sim이 위생이 아니라 미보유 장비 건수를 실은 것이다
        /// (`judge.ts`의 `describeBreach` D분기 — 위생은 기준선이 20이라 절대 0이
        /// 될 수 없으므로 이 둘은 구분된다).
        /// </summary>
        private static string DGearDetail(Snapshot snapshot, SnapshotFirstFailure f)
        {
            if (f != null && !string.IsNullOrEmpty(f.memberName))
            {
                var quest = string.IsNullOrEmpty(f.questLabel)
                    ? ""
                    : $" — {f.day:0}일차 \"{f.questLabel}\" 미완료가 결정타";
                var measure = f.threshold > 0
                    ? $"위생 {f.value:0}/{f.threshold:0}"
                    : $"장비 미보유 {f.value:0}건";
                return $"{f.memberName} {measure}{quest}";
            }

            var bits = new System.Collections.Generic.List<string>();
            if (snapshot.members != null)
            {
                foreach (var m in snapshot.members)
                {
                    if (m == null || m.presence != SnapshotMembersItemPresenceValues.Player) continue;
                    if (m.missingGear != null && m.missingGear.Length > 0)
                        bits.Add($"{m.name} 미보유 {m.missingGear.Length}건");
                }
            }
            return bits.Count > 0 ? string.Join(" · ", bits) : "청결 또는 필수 장비 기준 미달";
        }

        /// <summary>
        /// 결정타 조건이 최종 실패일보다 먼저 걸린 적이 있으면 "· N일차부터"를
        /// 덧붙인다 — 구제·경고로 살아남은 하루가 있었다는 뜻이라 다음 판 전략에
        /// "언제부터 손을 놨는지"를 알려준다.
        /// </summary>
        private static string SinceClause(SnapshotFirstFailure f, SnapshotLastJudgement j)
        {
            if (f == null || j == null) return "";
            if (f.day < j.day) return $" · {f.day:0}일차부터";
            return "";
        }
    }
}
