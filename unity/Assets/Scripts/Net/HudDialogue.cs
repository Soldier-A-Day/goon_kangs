using System.Collections.Generic;

namespace SoldierADay.Net
{
    /// <summary>
    /// C-1b "값싼 정서 잔여" — 규칙은 그대로 두고, 이미 계산돼 있지만 화면에
    /// 안 나가던 값을 문장으로 옮긴다(WORKORDER.md C-1b). 점호 실패·후송·돌발·
    /// 무전 전환 네 지점의 문구를 한곳에 모았다 — <see cref="HudTheme"/>가 이미
    /// `DelegationRefusalText`로 이 선례를 만들어 뒀으므로 그 관례(순수 static
    /// 문자열 테이블 + 조건 → 문구 스위치)를 그대로 따른다.
    ///
    /// **경계(WORKORDER C-1b에서 확정)**: 실존 부대·사건을 가리키지 않는다,
    /// 가혹행위를 미니게임 소재로 쓰지 않는다(사건은 한두 문장으로 끝나고
    /// 수행은 기존 퀘스트 UI가 그대로 맡는다), 가해자를 이분법으로 그리지
    /// 않는다(소대장도 위에 보고해야 하는 사람이다 — 아래 조건 C 세 번째 줄
    /// 참고), 혐오 은어를 쓰지 않는다. 톤은 D.P.의 폭로가 아니라 신병·Finnish
    /// Army Simulator 쪽 — 반복 플레이와 맞는 무게의 블랙유머로 완충한다.
    /// </summary>
    public static class HudDialogue
    {
        /// <summary>
        /// 문구 풀에서 결정론으로 하나를 고른다.
        ///
        /// **일차 + 키로 시드를 잡는다.** `Random`을 쓰면 같은 사건을 다시 봐도
        /// (재접속·화면 다시 그리기) 매번 다른 대사가 나온다 — 그러면 "그 사람이
        /// 한 말"이 아니라 "화면이 아무 말이나 낸다"가 된다. 일차는 서버 판정값,
        /// 키(조건·id·문자열)도 이벤트가 실어오는 값이라 재접속해도 같은 입력이
        /// 들어오고 같은 대사가 나온다. `string.GetHashCode`는 런타임마다 값이
        /// 달라질 수 있어(해시 시드 랜덤화) 쓰지 않는다 — 문자 코드를 직접 더한다.
        /// </summary>
        private static int Pick(int day, string key, int poolLength)
        {
            if (poolLength <= 1) return 0;
            var h = 0;
            if (!string.IsNullOrEmpty(key))
                foreach (var c in key) h += c;
            var index = (day * 7 + h) % poolLength;
            if (index < 0) index += poolLength;
            return index;
        }

        /* ═══════════════════════════════ 점호 실패 소대장 대사 (조건 A~D) */

        // 조건 A — 필수 퀘스트 미달(judge.ts conditionA). "기본이 안 됐다"는
        // 결과를, "왜 미리 말 안 했나"는 그 결과를 아무도 보고하지 않은 과정을
        // 짚는다 — 같은 미달이라도 소대장이 화내는 지점이 다르다.
        private static readonly string[] RollCallA =
        {
            "기본이 안 됐다.",
            "왜 미리 말을 안 했나.",
            "이걸 못 채우고 점호에 서겠다는 건가.",
        };

        // 조건 B — 합동 퀘스트 미달. 혼자 잘한다고 넘어가는 조건이 아니라서
        // 대사도 "너"가 아니라 "우리"·"손발"을 겨눈다.
        private static readonly string[] RollCallB =
        {
            "넷이 있는데 하나가 안 맞았다.",
            "손발을 맞췄어야지, 혼자 하는 일이 아니다.",
            "누가 뭘 하는지도 모르고 있었다는 소리다.",
        };

        // 조건 C — 분대 군기 < 40. 세 번째 줄은 소대장 본인도 위에 답해야 하는
        // 처지라는 것을 흘린다 — 가해자·피해자 이분법을 피하는 자리다.
        private static readonly string[] RollCallC =
        {
            "군기가 이 지경이 될 때까지 뭘 했나.",
            "느슨해진 걸 왜 아무도 말을 안 했나.",
            "이러면 나도 위에 할 말이 없어진다.",
        };

        // 조건 D — 복장·장비·청결 미달. 몸 하나 챙기는 문제라 대사도 사적이다.
        private static readonly string[] RollCallD =
        {
            "이 꼴로 점호를 서겠다는 건가.",
            "장비 하나를 못 챙기나.",
            "기본 중의 기본이 이거였다.",
        };

        private static readonly string[] RollCallFallback = { "오늘은 여기서 끝이다." };

        /// <summary>점호 판정 화면(<c>HudScreens.DrawRollCall</c>)이 실패한 줄 아래
        /// 붙이는 소대장 대사 한 줄을 고른다</summary>
        public static string RollCallFailureLine(string condition, int day)
        {
            var lines = condition switch
            {
                "A" => RollCallA,
                "B" => RollCallB,
                "C" => RollCallC,
                "D" => RollCallD,
                _ => RollCallFallback,
            };
            return lines[Pick(day, condition, lines.Length)];
        }

        /* ══════════════════════════════════════════════ 후송 — 남은 인원 */

        // 남은 인원이 0명·1명일 때는 "남은 셋이" 같은 정형 문장이 문법적으로도
        // 어색해진다(넷 중 셋이 실려 나간 뒤 마지막 한 명에게 "남은 하나가
        // 채운다"는 이상하다 — 혼자 남았다는 사실 자체가 더 무겁다). 그래서
        // 인원 수 구간별로 문장 형태를 따로 둔다.
        private static readonly string[] EvacuatedNone =
        {
            "이제 아무도 남지 않았다.",
            "더 이상 남은 사람이 없다.",
            "그 몫을 나눌 사람도 없다.",
        };

        private static readonly string[] EvacuatedOne =
        {
            "혼자 남아 그 몫까지 진다.",
            "이제 혼자다. 그 몫도 떠안는다.",
            "남은 건 한 사람뿐이다.",
        };

        // {0} = "셋이" 같은 "수사+조사" 조합. 문장 뼈대만 3종 두고 조사는
        // `HudTheme.Josa`로 맞춘다(분대원 이름과 같은 이유 — 수사도 받침 유무가
        // 갈린다. "둘이"·"셋이"는 받침, "하나가"는 무받침).
        private static readonly string[] EvacuatedManyTemplate =
        {
            "남은 {0} 그 몫까지 채운다.",
            "남은 {0} 그만큼을 나눠 진다.",
            "{0} 남아 그 자리를 메운다.",
        };

        /// <summary>순우리말 수사 — 10명 넘는 편성은 없으므로(§13.1 4인 협동)
        /// 그 이상은 안전망으로만 둔다</summary>
        private static string CountWord(int n) => n switch
        {
            1 => "하나",
            2 => "둘",
            3 => "셋",
            4 => "넷",
            5 => "다섯",
            6 => "여섯",
            7 => "일곱",
            8 => "여덟",
            9 => "아홉",
            10 => "열",
            _ => $"{n}명",
        };

        /// <summary>후송 토스트(`Hud.OnEvent` MemberEvacuated) 문장. 남은 인원 수는
        /// 호출부가 스냅샷을 세어 넘긴다 — 여기서는 문장만 만든다</summary>
        public static string EvacuationLine(string name, int remaining, int day)
        {
            var subject = name + HudTheme.Josa(name, "이", "가");
            if (remaining <= 0) return $"{subject} 실려 나갔다. {EvacuatedNone[Pick(day, name, EvacuatedNone.Length)]}";
            if (remaining == 1) return $"{subject} 실려 나갔다. {EvacuatedOne[Pick(day, name, EvacuatedOne.Length)]}";

            var word = CountWord(remaining);
            var phrase = word + HudTheme.Josa(word, "이", "가");
            var template = EvacuatedManyTemplate[Pick(day, name, EvacuatedManyTemplate.Length)];
            return $"{subject} 실려 나갔다. " + string.Format(template, phrase);
        }

        /* ══════════════════════════════════════════════════ 돌발 사건 도입 */

        /// <summary>
        /// 돌발 퀘스트 9종(`packages/sim/data/quests.json`의 `surprise`) 도입
        /// 문장 — id로 갈라 "무슨 일이 지금 벌어졌는가"만 1~2문장으로 던진다.
        /// 실제 퀘스트 이름·수행법은 토스트 태그와 기존 퀘스트 UI가 그대로 맡는다
        /// (사건은 문장으로 끝난다 — 가혹행위 미니게임화 금지, WORKORDER C-1b).
        /// 모르는 id(향후 추가분)는 공용 문구로 떨어진다.
        /// </summary>
        private static readonly Dictionary<string, string[]> SurpriseLines = new Dictionary<string, string[]>
        {
            ["sur-inspection"] = new[]
            {
                "복도에서 발소리가 다가온다. 예고 없는 점검이다.",
                "문이 갑자기 열렸다. 오늘은 그냥 넘어가는 날이 아니다.",
                "누가 온다는 말도 없이 명단부터 부른다.",
            },
            ["sur-patient"] = new[]
            {
                "누군가 배를 움켜쥐고 주저앉았다.",
                "저쪽에서 신음이 들린다. 누가 안 좋다.",
                "한 명이 얼굴이 하얘져서 벽을 짚었다.",
            },
            ["sur-truck"] = new[]
            {
                "위병소에서 트럭 경적이 울린다. 지금 받아야 한다.",
                "트럭이 들어왔다는 무전이 왔다. 놓치면 다음 순번을 기다려야 한다.",
                "정문에서 짐을 부리라는 연락이 왔다.",
            },
            ["sur-lecture"] = new[]
            {
                "행정반에서 전화가 왔다. \"지금 바로 오세요.\"",
                "스피커에서 소집 방송이 나온다. 이유는 나중에 안다.",
                "메모 한 장이 붙었다. 집합 시간과 장소뿐이다.",
            },
            ["sur-leak"] = new[]
            {
                "천장에서 물이 떨어지고 전등이 하나둘 꺼진다.",
                "두꺼비집이 나갔다. 어디서부터 손대야 할지 모르겠다.",
                "바닥에 물이 고이기 시작했다. 누가 먼저 알아챘다.",
            },
            ["sur-lost"] = new[]
            {
                "인원·장비를 세다가 하나가 빈 걸 알았다.",
                "마지막으로 봤다는 사람이 아무도 없다.",
                "확인 서명란에 물음표가 하나 붙었다.",
            },
            ["sur-officer"] = new[]
            {
                "복도 끝에서 낯익은 발소리 — 오늘 순찰이 도는 날이다.",
                "계급장이 먼저 보였다. 정리할 시간이 없다.",
                "누가 창밖을 보더니 자세를 고쳐 앉는다.",
            },
            ["sur-help"] = new[]
            {
                "옆 분대에서 무전이 왔다. 손이 모자란다고 한다.",
                "다른 생활관에서 사람을 보내 달라는 연락이 왔다.",
                "무전으로 다급한 목소리가 들린다. 급한 모양이다.",
            },
            ["sur-extra"] = new[]
            {
                "위에서 한마디 던졌다. \"저기도 마저 해라.\"",
                "끝난 줄 알았던 구역이 다시 지정됐다.",
                "장비를 정리하는데 한 구역이 더 남았다는 말이 돌아온다.",
            },
        };

        private static readonly string[] SurpriseFallback =
        {
            "무전이 다급하게 울린다. 예정에 없던 일이 떨어졌다.",
            "갑자기 뭔가 끼어들었다. 자세한 건 가서 알게 된다.",
            "일정에 없던 호출이 왔다.",
        };

        public static string SurpriseIntro(string questId, int day)
        {
            var lines = questId != null && SurpriseLines.TryGetValue(questId, out var found)
                ? found
                : SurpriseFallback;
            return lines[Pick(day, questId, lines.Length)];
        }

        /* ══════════════════════════════════════════════ 무전 상태 — 보직별 */

        // down·comms — 두절이 자기 손에서 시작됐다는 것을 아는 유일한 사람
        private static readonly string[] RadioDownComms =
        {
            "네가 못 고쳐서 끊겼다.",
            "네 손이 늦었다 — 안테나가 죽었다.",
            "네가 마지막으로 만진 뒤로 먹통이다.",
        };

        private static readonly string[] RadioDownOther =
        {
            "무전이 끊겼다 — 통신병을 찾아야 한다.",
            "미니맵이 비었다. 무전이 죽었다.",
            "연락이 끊겼다. 직접 모이는 수밖에 없다.",
        };

        private static readonly string[] RadioWeakComms =
        {
            "네 손이 늦어서 흐려졌다.",
            "네가 미룬 만큼 잡음이 늘었다.",
            "완전히 죽기 전에 네가 봐야 한다.",
        };

        private static readonly string[] RadioWeakOther =
        {
            "무전이 흐려졌다.",
            "마커가 깜빡인다. 신호가 약하다.",
            "목소리가 자꾸 끊긴다.",
        };

        private static readonly string[] RadioOkComms =
        {
            "네가 다시 잡았다.",
            "네 손으로 되돌렸다.",
            "고친 건 너다.",
        };

        private static readonly string[] RadioOkOther =
        {
            "무전이 돌아왔다.",
            "마커가 다시 선명하다.",
            "연결이 다시 잡혔다.",
        };

        /// <summary>
        /// 무전 전환 토스트(`radioChanged`, `Hud.OnEvent`)의 태그·문장·강조색.
        ///
        /// 규칙(끊김·복구 판정)은 그대로 `packages/sim/src/radio.ts` 소유다 — 여기서
        /// 갈리는 것은 **같은 사건을 옮기는 문장**뿐이다(클라 분기만). `radio.ts`가
        /// 상태 변화가 있을 때만 이벤트를 낸다(`step.ts` — `if (radio) effects.push(...)`)
        /// 이므로 "ok" 도착은 항상 직전 상태에서의 복구다.
        /// </summary>
        public static (string tag, string text, UnityEngine.Color color) RadioChangeLine(
            string radioState, bool isComms, int day)
        {
            var key = radioState + (isComms ? "|comms" : "|other");
            switch (radioState)
            {
                case "down":
                    var down = isComms ? RadioDownComms : RadioDownOther;
                    return ("무전 두절", down[Pick(day, key, down.Length)], HudTheme.Alert);
                case "weak":
                    var weak = isComms ? RadioWeakComms : RadioWeakOther;
                    return ("무전 약함", weak[Pick(day, key, weak.Length)], HudTheme.Heat);
                default:
                    var ok = isComms ? RadioOkComms : RadioOkOther;
                    return ("무전 복구", ok[Pick(day, key, ok.Length)], HudTheme.Accent);
            }
        }
    }
}
