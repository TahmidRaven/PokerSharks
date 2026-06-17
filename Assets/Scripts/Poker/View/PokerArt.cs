using System.Collections.Generic;
using UnityEngine;

namespace Poker
{
    /// <summary>Loads and maps the poker sprite sheets from Resources/Poker.</summary>
    public sealed class PokerArt
    {
        private readonly Dictionary<string, Sprite> _byName = new Dictionary<string, Sprite>();
        public bool Loaded { get; private set; }

        private static readonly string[] Sheets =
        {
            "Hearts-88x124", "Spades-88x124", "Clubs-88x124", "Diamonds-88x124",
            "Card_Back-88x124", "ChipsA_Flat-64x72"
        };

        public void Load()
        {
            _byName.Clear();
            foreach (var sheet in Sheets)
            {
                var sprites = Resources.LoadAll<Sprite>("Poker/" + sheet);
                if (sprites == null || sprites.Length == 0)
                {
                    Debug.LogWarning($"[PokerArt] No sprites found at Resources/Poker/{sheet}");
                    continue;
                }
                foreach (var s in sprites)
                    _byName[s.name] = s;
            }
            Loaded = _byName.Count > 0;
        }

        public Sprite Card(Card c) => Get(c.SpriteName);

        // Card_Back sheet: _0 = red, _1 = blue.
        public Sprite Back(bool blue) => Get(blue ? "Card_Back-88x124_1" : "Card_Back-88x124_0");

        // ChipsA sheet, 10 colours: 0 white,1 red,2 green,3 blue,4 black,5 yellow,6 orange,7 purple,8 pink,9 brown.
        public Sprite Chip(int index) => Get($"ChipsA_Flat-64x72_{Mathf.Clamp(index, 0, 9)}");

        public Sprite Get(string name) => _byName.TryGetValue(name, out var s) ? s : null;
    }
}
