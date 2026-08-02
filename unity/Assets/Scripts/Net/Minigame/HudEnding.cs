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

            var rejected = client.Rejected;
            var status = client.Latest?.status;
            var ended = !string.IsNullOrEmpty(status) &&
                        status != SnapshotStatusValues.Running;

            if (string.IsNullOrEmpty(rejected) && !ended) return false;

            theme.Fill(new Rect(0f, 0f, HudTheme.ViewWidth, HudTheme.ViewHeight),
                       HudTheme.Dim, 0.92f);

            var panel = new Rect(HudTheme.ViewWidth * 0.5f - 380f,
                                 HudTheme.ViewHeight * 0.5f - 190f, 760f, 380f);
            theme.Fill(panel, HudTheme.Paper2);
            theme.Border(panel, rejected != null ? HudTheme.Alert : Accent(status), 2f);

            var (title, line, hint) = Words(rejected, status);

            GUI.Label(new Rect(panel.x, panel.y + 44f, panel.width, 40f),
                rejected != null ? "연결 끊김" : "런 종료",
                theme.At(theme.Label, 13, HudTheme.Ink2, TextAnchor.MiddleCenter));

            GUI.Label(new Rect(panel.x, panel.y + 82f, panel.width, 60f), title,
                theme.At(theme.Display, 44, rejected != null ? HudTheme.Alert : Accent(status),
                    TextAnchor.MiddleCenter));

            GUI.Label(new Rect(panel.x + 40f, panel.y + 156f, panel.width - 80f, 30f), line,
                theme.At(theme.Body, 17, HudTheme.Ink, TextAnchor.MiddleCenter));

            GUI.Label(new Rect(panel.x + 40f, panel.y + 192f, panel.width - 80f, 30f), hint,
                theme.At(theme.Small, 14, HudTheme.Ink3, TextAnchor.MiddleCenter));

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
        /// 무슨 일이 있었는지 한 줄로.
        ///
        /// 판정이 왜 깨졌는지는 스냅샷의 `lastJudgement`가 들고 있지만, 런이
        /// 끝나면 그 스냅샷이 마지막이라 값이 그대로 남아 있다.
        /// </summary>
        private static (string, string, string) Words(string rejected, string status)
        {
            if (rejected != null)
            {
                return ("붙지 못했다", rejected,
                    "서버가 잠들었다 깨어나면 발급했던 토큰이 죽는다 — 로비에서 다시 들어가야 한다");
            }

            return status switch
            {
                SnapshotStatusValues.Cleared =>
                    ("전역", "18일을 끝까지 버텼다", "기록은 전적에 남는다"),
                SnapshotStatusValues.Disbanded =>
                    ("분대 해산", "남은 인원으로는 하루를 끝낼 수 없다",
                     "1~3인 방은 대리가 필수를 메우지만 합동은 사람이 시작해야 한다"),
                _ =>
                    ("퇴소", "점호 판정을 통과하지 못했다",
                     "같은 방에서 다시 하면 인원도 코드도 그대로다 — 일과표만 새로 뽑힌다"),
            };
        }
    }
}
