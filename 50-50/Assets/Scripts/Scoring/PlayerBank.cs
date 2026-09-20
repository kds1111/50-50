using System;
using System.Collections.Generic;
using FiftyFifty.Board;
using FiftyFifty.Board.Tricks;
using UnityEngine;

namespace FiftyFifty.Scoring
{
    /// <summary>
    /// One player's bank and score, on their board (#20). The rules are <see cref="PendingBank"/>'s;
    /// this component holds the numbers in the Inspector, listens for tricks and bails, and is
    /// what a goal talks to.
    ///
    /// ADDITIVE, like the trick system. It reads BoardTrickController's events and never writes
    /// to the board. Untick it and the game is exactly what it was before the bank existed —
    /// goals count entries and touch nothing.
    ///
    /// Bank and score are two numbers and must never be confused (CONTEXT.md). The bank is at
    /// risk; the score is cemented and can never go down.
    /// </summary>
    public class PlayerBank : MonoBehaviour
    {
        private static readonly List<PlayerBank> Registered = new();

        /// <summary>Every enabled bank in the scene. Goals walk this to find both sides.</summary>
        public static IReadOnlyList<PlayerBank> All => Registered;

        [Header("Side")]
        [Tooltip("Which end this player plays for. A goal in the net side A defends cements side " +
                 "B's bank and forfeits side A's. Side A starts at the -Z end by convention.")]
        public Side Side = Side.A;

        [Header("Wiring")]
        [Tooltip("Found on this object if left empty.")]
        public BoardController Board;

        [Tooltip("Found on this object if left empty. Without one the bank never grows, but a " +
                 "goal still pays 1.")]
        public BoardTrickController Tricks;

        [Header("The bank (#8)")]
        [Tooltip("An empty bank, and what a goal pays with nothing banked. A goal is always worth " +
                 "something, which is the whole reason the bank is a multiplier.")]
        public float StartValue = 1f;

        [Tooltip("The bank clamps here. Further tricks add nothing and cost nothing. A great run " +
                 "is designed to land near x2.6, so this mostly catches the long 10% tail.")]
        public float Cap = 3f;

        [Header("Spin bonus (stacks on a named trick; a spin alone pays nothing)")]
        [Tooltip("Added for a 180 on top of a named trick.")]
        public float SpinBonus180 = 0.10f;

        [Tooltip("Added for every further 180: a 360 pays 0.15, a 540 0.20, and so on.")]
        public float SpinBonusPerExtra180 = 0.05f;

        [Header("Repetition")]
        [Tooltip("Fraction paid for the 1st, 2nd, 3rd… landing of the same name in one bank. " +
                 "Keyed on the name, so spin variants of one flip share a chain. The last entry " +
                 "repeats forever. Resets when the bank does.")]
        public float[] RepeatCurve = { 1f, 0.75f, 0.5f, 0.25f, 0.1f };

        [Header("Grinds (#12 feeds these)")]
        [Tooltip("Seconds on a rail before the clock starts. Shorter than this is a graze: pays " +
                 "nothing, and falling off one is not a bail.")]
        public float GrindMinSeconds = BankRules.DefaultGrindMinSeconds;

        [Tooltip("Most one grind can add, before repetition.")]
        public float GrindCapPerGrind = 0.2f;

        [Header("Losing it")]
        [Tooltip("Fraction of the bank's gain kept on a bail. 0 is the total wipe #8 decided.")]
        [Range(0f, 1f)] public float BailKeeps;

        [Tooltip("Fraction of the bank's gain kept when the other side scores. 0 is the total " +
                 "wipe #8 decided; 0.5 is the obvious first retune if that feels too harsh.")]
        [Range(0f, 1f)] public float ConcedeKeeps;

        [Header("Debug (read-only)")]
        [SerializeField] private float _bank = 1f;
        [SerializeField] private float _score;
        [SerializeField] private string _lastEvent = "-";

        private PendingBank _pending;
        private float _lastEventTime = -999f;

        /// <summary>The multiplier at risk right now.</summary>
        public float Bank => _pending != null ? _pending.Value : StartValue;

        /// <summary>Cemented. Permanent. Never call this the bank.</summary>
        public float Score => _score;

        /// <summary>One line on what last happened to the bank, for readouts.</summary>
        public string LastEvent => _lastEvent;

        public float SecondsSinceEvent => Time.time - _lastEventTime;

        /// <summary>Raised whenever something is added to the bank.</summary>
        public event Action<BankCredit> Credited;

        /// <summary>Raised on a goal for this side, with what it paid.</summary>
        public event Action<float> Cemented;

        /// <summary>Raised when the bank is lost, with why and how much went.</summary>
        public event Action<ForfeitReason, float> Forfeited;

        private void Awake()
        {
            // Explicit null checks, not ??=: an unassigned serialized reference can be Unity's
            // fake null, which ??= does not see.
            if (Board == null)
            {
                Board = GetComponent<BoardController>();
            }

            if (Tricks == null)
            {
                Tricks = GetComponent<BoardTrickController>();
            }

            _pending = new PendingBank(BuildRules());
            _bank = _pending.Value;
        }

        private void OnEnable()
        {
            Registered.Add(this);

            if (Tricks != null)
            {
                Tricks.Landed += OnLanded;
                Tricks.Bailed += OnBailed;
            }
        }

        private void OnDisable()
        {
            Registered.Remove(this);

            if (Tricks != null)
            {
                Tricks.Landed -= OnLanded;
                Tricks.Bailed -= OnBailed;
            }
        }

        /// <summary>Retune a live bank without leaving Play. What is banked stays banked.</summary>
        private void OnValidate()
        {
            if (_pending != null)
            {
                _pending.Rules = BuildRules();
            }
        }

        private BankRules BuildRules() => new()
        {
            StartValue = StartValue,
            Cap = Cap,
            SpinBonusFirstHalfTurn = SpinBonus180,
            SpinBonusPerExtraHalfTurn = SpinBonusPerExtra180,
            RepeatCurve = RepeatCurve != null ? (float[])RepeatCurve.Clone() : null,
            GrindMinSeconds = GrindMinSeconds,
            GrindCapPerGrind = GrindCapPerGrind,
            BailKeeps = BailKeeps,
            ConcedeKeeps = ConcedeKeeps,
        };

        /// <summary>Grinds are too short to count below this. #12's detection reads it to tell a
        /// graze from a fall.</summary>
        public bool IsGraze(float secondsOnRail) => _pending.Rules.IsGraze(secondsOnRail);

        /// <summary>
        /// A grind left cleanly. Called by grind detection (#12) on a deliberate exit; a fall is
        /// a bail and goes through the trick system's bail instead.
        /// </summary>
        public BankCredit CreditGrind(string grindName, float secondsOnRail, float ratePerSecond)
        {
            // Same carry hook as tricks: off by default, honoured if switched on.
            if (Board != null && Board.TrickCreditBlocked)
            {
                Note($"{grindName} — no credit");
                return default;
            }

            BankCredit credit = _pending.CreditGrind(grindName, secondsOnRail, ratePerSecond);
            Record(credit, grindName);
            return credit;
        }

        /// <summary>What a clean exit from this grind would add right now. Changes nothing.</summary>
        public float PreviewGrind(string grindName, float secondsOnRail, float ratePerSecond) =>
            _pending.PreviewGrind(grindName, secondsOnRail, ratePerSecond);

        /// <summary>A goal for this side. Pays the bank into the score and resets it.</summary>
        public void ScoreGoal()
        {
            float payout = _pending.Cement();
            _score += payout;
            Note($"GOAL  +{payout:0.00}");
            Debug.Log($"[50-50] Side {Side} scores {payout:0.00} — score {_score:0.00}");
            Cemented?.Invoke(payout);
        }

        /// <summary>A goal against this side. The bank is lost (#8).</summary>
        public void Concede() => Forfeit(ForfeitReason.Conceded);

        /// <summary>A fresh match (#24): score back to zero and an empty bank.</summary>
        public void ResetForNewMatch()
        {
            _score = 0f;
            ResetBank();
            Note("new match");
        }

        /// <summary>Straight back to an empty bank, costing nothing. For a match reset (#22).</summary>
        public void ResetBank()
        {
            _pending.Reset();
            _bank = _pending.Value;
        }

        private void OnLanded(TrickLanding landing)
        {
            if (landing.Trick == null)
            {
                // An ollie or a bare spin. Worth nothing, by design (#8) — say nothing, or every
                // ollie would bury the last real credit in the readout.
                return;
            }

            // The carry debuff that blocks trick credit is off by default (#8: tricks while
            // carrying keep full credit), but the hook exists and the bank honours it.
            if (Board != null && Board.TrickCreditBlocked)
            {
                Note($"{landing.Trick.Name} — no credit");
                return;
            }

            Record(_pending.CreditTrick(landing), landing.Trick.Name);
        }

        private void OnBailed() => Forfeit(ForfeitReason.Bail);

        private void Forfeit(ForfeitReason reason)
        {
            float lost = _pending.Forfeit(reason);
            string why = reason == ForfeitReason.Bail ? "bail" : "conceded";
            Note(lost > 0.0001f ? $"LOST  -{lost:0.00}  ({why})" : $"{why} — nothing banked");
            Debug.Log($"[50-50] Side {Side} forfeits {lost:0.00} ({why})");
            Forfeited?.Invoke(reason, lost);
        }

        private void Record(BankCredit credit, string name)
        {
            if (!credit.Counted)
            {
                return;
            }

            string spin = credit.SpinHalfTurns > 0 ? $" {credit.SpinHalfTurns * 180}" : "";
            string repeat = credit.Fraction < 0.999f ? $"  ({credit.Fraction * 100f:0}%)" : "";
            string capped = credit.Clamped ? "  CAPPED" : "";
            Note($"+{credit.Added:0.00}  {name}{spin}{repeat}{capped}");
            Credited?.Invoke(credit);
        }

        private void Note(string text)
        {
            _lastEvent = text;
            _lastEventTime = Time.time;
            _bank = _pending.Value;
        }
    }
}
