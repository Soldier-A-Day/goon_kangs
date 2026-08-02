using UnityEngine;

namespace SoldierADay.Net
{
    /// <summary>
    /// `SEARCH` 탐색 — 4건. 환자 발생 점검 · 보일러실 순찰 · 울타리 순찰 · 분실 장비 수색.
    ///
    /// 화면을 훑다가 신호가 세지는 곳을 찍는다. 조작은 커서와 클릭 하나뿐이다.
    ///
    /// **찾기 놀이가 되면 안 된다.** 아무 단서 없이 넓은 화면을 훑게 하면 그건
    /// 운이고, 20초 안에 되는 일도 아니다. 그래서 `signal` 반경 안에서는 신호가
    /// 세지고, 커서 주변만 밝아진다 — 어디를 이미 봤는지가 화면에 남으므로
    /// 훑는 순서가 실력이 된다.
    ///
    /// 헛짚어도 실패는 아니다. 실수로만 세고, 못 찾은 채 시간이 다하면 재시도다.
    /// </summary>
    public sealed class SearchBoard : Board
    {
        private Vector2[] _hidden;
        private bool[] _found;
        private bool[] _swept;
        private int _cols;
        private int _rows;
        private int _count;
        private float _signal;
        private float _strength;
        private float _ping;
        private Rect _area;

        /// <summary>H-4 — 훑는 자리를 마우스 대신 가상 커서에서 읽는다(Board.cs §VirtualCursor)</summary>
        private readonly VirtualCursor _cursor = new VirtualCursor();

        /// <summary>대상마다 배정된 발견 카드 문구. `Setup`에서 시드 고정 `Rng`로 하나씩 뽑는다</summary>
        private string[] _cardText;
        /// <summary>화면 위에 뜬 발견 카드가 남은 표시 시간(초). 0이면 안 보인다</summary>
        private float _cardT;
        private string _cardLabel;

        /// <summary>실패 리플레이가 깜빡이는 창(초) — 결과 화면이 뜬 처음 이만큼만 보여준다</summary>
        private const float RevealWindow = 1.4f;

        /// <summary>
        /// 변형별 발견 카드 문구 — 무엇을 찾았는지 한 명사구로.
        ///
        /// "운이 나빴다"가 아니라 "저기 있었구나"를 남기려면 찾는 순간 그게
        /// 무엇인지 말해줘야 한다. 톤은 내무반 인수인계 수첩처럼 건조하게 —
        /// 감상을 붙이지 않는다.
        /// </summary>
        private static readonly string[] SeepCards = { "배관 이음새 누수" };
        private static readonly string[] BunkCards = { "관물대 밑 편지", "열이 오른 관자놀이" };
        private static readonly string[] WirecutCards = { "절단된 철조망 2겹" };
        private static readonly string[] GridCards = { "모래에 반쯤 묻힌 수통" };

        private static string[] CardPool(string variant) => variant switch
        {
            "seep" => SeepCards,
            "bunk" => BunkCards,
            "wirecut" => WirecutCards,
            "grid" => GridCards,
            _ => SeepCards,
        };

        public override string Instruction => "훑다가 신호가 세지면 눌러라 — 화살표+Space도 된다";

        public override string Status
        {
            get
            {
                var swept = 0;
                if (_swept != null) foreach (var s in _swept) if (s) swept += 1;
                var pct = _swept == null || _swept.Length == 0 ? 0 : swept * 100 / _swept.Length;
                return $"발견 {_count}/{_hidden?.Length ?? 0}  ·  수색 {pct}%";
            }
        }

        /// <summary>
        /// **시간초과 통과 계약(S1 발주 · `Board.GradesOnTimeout`).**
        ///
        /// 수색은 헛짚어도 실수로만 셀 뿐 실패가 아니다(클래스 요약 참고) — 그런데
        /// 시간이 다 되면 베이스가 무조건 실패로 닫아버려서, 절반 넘게 찾아 놓고도
        /// "못 했다"가 뜨는 건 이상하다. 이 스위치를 켜면 베이스가 시간초과
        /// 시점의 `Fill`이 0.5 이상이면 통과로 쳐준다 — 판정 자체는 `Board.Tick`이
        /// 한다. 여기서는 자격만 연다.
        ///
        /// 이 워크트리에는 아직 베이스에 `GradesOnTimeout`/`TimedOut`이 없다 —
        /// 병렬로 도는 S1이 `Board.cs`에 추가하는 중이라 그렇다. 병합 전까지
        /// 컴파일이 안 맞는 게 정상이고, 병합되면 맞물린다.
        /// </summary>
        protected override bool GradesOnTimeout => true;

        protected override void Setup()
        {
            var count = Mathf.Clamp(ParamInt("hidden", 3), 1, 6);
            _hidden = new Vector2[count];
            _found = new bool[count];
            _cardText = new string[count];
            _cardT = 0f;
            _cardLabel = "";
            _count = 0;
            _signal = Param("signal", 0.2f);

            var cells = Mathf.Clamp(ParamInt("cells", 24), 12, 48);
            _cols = Mathf.Clamp(Mathf.RoundToInt(Mathf.Sqrt(cells * 2f)), 4, 12);
            _rows = Mathf.Max(3, Mathf.CeilToInt((float)cells / _cols));
            _swept = new bool[_cols * _rows];

            // 변형별 발견 카드 문구를 대상마다 하나씩 배정한다. 시드 고정 Rng라
            // 같은 questId면 매번 같은 대상이 같은 문구를 낸다.
            var cards = CardPool(Spec?.variant);
            for (var i = 0; i < count; i += 1)
            {
                // 가장자리에 붙이지 않는다 — 구석부터 찍는 것이 최적해가 되면 안 된다
                _hidden[i] = new Vector2(RandRange(0.12f, 0.88f), RandRange(0.14f, 0.86f));
                _cardText[i] = cards[Rng.Next(cards.Length)];
            }
        }

        protected override void Advance(float dt, BoardInput input)
        {
            _ping = Mathf.Max(0f, _ping - dt * 2f);
            _cardT = Mathf.Max(0f, _cardT - dt);
            if (_area.width <= 0f) return;

            // H-4 — 커서 자리를 가상 커서에서 읽는다. 마우스로 훑을 때는 매
            // 프레임 마우스 좌표로 스냅되니 예전과 같고, 화살표+WASD로도 같은
            // `norm`(0~1)을 만들어낸다(Board.cs §VirtualCursor)
            _cursor.Tick(dt, input, _area);
            var norm = _cursor.Norm;

            // 훑은 자리는 남는다. 어디를 이미 봤는지가 보여야 순서가 실력이 된다
            var cx = Mathf.Clamp(Mathf.FloorToInt(norm.x * _cols), 0, _cols - 1);
            var cy = Mathf.Clamp(Mathf.FloorToInt(norm.y * _rows), 0, _rows - 1);
            _swept[cy * _cols + cx] = true;

            // 가장 가까운 미발견 대상까지의 거리 → 신호 세기
            _strength = 0f;
            var nearest = -1;
            for (var i = 0; i < _hidden.Length; i += 1)
            {
                if (_found[i]) continue;
                var d = Vector2.Distance(norm, _hidden[i]);
                var s = 1f - Mathf.Clamp01(d / _signal);
                if (s <= _strength) continue;
                _strength = s;
                nearest = i;
            }

            // 클릭 대신 Space·Enter도 받는다(`Confirm` — Board.cs §BoardInput)
            if (!input.Confirm) return;

            // 신호가 충분히 센 자리에서만 잡힌다. 난이도가 오르면 더 가까이 가야 한다
            var need = Mathf.Lerp(0.45f, 0.66f, (Difficulty - 1f) * 0.5f);
            if (nearest >= 0 && _strength >= need)
            {
                _found[nearest] = true;
                _count += 1;
                _ping = 1f;
                // 발견 카드 — 무엇을 찾았는지 1초쯤 화면 위에 뜬다
                _cardLabel = _cardText[nearest];
                _cardT = 1f;
                Fill = (float)_count / _hidden.Length;
                if (_count >= _hidden.Length) Clear();
                return;
            }

            Miss();
        }

        public override void Draw(HudTheme theme, Rect body)
        {
            _area = body;
            if (_hidden == null) return;

            theme.Fill(body, HudTheme.Rule3);

            // 훑지 않은 칸은 덮여 있다 — 수색은 덮인 것을 걷어내는 일이다
            var cw = body.width / _cols;
            var ch = body.height / _rows;
            for (var y = 0; y < _rows; y += 1)
            for (var x = 0; x < _cols; x += 1)
            {
                if (_swept[y * _cols + x]) continue;
                theme.Fill(new Rect(body.x + x * cw, body.y + y * ch, cw + 1f, ch + 1f),
                           HudTheme.Paper, 0.92f);
            }

            // 찾은 것
            for (var i = 0; i < _hidden.Length; i += 1)
            {
                if (!_found[i]) continue;
                var p = new Vector2(body.x + _hidden[i].x * body.width,
                                    body.y + _hidden[i].y * body.height);
                var mark = new Rect(p.x - 15f, p.y - 15f, 30f, 30f);
                theme.Fill(mark, HudTheme.AccentW);
                theme.Border(mark, HudTheme.Accent, 2f);
            }

            // **실패 리플레이.** 판이 끝났는데(통과든 실패든) 못 찾은 대상이 남아
            // 있으면, 결과 화면이 뜬 처음 `RevealWindow`초 동안 그 자리가 깜빡이며
            // 드러난다. "운이 나빴다"가 아니라 "저기 있었구나"를 남기는 장치라
            // 통과·실패 양쪽에서 다 그린다 — 시간초과 통과(`GradesOnTimeout`)에도
            // 미발견분은 남을 수 있다. `HudMinigame`이 판이 멈춘 뒤에도 `Draw`를
            // 계속 부르므로 `ResultAge` 기준으로 여기서 직접 시간을 잰다.
            if (State != BoardState.Running && _count < _hidden.Length && ResultAge < RevealWindow)
            {
                var blink = Mathf.Repeat(ResultAge * 5f, 1f) < 0.5f;
                if (blink)
                {
                    for (var i = 0; i < _hidden.Length; i += 1)
                    {
                        if (_found[i]) continue;
                        var p = new Vector2(body.x + _hidden[i].x * body.width,
                                            body.y + _hidden[i].y * body.height);
                        var mark = new Rect(p.x - 15f, p.y - 15f, 30f, 30f);
                        theme.Fill(mark, HudTheme.AlertW);
                        theme.Border(mark, HudTheme.Alert, 2f);
                    }
                }
            }

            // 신호 — 커서에 붙어 세기를 말한다. 가상 커서 자리라 마우스든
            // 화살표+WASD든 같은 원이 따라온다
            var cursorPos = _cursor.Screen(body);
            if (body.Contains(cursorPos))
            {
                var r = Mathf.Lerp(46f, 20f, _strength);
                theme.Border(new Rect(cursorPos.x - r, cursorPos.y - r, r * 2f, r * 2f),
                    _strength > 0.6f ? HudTheme.Accent
                    : _strength > 0.25f ? HudTheme.Heat
                    : HudTheme.Rule, 2f);
            }

            var meter = new Rect(body.x + 16f, body.yMax - 34f, 240f, 16f);
            theme.Bar(meter, _strength,
                _strength > 0.6f ? HudTheme.Accent : HudTheme.Heat);
            GUI.Label(new Rect(meter.xMax + 12f, meter.y - 3f, 200f, 22f),
                _ping > 0f ? "찾았다" : _strength > 0.6f ? "가깝다" : _strength > 0.25f ? "무언가 있다" : "신호 없음",
                theme.At(theme.Small, 14, _ping > 0f ? HudTheme.Accent : HudTheme.Ink3));

            // **발견 카드.** 찾는 순간 무엇을 찾았는지가 위쪽에 1초쯤 뜬다 — 몇
            // 번째 발견인지가 아니라 "무엇을" 찾았는지가 남아야 다음 판에서
            // 이 자리를 기억하는 데 쓸모가 있다. 남은 표시 시간(`_cardT`)의
            // 마지막 0.25초를 알파로 접어 사라진다.
            if (_cardT > 0f)
            {
                var alpha = Mathf.Clamp01(_cardT / 0.25f);
                var prev = GUI.color;
                GUI.color = new Color(1f, 1f, 1f, alpha);
                const float w = 300f;
                var card = new Rect(body.center.x - w * 0.5f, body.y + 12f, w, 32f);
                theme.Fill(card, HudTheme.AccentW);
                theme.Border(card, HudTheme.Accent, 2f);
                GUI.Label(card, _cardLabel,
                    theme.At(theme.Body, 15, HudTheme.Accent, TextAnchor.MiddleCenter));
                GUI.color = prev;
            }

            theme.Border(body, HudTheme.Rule);
        }
    }
}
