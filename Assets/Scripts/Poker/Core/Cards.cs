using System;
using System.Collections.Generic;

namespace Poker
{
    public enum Suit { Clubs = 0, Diamonds = 1, Hearts = 2, Spades = 3 }

    // Numeric values double as poker rank strength (Ace high).
    public enum Rank
    {
        Two = 2, Three = 3, Four = 4, Five = 5, Six = 6, Seven = 7, Eight = 8,
        Nine = 9, Ten = 10, Jack = 11, Queen = 12, King = 13, Ace = 14
    }

    public readonly struct Card : IEquatable<Card>
    {
        public readonly Rank Rank;
        public readonly Suit Suit;

        public Card(Rank rank, Suit suit) { Rank = rank; Suit = suit; }

        public bool IsRed => Suit == Suit.Hearts || Suit == Suit.Diamonds;

        // Index of this card within its suit's sprite sheet:
        // 0=A, 1=2, 2=3, ... 9=10, 10=J, 11=Q, 12=K
        public int SpriteIndex => Rank == Rank.Ace ? 0 : (int)Rank - 1;

        // Sub-sprite name inside the suit sheet, e.g. "Hearts-88x124_0".
        public string SpriteName => $"{SheetName}_{SpriteIndex}";

        public string SheetName => Suit switch
        {
            Suit.Hearts => "Hearts-88x124",
            Suit.Spades => "Spades-88x124",
            Suit.Clubs => "Clubs-88x124",
            Suit.Diamonds => "Diamonds-88x124",
            _ => "Hearts-88x124"
        };

        public string RankLabel => Rank switch
        {
            Rank.Ace => "A",
            Rank.King => "K",
            Rank.Queen => "Q",
            Rank.Jack => "J",
            Rank.Ten => "10",
            _ => ((int)Rank).ToString()
        };

        public string SuitSymbol => Suit switch
        {
            Suit.Hearts => "♥",
            Suit.Diamonds => "♦",
            Suit.Clubs => "♣",
            Suit.Spades => "♠",
            _ => "?"
        };

        public bool Equals(Card other) => Rank == other.Rank && Suit == other.Suit;
        public override bool Equals(object obj) => obj is Card c && Equals(c);
        public override int GetHashCode() => ((int)Rank) * 4 + (int)Suit;
        public override string ToString() => $"{RankLabel}{SuitSymbol}";
    }

    public sealed class Deck
    {
        private readonly List<Card> _cards = new List<Card>(52);
        private int _next;
        private readonly Random _rng;

        public Deck(Random rng)
        {
            _rng = rng ?? new Random();
        }

        public int Remaining => _cards.Count - _next;

        public void Reset()
        {
            _cards.Clear();
            for (int s = 0; s < 4; s++)
                for (int r = 2; r <= 14; r++)
                    _cards.Add(new Card((Rank)r, (Suit)s));
            _next = 0;
            Shuffle();
        }

        private void Shuffle()
        {
            // Fisher-Yates
            for (int i = _cards.Count - 1; i > 0; i--)
            {
                int j = _rng.Next(i + 1);
                (_cards[i], _cards[j]) = (_cards[j], _cards[i]);
            }
        }

        public Card Deal()
        {
            if (_next >= _cards.Count)
                throw new InvalidOperationException("Deck exhausted.");
            return _cards[_next++];
        }
    }
}
