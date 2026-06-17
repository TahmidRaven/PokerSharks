using System;
using System.Collections.Generic;

namespace Poker
{
    public enum HandCategory
    {
        HighCard = 0,
        Pair = 1,
        TwoPair = 2,
        ThreeOfAKind = 3,
        Straight = 4,
        Flush = 5,
        FullHouse = 6,
        FourOfAKind = 7,
        StraightFlush = 8
    }

    /// <summary>
    /// Comparable poker hand strength. Higher Score beats lower; equal Score ties (split pot).
    /// </summary>
    public readonly struct HandValue : IComparable<HandValue>
    {
        public readonly HandCategory Category;
        public readonly long Score;       // category + tiebreak ranks packed into one comparable number
        public readonly int TopRank;      // most significant tiebreak rank (e.g. trips rank, straight high)
        public readonly int SecondRank;   // secondary (e.g. pair rank in full house / two pair)

        public HandValue(HandCategory category, long score, int topRank, int secondRank)
        {
            Category = category;
            Score = score;
            TopRank = topRank;
            SecondRank = secondRank;
        }

        public int CompareTo(HandValue other) => Score.CompareTo(other.Score);

        public bool IsRoyalFlush => Category == HandCategory.StraightFlush && TopRank == 14;

        public string Describe()
        {
            switch (Category)
            {
                case HandCategory.StraightFlush:
                    return IsRoyalFlush ? "Royal Flush" : $"Straight Flush, {RankName(TopRank)} high";
                case HandCategory.FourOfAKind:
                    return $"Four of a Kind, {RankPlural(TopRank)}";
                case HandCategory.FullHouse:
                    return $"Full House, {RankPlural(TopRank)} over {RankPlural(SecondRank)}";
                case HandCategory.Flush:
                    return $"Flush, {RankName(TopRank)} high";
                case HandCategory.Straight:
                    return $"Straight, {RankName(TopRank)} high";
                case HandCategory.ThreeOfAKind:
                    return $"Three of a Kind, {RankPlural(TopRank)}";
                case HandCategory.TwoPair:
                    return $"Two Pair, {RankPlural(TopRank)} & {RankPlural(SecondRank)}";
                case HandCategory.Pair:
                    return $"Pair of {RankPlural(TopRank)}";
                default:
                    return $"High Card {RankName(TopRank)}";
            }
        }

        private static string RankName(int r) => r switch
        {
            14 => "Ace", 13 => "King", 12 => "Queen", 11 => "Jack", 10 => "Ten",
            9 => "Nine", 8 => "Eight", 7 => "Seven", 6 => "Six", 5 => "Five",
            4 => "Four", 3 => "Three", 2 => "Two", _ => r.ToString()
        };

        private static string RankPlural(int r) => r == 6 ? "Sixes" : RankName(r) + "s";
    }

    public static class HandEvaluator
    {
        // All 21 ways to choose 5 cards out of 7.
        private static readonly int[][] Combos7 = BuildCombos(7, 5);

        public static HandValue Evaluate(IReadOnlyList<Card> cards)
        {
            if (cards.Count == 5)
                return Eval5(cards[0], cards[1], cards[2], cards[3], cards[4]);

            if (cards.Count < 5)
                throw new ArgumentException("Need at least 5 cards to evaluate.");

            // Best 5-of-N (N = 6 or 7 in Hold'em). Use precomputed combos for 7, generic otherwise.
            HandValue best = default;
            bool has = false;

            if (cards.Count == 7)
            {
                foreach (var c in Combos7)
                {
                    var v = Eval5(cards[c[0]], cards[c[1]], cards[c[2]], cards[c[3]], cards[c[4]]);
                    if (!has || v.Score > best.Score) { best = v; has = true; }
                }
            }
            else
            {
                foreach (var c in BuildCombos(cards.Count, 5))
                {
                    var v = Eval5(cards[c[0]], cards[c[1]], cards[c[2]], cards[c[3]], cards[c[4]]);
                    if (!has || v.Score > best.Score) { best = v; has = true; }
                }
            }
            return best;
        }

        private static HandValue Eval5(Card a, Card b, Card c, Card d, Card e)
        {
            Span<int> ranks = stackalloc int[5]
                { (int)a.Rank, (int)b.Rank, (int)c.Rank, (int)d.Rank, (int)e.Rank };
            Span<int> suits = stackalloc int[5]
                { (int)a.Suit, (int)b.Suit, (int)c.Suit, (int)d.Suit, (int)e.Suit };

            // sort ranks descending (simple insertion sort on 5 elements)
            for (int i = 1; i < 5; i++)
            {
                int key = ranks[i];
                int j = i - 1;
                while (j >= 0 && ranks[j] < key) { ranks[j + 1] = ranks[j]; j--; }
                ranks[j + 1] = key;
            }

            bool flush = suits[0] == suits[1] && suits[1] == suits[2] &&
                         suits[2] == suits[3] && suits[3] == suits[4];

            // group rank -> count
            Span<int> uniqueRank = stackalloc int[5];
            Span<int> uniqueCount = stackalloc int[5];
            int groups = 0;
            for (int i = 0; i < 5; i++)
            {
                int found = -1;
                for (int g = 0; g < groups; g++)
                    if (uniqueRank[g] == ranks[i]) { found = g; break; }
                if (found >= 0) uniqueCount[found]++;
                else { uniqueRank[groups] = ranks[i]; uniqueCount[groups] = 1; groups++; }
            }

            // sort groups by (count desc, rank desc)
            for (int i = 1; i < groups; i++)
            {
                int kr = uniqueRank[i], kc = uniqueCount[i];
                int j = i - 1;
                while (j >= 0 && (uniqueCount[j] < kc || (uniqueCount[j] == kc && uniqueRank[j] < kr)))
                {
                    uniqueRank[j + 1] = uniqueRank[j];
                    uniqueCount[j + 1] = uniqueCount[j];
                    j--;
                }
                uniqueRank[j + 1] = kr;
                uniqueCount[j + 1] = kc;
            }

            // straight detection (distinct ranks only)
            bool straight = false;
            int straightHigh = 0;
            if (groups == 5)
            {
                if (ranks[0] - ranks[4] == 4) { straight = true; straightHigh = ranks[0]; }
                // wheel: A-5-4-3-2 (ranks sorted desc = 14,5,4,3,2)
                else if (ranks[0] == 14 && ranks[1] == 5 && ranks[2] == 4 && ranks[3] == 3 && ranks[4] == 2)
                { straight = true; straightHigh = 5; }
            }

            HandCategory cat;
            Span<int> tb = stackalloc int[5];
            int top, second = 0;

            if (straight && flush)
            {
                cat = HandCategory.StraightFlush;
                tb[0] = straightHigh; top = straightHigh;
            }
            else if (uniqueCount[0] == 4)
            {
                cat = HandCategory.FourOfAKind;
                tb[0] = uniqueRank[0]; tb[1] = uniqueRank[1];
                top = uniqueRank[0];
            }
            else if (uniqueCount[0] == 3 && uniqueCount[1] == 2)
            {
                cat = HandCategory.FullHouse;
                tb[0] = uniqueRank[0]; tb[1] = uniqueRank[1];
                top = uniqueRank[0]; second = uniqueRank[1];
            }
            else if (flush)
            {
                cat = HandCategory.Flush;
                for (int i = 0; i < 5; i++) tb[i] = ranks[i];
                top = ranks[0];
            }
            else if (straight)
            {
                cat = HandCategory.Straight;
                tb[0] = straightHigh; top = straightHigh;
            }
            else if (uniqueCount[0] == 3)
            {
                cat = HandCategory.ThreeOfAKind;
                tb[0] = uniqueRank[0]; tb[1] = uniqueRank[1]; tb[2] = uniqueRank[2];
                top = uniqueRank[0];
            }
            else if (uniqueCount[0] == 2 && uniqueCount[1] == 2)
            {
                cat = HandCategory.TwoPair;
                tb[0] = uniqueRank[0]; tb[1] = uniqueRank[1]; tb[2] = uniqueRank[2];
                top = uniqueRank[0]; second = uniqueRank[1];
            }
            else if (uniqueCount[0] == 2)
            {
                cat = HandCategory.Pair;
                tb[0] = uniqueRank[0]; tb[1] = uniqueRank[1]; tb[2] = uniqueRank[2]; tb[3] = uniqueRank[3];
                top = uniqueRank[0];
            }
            else
            {
                cat = HandCategory.HighCard;
                for (int i = 0; i < 5; i++) tb[i] = ranks[i];
                top = ranks[0];
            }

            long score = (long)cat;
            for (int i = 0; i < 5; i++) score = score * 16 + tb[i];

            return new HandValue(cat, score, top, second);
        }

        private static int[][] BuildCombos(int n, int k)
        {
            var result = new List<int[]>();
            var idx = new int[k];
            for (int i = 0; i < k; i++) idx[i] = i;
            while (true)
            {
                result.Add((int[])idx.Clone());
                int pos = k - 1;
                while (pos >= 0 && idx[pos] == n - k + pos) pos--;
                if (pos < 0) break;
                idx[pos]++;
                for (int i = pos + 1; i < k; i++) idx[i] = idx[i - 1] + 1;
            }
            return result.ToArray();
        }
    }
}
