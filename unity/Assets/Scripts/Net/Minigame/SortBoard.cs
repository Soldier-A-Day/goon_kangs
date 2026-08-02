using UnityEngine;

namespace SoldierADay.Net
{
    /// <summary>
    /// `SORT` 분류 — 4건. 쓰레기 3분류 · 세탁물 · 의약품 선입선출 · 신분증 판정.
    ///
    /// 앞으로 나온 물건을 통에 던져 넣는다. 클릭 하나뿐이다.
    ///
    /// **`ambiguous`가 이 판의 전부다.** 젖은 종이는 종이인가 일반인가, 오늘
    /// 만료된 약은 쓰는 것인가 버리는 것인가 — 애매한 품목이 없으면 분류는
    /// 색깔 맞추기가 되고, 아무 생각 없이 손만 빨라진다.
    ///
    /// 애매한 것은 **표시로 알려준다.** 헷갈리게 만드는 것과 안 보이게 만드는
    /// 것은 다르다. 답이 하나로 안 정해지는 물건이라는 것을 알려주고, 판단은
    /// 시키되 실수해도 오답 한도 안에서 되돌릴 수 있게 둔다.
    /// </summary>
    public sealed class SortBoard : Board
    {
        private sealed class Item
        {
            public string Name;
            public int Bin;
            public bool Ambiguous;
        }

        private Item[] _items;
        private int _at;
        private int _bins;
        private int _strikes;
        private int _limit;
        private float _wrongFlash;
        private int _lastWrong = -1;
        private Rect _area;

        public override string Instruction => "물건을 보고 통을 눌러라";

        public override string Status =>
            $"{_at}/{_items?.Length ?? 0} 분류  ·  오답 {_strikes}/{_limit}";

        private static readonly string[][] Sets =
        {
            // recycle — 쓰레기 3분류
            new[] { "일반", "재활용", "음식물" },
            // nametag — 세탁물 분대별
            new[] { "1분대", "2분대", "3분대", "4분대" },
            // expiry — 의약품 선입선출
            new[] { "먼저 쓸 것", "나중 것", "폐기" },
            // idcheck — 정문 출입
            new[] { "통과", "차단" },
        };

        private string[] _bin;

        protected override void Setup()
        {
            _bins = Mathf.Clamp(ParamInt("bins", 3), 2, 4);
            _limit = Mathf.Max(1, ParamInt("strikes", 3));
            _strikes = 0;
            _at = 0;

            _bin = Spec?.variant switch
            {
                "nametag" => Sets[1],
                "expiry" => Sets[2],
                "idcheck" => Sets[3],
                _ => Sets[0],
            };
            if (_bin.Length < _bins)
            {
                var grown = new string[_bins];
                for (var i = 0; i < _bins; i += 1) grown[i] = i < _bin.Length ? _bin[i] : $"{i + 1}번";
                _bin = grown;
            }

            var count = Mathf.Clamp(ParamInt("items", 14), 4, 24);
            var ambiguous = Mathf.Clamp(ParamInt("ambiguous", 3), 0, count);

            _items = new Item[count];
            for (var i = 0; i < count; i += 1)
            {
                var bin = Rng.Next(Mathf.Min(_bins, _bin.Length));
                _items[i] = new Item { Bin = bin, Name = NameFor(bin, false) };
            }

            var picked = 0;
            var guard = 0;
            while (picked < ambiguous && guard++ < 200)
            {
                var i = Rng.Next(count);
                if (_items[i].Ambiguous) continue;
                _items[i].Ambiguous = true;
                _items[i].Name = NameFor(_items[i].Bin, true);
                picked += 1;
            }
        }

        private string NameFor(int bin, bool ambiguous) => Spec?.variant switch
        {
            "nametag" => ambiguous ? "이름표 지워짐" : $"관등성명 {(char)('가' + Rng.Next(12))}{Rng.Next(100, 999)}",
            "expiry" => ambiguous ? "오늘 만료" : $"유효 D+{Rng.Next(1, 120)}",
            "idcheck" => ambiguous ? "사진 훼손 신분증" : (bin == 0 ? "정상 신분증" : "미등록 차량"),
            _ => ambiguous
                ? new[] { "젖은 종이", "코팅 상자", "기름 묻은 비닐" }[Rng.Next(3)]
                : new[] { "종이컵", "페트병", "잔반", "휴지", "캔", "우유팩" }[Rng.Next(6)],
        };

        protected override void Advance(float dt, BoardInput input)
        {
            _wrongFlash = Mathf.Max(0f, _wrongFlash - dt * 2.2f);
            if (!input.Pressed || _items == null || _at >= _items.Length) return;
            if (_area.width <= 0f) return;

            for (var b = 0; b < _bins; b += 1)
            {
                if (!BinRect(b).Contains(input.Mouse)) continue;

                var item = _items[_at];
                if (b == item.Bin)
                {
                    _at += 1;
                    Fill = (float)_at / _items.Length;
                    if (_at >= _items.Length) Clear();
                    return;
                }

                // 오분류. 물건은 그대로 남는다 — 다시 판단해야 한다
                _strikes += 1;
                _wrongFlash = 1f;
                _lastWrong = b;
                Miss();
                if (_strikes >= _limit) Fail();
                return;
            }
        }

        private Rect BinRect(int index)
        {
            var w = (_area.width - 40f) / _bins;
            return new Rect(_area.x + 20f + index * w, _area.yMax - 132f, w - 14f, 112f);
        }

        public override void Draw(HudTheme theme, Rect body)
        {
            _area = body;
            if (_items == null) return;

            theme.Fill(body, HudTheme.Paper3);

            // 지금 물건 — 판의 주인공이라 크게
            var card = new Rect(body.center.x - 180f, body.y + 26f, 360f, 104f);
            if (_at < _items.Length)
            {
                var item = _items[_at];
                theme.Fill(card, item.Ambiguous ? HudTheme.Paper2 : HudTheme.Paper);
                theme.Border(card, item.Ambiguous ? HudTheme.Heat : HudTheme.Rule, 2f);
                GUI.Label(new Rect(card.x, card.y + 22f, card.width, 34f), item.Name,
                    theme.At(theme.Title, 24, HudTheme.Ink, TextAnchor.MiddleCenter));
                GUI.Label(new Rect(card.x, card.y + 62f, card.width, 24f),
                    item.Ambiguous ? "애매하다 — 규정을 떠올려라" : "",
                    theme.At(theme.Small, 14, HudTheme.Heat, TextAnchor.MiddleCenter));
            }

            // 남은 줄 — 뒤에 몇 개나 있는가
            var queue = Mathf.Min(6, _items.Length - _at - 1);
            for (var i = 0; i < queue; i += 1)
            {
                theme.Fill(new Rect(card.xMax + 16f + i * 14f, card.center.y - 14f, 10f, 28f),
                           HudTheme.Rule2);
            }

            // 통
            for (var b = 0; b < _bins; b += 1)
            {
                var rect = BinRect(b);
                var wrong = _wrongFlash > 0f && b == _lastWrong;
                theme.Fill(rect, wrong ? HudTheme.AlertW : HudTheme.Paper2);
                theme.Border(rect, wrong ? HudTheme.Alert : HudTheme.Rule2, 2f);
                GUI.Label(rect, _bin[b],
                    theme.At(theme.Heading, 19, wrong ? HudTheme.Alert : HudTheme.Ink,
                        TextAnchor.MiddleCenter));
            }

            theme.Border(body, HudTheme.Rule);
        }
    }
}
