using System;
using System.Collections.Generic;

namespace Poker
{
    public enum Street { Preflop, Flop, Turn, River, Showdown }
    public enum ActionType { Fold, Check, Call, Raise }

    public readonly struct PlayerAction
    {
        public readonly ActionType Type;
        public readonly int Amount; // for Raise: the total "raise-to" street commitment level

        public PlayerAction(ActionType type, int amount = 0) { Type = type; Amount = amount; }

        public static PlayerAction Fold() => new PlayerAction(ActionType.Fold);
        public static PlayerAction Check() => new PlayerAction(ActionType.Check);
        public static PlayerAction Call() => new PlayerAction(ActionType.Call);
        public static PlayerAction RaiseTo(int amount) => new PlayerAction(ActionType.Raise, amount);
    }

    public readonly struct LegalActions
    {
        public readonly bool CanCheck;
        public readonly bool CanCall;
        public readonly bool CanRaise;
        public readonly int ToCall;       // chips needed to call
        public readonly int MinRaiseTo;   // smallest legal raise-to level
        public readonly int MaxRaiseTo;   // all-in raise-to level

        public LegalActions(bool canCheck, bool canCall, bool canRaise, int toCall, int minRaiseTo, int maxRaiseTo)
        {
            CanCheck = canCheck; CanCall = canCall; CanRaise = canRaise;
            ToCall = toCall; MinRaiseTo = minRaiseTo; MaxRaiseTo = maxRaiseTo;
        }
    }

    public sealed class SeatPlayer
    {
        public int Seat;
        public string Name;
        public bool IsHuman;

        public int Chips;
        public readonly Card[] Hole = new Card[2];
        public int HoleCount;

        public bool InHand;          // dealt into the current hand
        public bool Folded;
        public int StreetCommitted;  // chips put in this betting round
        public int TotalCommitted;   // chips put in this whole hand (for side pots)
        public bool HasActed;        // acted since the last bet/raise this round

        public PlayerAction LastAction;
        public bool HasLastAction;

        public bool IsAllIn => InHand && !Folded && Chips == 0;
        public bool IsActive => InHand && !Folded;        // still contesting the pot
        public bool CanAct => InHand && !Folded && Chips > 0;
    }

    public sealed class PotShare
    {
        public SeatPlayer Player;
        public int Amount;
        public HandValue Value;     // default when won by fold
        public bool WonByFold;
        public int PotIndex;        // 0 = main pot
    }

    public sealed class PokerEngine
    {
        public readonly List<SeatPlayer> Players = new List<SeatPlayer>();
        public readonly List<Card> Board = new List<Card>(5);

        public int SmallBlind = 5;
        public int BigBlind = 10;
        public int ButtonIndex = -1;

        public Street Street { get; private set; }
        public int CurrentBet { get; private set; }   // highest street commitment to match
        public int MinRaise { get; private set; }
        public int ActingIndex { get; private set; }

        public bool NeedsAction { get; private set; }  // a player must act now
        public bool HandOver { get; private set; }
        public bool ReachedShowdown { get; private set; }

        private readonly Deck _deck;

        public PokerEngine(Random rng)
        {
            _deck = new Deck(rng);
        }

        public SeatPlayer ActingPlayer => (ActingIndex >= 0 && ActingIndex < Players.Count) ? Players[ActingIndex] : null;
        public int Pot
        {
            get { int p = 0; foreach (var pl in Players) p += pl.TotalCommitted; return p; }
        }

        public int ActiveCount
        {
            get { int c = 0; foreach (var p in Players) if (p.IsActive) c++; return c; }
        }

        private int PlayersAbleToAct
        {
            get { int c = 0; foreach (var p in Players) if (p.CanAct) c++; return c; }
        }

        public bool CanStartHand
        {
            get { int c = 0; foreach (var p in Players) if (p.Chips > 0) c++; return c >= 2; }
        }

        // -------- Hand setup --------

        public void StartHand()
        {
            Board.Clear();
            _deck.Reset();
            Street = Street.Preflop;
            HandOver = false;
            ReachedShowdown = false;
            NeedsAction = false;
            CurrentBet = 0;
            MinRaise = BigBlind;

            foreach (var p in Players)
            {
                p.Folded = false;
                p.HoleCount = 0;
                p.StreetCommitted = 0;
                p.TotalCommitted = 0;
                p.HasActed = false;
                p.HasLastAction = false;
                p.InHand = p.Chips > 0;
            }

            // Move button to next player who is in the hand.
            ButtonIndex = NextInHand(ButtonIndex);

            // Blinds.
            int sbIndex = NextInHand(ButtonIndex);
            int bbIndex = NextInHand(sbIndex);
            PostBlind(Players[sbIndex], SmallBlind);
            PostBlind(Players[bbIndex], BigBlind);
            CurrentBet = BigBlind;
            MinRaise = BigBlind;

            // Deal 2 hole cards, one at a time, starting left of button.
            int start = NextInHand(ButtonIndex);
            for (int round = 0; round < 2; round++)
            {
                int idx = start;
                do
                {
                    var p = Players[idx];
                    if (p.InHand) p.Hole[p.HoleCount++] = _deck.Deal();
                    idx = NextIndex(idx);
                } while (idx != start);
            }

            // First to act preflop is left of the big blind.
            ActingIndex = NextInHand(bbIndex);
            BeginActorScan(ActingIndex, openingActor: true);
        }

        private void PostBlind(SeatPlayer p, int blind)
        {
            int amt = Math.Min(blind, p.Chips);
            p.Chips -= amt;
            p.StreetCommitted += amt;
            p.TotalCommitted += amt;
        }

        // -------- Action processing --------

        public LegalActions GetLegalActions()
        {
            var p = ActingPlayer;
            int toCall = Math.Min(CurrentBet - p.StreetCommitted, p.Chips);
            if (toCall < 0) toCall = 0;
            bool canCheck = (CurrentBet - p.StreetCommitted) <= 0;
            bool canCall = !canCheck && toCall > 0;
            bool canRaise = p.Chips > toCall; // has chips beyond a call
            int minRaiseTo = CurrentBet + MinRaise;
            int maxRaiseTo = p.StreetCommitted + p.Chips;
            if (minRaiseTo > maxRaiseTo) minRaiseTo = maxRaiseTo; // short all-in
            return new LegalActions(canCheck, canCall, canRaise, toCall, minRaiseTo, maxRaiseTo);
        }

        public void SubmitAction(PlayerAction action)
        {
            var p = ActingPlayer;
            var legal = GetLegalActions();
            ActionType type = action.Type;

            switch (type)
            {
                case ActionType.Fold:
                    p.Folded = true;
                    break;

                case ActionType.Check:
                    if (!legal.CanCheck) goto case ActionType.Call; // can't check facing a bet → treat as call
                    break;

                case ActionType.Call:
                {
                    int amt = legal.ToCall;
                    Commit(p, amt);
                    break;
                }

                case ActionType.Raise:
                {
                    int raiseTo = Math.Clamp(action.Amount, legal.MinRaiseTo, legal.MaxRaiseTo);
                    int needed = raiseTo - p.StreetCommitted;
                    Commit(p, needed);
                    if (p.StreetCommitted > CurrentBet)
                    {
                        MinRaise = Math.Max(BigBlind, p.StreetCommitted - CurrentBet);
                        CurrentBet = p.StreetCommitted;
                        // Re-open action for everyone else still able to act.
                        foreach (var other in Players)
                            if (other != p && other.CanAct) other.HasActed = false;
                    }
                    break;
                }
            }

            p.HasActed = true;
            p.LastAction = new PlayerAction(type, p.StreetCommitted);
            p.HasLastAction = true;

            // Win by everyone folding?
            if (ActiveCount <= 1)
            {
                NeedsAction = false;
                HandOver = true;
                ReachedShowdown = false;
                return;
            }

            BeginActorScan(NextIndex(ActingIndex), openingActor: false);
        }

        private void Commit(SeatPlayer p, int amount)
        {
            amount = Math.Min(amount, p.Chips);
            if (amount < 0) amount = 0;
            p.Chips -= amount;
            p.StreetCommitted += amount;
            p.TotalCommitted += amount;
        }

        // Find the next player who still needs to act this street, starting at `from`.
        private void BeginActorScan(int from, bool openingActor)
        {
            int idx = from;
            for (int i = 0; i < Players.Count; i++)
            {
                var p = Players[idx];
                if (p.CanAct && (!p.HasActed || p.StreetCommitted < CurrentBet))
                {
                    ActingIndex = idx;
                    NeedsAction = true;
                    return;
                }
                idx = NextIndex(idx);
            }
            // No one left to act → betting round complete.
            NeedsAction = false;
        }

        // -------- Street progression --------

        /// <summary>Call when !NeedsAction && !HandOver to deal the next street (or reach showdown).</summary>
        public void AdvanceStreet()
        {
            if (NeedsAction || HandOver) return;

            // Reset per-street betting state.
            foreach (var p in Players) { p.StreetCommitted = 0; p.HasActed = false; }
            CurrentBet = 0;
            MinRaise = BigBlind;

            switch (Street)
            {
                case Street.Preflop:
                    Street = Street.Flop;
                    Board.Add(_deck.Deal()); Board.Add(_deck.Deal()); Board.Add(_deck.Deal());
                    break;
                case Street.Flop:
                    Street = Street.Turn;
                    Board.Add(_deck.Deal());
                    break;
                case Street.Turn:
                    Street = Street.River;
                    Board.Add(_deck.Deal());
                    break;
                case Street.River:
                    Street = Street.Showdown;
                    HandOver = true;
                    ReachedShowdown = true;
                    return;
            }

            // If two or more players can still act, open betting on this street
            // (first active player left of the button). Otherwise leave NeedsAction
            // false so the controller deals the next street (all-in run-out).
            if (PlayersAbleToAct >= 2)
            {
                BeginActorScan(NextInHand(ButtonIndex), openingActor: true);
            }
            else
            {
                NeedsAction = false;
            }
        }

        // -------- Showdown / awarding --------

        public List<PotShare> FinishHand()
        {
            var shares = new List<PotShare>();

            // Fold win: a single active player scoops everything.
            if (!ReachedShowdown)
            {
                SeatPlayer winner = null;
                foreach (var p in Players) if (p.IsActive) { winner = p; break; }
                if (winner != null)
                {
                    int pot = Pot;
                    winner.Chips += pot;
                    shares.Add(new PotShare { Player = winner, Amount = pot, WonByFold = true, PotIndex = 0 });
                }
                ZeroContributions();
                return shares;
            }

            // Showdown with side pots.
            var contrib = new Dictionary<SeatPlayer, int>();
            foreach (var p in Players) if (p.TotalCommitted > 0) contrib[p] = p.TotalCommitted;

            // Pre-evaluate the active players' best hands.
            var value = new Dictionary<SeatPlayer, HandValue>();
            foreach (var p in Players)
            {
                if (!p.IsActive) continue;
                var seven = new List<Card>(7) { p.Hole[0], p.Hole[1] };
                seven.AddRange(Board);
                value[p] = HandEvaluator.Evaluate(seven);
            }

            int potIndex = 0;
            var keys = new List<SeatPlayer>(contrib.Keys);
            while (true)
            {
                // Smallest remaining positive contribution sets the next pot layer.
                int min = int.MaxValue;
                foreach (var p in keys) if (contrib[p] > 0 && contrib[p] < min) min = contrib[p];
                if (min == int.MaxValue) break;

                // Only players who still have chips in (>= min) contributed to this layer.
                var layerContributors = new List<SeatPlayer>();
                foreach (var p in keys) if (contrib[p] > 0) layerContributors.Add(p);

                int potAmount = min * layerContributors.Count;
                foreach (var p in layerContributors) contrib[p] -= min;

                // Eligible winners = still active AND contributed to this layer.
                var eligible = new List<SeatPlayer>();
                long best = long.MinValue;
                foreach (var p in layerContributors)
                    if (p.IsActive && value[p].Score > best) best = value[p].Score;
                foreach (var p in layerContributors)
                    if (p.IsActive && value[p].Score == best) eligible.Add(p);

                if (eligible.Count > 0)
                {
                    int each = potAmount / eligible.Count;
                    int remainder = potAmount - each * eligible.Count;
                    // Order winners by seat distance from button for odd-chip assignment.
                    eligible.Sort((a, b) => SeatOrderFromButton(a.Seat).CompareTo(SeatOrderFromButton(b.Seat)));
                    for (int i = 0; i < eligible.Count; i++)
                    {
                        int amt = each + (i < remainder ? 1 : 0);
                        eligible[i].Chips += amt;
                        shares.Add(new PotShare
                        {
                            Player = eligible[i],
                            Amount = amt,
                            Value = value[eligible[i]],
                            WonByFold = false,
                            PotIndex = potIndex
                        });
                    }
                }
                potIndex++;
            }

            ZeroContributions();
            return shares;
        }

        private void ZeroContributions()
        {
            foreach (var p in Players) { p.TotalCommitted = 0; p.StreetCommitted = 0; }
        }

        private int SeatOrderFromButton(int seat)
        {
            int n = Players.Count;
            return ((seat - ButtonIndex - 1) % n + n) % n;
        }

        // -------- Index helpers --------

        private int NextIndex(int i) => (i + 1) % Players.Count;

        private int NextInHand(int from)
        {
            int idx = NextIndex(from < 0 ? Players.Count - 1 : from);
            for (int i = 0; i < Players.Count; i++)
            {
                if (Players[idx].InHand) return idx;
                idx = NextIndex(idx);
            }
            return idx;
        }
    }
}
