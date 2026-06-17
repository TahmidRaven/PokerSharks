using System;
using System.Collections.Generic;

namespace Poker
{
    /// <summary>
    /// AI decision maker for a single seat. Estimates equity by Monte-Carlo
    /// simulation, then chooses an action weighted by a per-bot personality.
    /// </summary>
    public sealed class BotBrain
    {
        private readonly Random _rng;
        public readonly float Aggression; // 0..1 — how often / how big it raises
        public readonly float Tightness;  // 0..1 — how much equity it demands to continue
        public readonly float Bluff;      // 0..~0.15 — chance to bet/raise weak

        public BotBrain(Random rng, float aggression, float tightness, float bluff)
        {
            _rng = rng;
            Aggression = aggression;
            Tightness = tightness;
            Bluff = bluff;
        }

        public static BotBrain CreateRandom(Random rng)
        {
            return new BotBrain(
                rng,
                aggression: 0.35f + (float)rng.NextDouble() * 0.45f,
                tightness: 0.40f + (float)rng.NextDouble() * 0.35f,
                bluff: 0.04f + (float)rng.NextDouble() * 0.10f);
        }

        public PlayerAction Decide(PokerEngine engine, SeatPlayer me)
        {
            var legal = engine.GetLegalActions();
            int opponents = 0;
            foreach (var p in engine.Players) if (p.IsActive && p != me) opponents++;
            if (opponents < 1) opponents = 1;

            float equity = EstimateEquity(engine, me, opponents);
            int pot = engine.Pot;
            int toCall = legal.ToCall;

            // A little noise so bots aren't deterministic.
            float noise = ((float)_rng.NextDouble() - 0.5f) * 0.08f;
            float e = Math.Clamp(equity + noise, 0f, 1f);

            bool wantsToBluff = _rng.NextDouble() < Bluff;

            // ---- No bet to face: check or bet ----
            if (toCall == 0)
            {
                float betThreshold = 0.55f + Tightness * 0.12f; // higher tightness → bets less
                if (legal.CanRaise && (e > betThreshold || wantsToBluff))
                {
                    float frac = wantsToBluff ? 0.5f : 0.45f + Aggression * 0.45f;
                    return RaiseByPotFraction(engine, legal, pot, frac);
                }
                return PlayerAction.Check();
            }

            // ---- Facing a bet: fold / call / raise ----
            float potOdds = toCall / (float)(pot + toCall);
            // Required equity scaled up a touch by tightness; loosened by aggression.
            float continueThreshold = potOdds + (Tightness - 0.5f) * 0.12f - (Aggression - 0.5f) * 0.05f;
            continueThreshold = Math.Clamp(continueThreshold, 0.05f, 0.95f);

            bool callIsAllIn = toCall >= me.Chips;
            // Demand more equity to stake the whole stack.
            float allInPenalty = callIsAllIn ? 0.08f : 0f;

            // Strong hand → raise for value.
            float raiseThreshold = 0.66f + (1f - Aggression) * 0.16f;
            if (legal.CanRaise && (e > raiseThreshold || (wantsToBluff && e > continueThreshold)))
            {
                float frac = 0.5f + Aggression * 0.6f;
                return RaiseByPotFraction(engine, legal, pot, frac);
            }

            if (e >= continueThreshold + allInPenalty)
                return PlayerAction.Call();

            // Occasionally call a cheap bet as a bluff-catch / float.
            if (!callIsAllIn && toCall <= pot * 0.2f && _rng.NextDouble() < 0.35f)
                return PlayerAction.Call();

            return PlayerAction.Fold();
        }

        private PlayerAction RaiseByPotFraction(PokerEngine engine, LegalActions legal, int pot, float frac)
        {
            int raiseExtra = Math.Max(engine.BigBlind, (int)Math.Round(pot * frac));
            int raiseTo = engine.CurrentBet + raiseExtra;
            raiseTo = Math.Clamp(raiseTo, legal.MinRaiseTo, legal.MaxRaiseTo);
            return PlayerAction.RaiseTo(raiseTo);
        }

        // ---- Monte-Carlo equity ----

        private float EstimateEquity(PokerEngine engine, SeatPlayer me, int opponents)
        {
            // Build the unknown-card pool (full deck minus my hole cards and the board).
            var used = new bool[52];
            int Idx(Card c) => (int)c.Suit * 13 + ((int)c.Rank - 2);
            used[Idx(me.Hole[0])] = true;
            used[Idx(me.Hole[1])] = true;
            foreach (var bc in engine.Board) used[Idx(bc)] = true;

            var pool = new List<Card>(52);
            for (int s = 0; s < 4; s++)
                for (int r = 2; r <= 14; r++)
                    if (!used[s * 13 + (r - 2)]) pool.Add(new Card((Rank)r, (Suit)s));

            int boardKnown = engine.Board.Count;
            int boardNeeded = 5 - boardKnown;
            int draw = boardNeeded + opponents * 2;
            if (draw > pool.Count) return 0.5f; // shouldn't happen

            int samples = boardKnown == 0 ? 120 : 200;
            double equity = 0.0;

            var myCards = new List<Card>(7);
            var board = new List<Card>(5);
            var oppCards = new List<Card>(7);

            for (int s = 0; s < samples; s++)
            {
                // Partial Fisher-Yates draw of the cards we need this sample.
                for (int i = 0; i < draw; i++)
                {
                    int j = i + _rng.Next(pool.Count - i);
                    (pool[i], pool[j]) = (pool[j], pool[i]);
                }

                board.Clear();
                board.AddRange(engine.Board);
                for (int i = 0; i < boardNeeded; i++) board.Add(pool[i]);

                myCards.Clear();
                myCards.Add(me.Hole[0]); myCards.Add(me.Hole[1]);
                myCards.AddRange(board);
                long myScore = HandEvaluator.Evaluate(myCards).Score;

                bool lose = false;
                int tie = 0;
                int cursor = boardNeeded;
                for (int o = 0; o < opponents; o++)
                {
                    oppCards.Clear();
                    oppCards.Add(pool[cursor++]);
                    oppCards.Add(pool[cursor++]);
                    oppCards.AddRange(board);
                    long oppScore = HandEvaluator.Evaluate(oppCards).Score;
                    if (oppScore > myScore) { lose = true; break; }
                    if (oppScore == myScore) tie++;
                }

                if (lose) continue;
                equity += tie > 0 ? 1.0 / (tie + 1) : 1.0;
            }

            return (float)(equity / samples);
        }
    }
}
