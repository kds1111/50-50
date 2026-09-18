using System;
using UnityEngine;

namespace FiftyFifty.Board.Tricks
{
    /// <summary>
    /// Named tricks, the spin readout, and bail. The trick half of #6.
    ///
    /// ADDITIVE BY CONSTRUCTION. This component reads BoardController and writes only to its
    /// external hooks; it never touches the movement path. Untick it in the Inspector — or
    /// untick <see cref="TricksEnabled"/> without leaving Play — and the board behaves exactly
    /// as it did before tricks existed. That is a standing requirement on #6, not a nicety:
    /// the movement system feels right today and must stay recoverable at all times.
    ///
    /// The model (#6):
    ///   - SPIN is classified, never commanded by this component. Yaw accumulates in
    ///     BoardController as it always has; on landing it is named and contributes a
    ///     multiplier. A spin is never punished and can never bail.
    ///   - NAMED TRICKS are asked for by input, run for a FIXED duration, and are the only
    ///     thing in the game that can cost a player the bank.
    ///
    /// Spin is the safe scoring lane. Named tricks are the risky one. That ladder is the whole
    /// point of the hybrid, and every rule below exists to keep it true.
    ///
    /// What this component deliberately does NOT do:
    ///   - It does not rotate the board. Flips and shuvits rotate a cosmetic child mesh only;
    ///     simulation heading is never written (#16's law, #6 rule 8).
    ///   - It does not score. It reports trick names, outcomes and a multiplier; the bank that
    ///     consumes them is #20, and what any of it is worth is #8.
    /// </summary>
    [DefaultExecutionOrder(100)]
    [RequireComponent(typeof(BoardController))]
    public class BoardTrickController : MonoBehaviour
    {
        /// <summary>
        /// One row of the trick table, Inspector-exposed so it can be driven to the right
        /// numbers by playing rather than by editing this file.
        /// </summary>
        [Serializable]
        public class TrickSettings
        {
            public string Name = "Trick";

            [Tooltip("Roll is nose-to-tail (kickflips). Yaw is vertical (shuvits). Both cosmetic.")]
            public TrickAxis Axis = TrickAxis.Roll;

            [Tooltip("Signed, in whole turns. A kickflip and a heelflip differ only in sign.")]
            public float Turns = 1f;

            [Tooltip("FIXED seconds the trick takes. Land before this elapses and you bail.\n\n" +
                     "PLACEHOLDER — expected to be wrong. These cannot be chosen honestly until " +
                     "#9's arena says what airtime its ramps give. Measure, then set.")]
            public float Seconds = 0.45f;

            [Tooltip("PLACEHOLDER. Point values are balance and belong to #8.")]
            public float BasePoints = 100f;

            public TrickDefinition ToDefinition() =>
                new TrickDefinition(Name, Axis, Turns, Seconds, BasePoints);
        }

        [Header("Master switch")]
        [Tooltip("Off gives you the board exactly as it was before tricks existed — same scene, " +
                 "no recompile. First thing to try if movement ever feels wrong.")]
        public bool TricksEnabled = true;

        [Header("The table")]
        [Tooltip("Three tricks, per the map's done-list. Slot order matches the input bindings " +
                 "on PlayerBoardInput; the bindings themselves are still open on #6.")]
        public TrickSettings[] Tricks =
        {
            new TrickSettings { Name = "Kickflip", Axis = TrickAxis.Roll, Turns = -1f, Seconds = 0.45f, BasePoints = 100f },
            new TrickSettings { Name = "Heelflip", Axis = TrickAxis.Roll, Turns = 1f, Seconds = 0.45f, BasePoints = 100f },
            new TrickSettings { Name = "Shuvit", Axis = TrickAxis.Yaw, Turns = 0.5f, Seconds = 0.40f, BasePoints = 80f },
        };

        [Header("Spin")]
        [Tooltip("How far from a clean half-turn still counts as that half-turn. Generous on " +
                 "purpose: airtime is short, so exact multiples are rare, and an unnamed rotation " +
                 "reads as a generic air rather than a guess.")]
        [Range(0.05f, 0.45f)] public float SpinTolerance = 0.2f;

        [Tooltip("PLACEHOLDER multiplier added per half-turn. Balance, and #8's to decide.")]
        public float SpinMultiplierPerHalfTurn = 0.5f;

        [Header("Bail")]
        [Tooltip("Whether an unfinished trick bails at all. Off makes every trick land, which is " +
                 "how board feel gets judged with the scoring rules out of the way.")]
        public bool BailEnabled = true;

        [Header("Input")]
        [Tooltip("How long a trick request stays alive waiting to leave the ground. Covers the " +
                 "case where the trick button is pressed a frame before the ollie.")]
        public float InputBufferSeconds = 0.25f;

        [Tooltip("Seconds the rider stays down. The board keeps its momentum and slides through " +
                 "this — it is a knockdown, not a wall.")]
        public float KnockdownSeconds = 1.1f;

        [Tooltip("Respawn at the nearest SafePoint after the knockdown. With none in the scene " +
                 "this falls back to the board's own spawn.")]
        public bool RespawnAfterKnockdown = true;

        [Header("Visual")]
        [Tooltip("Degrees per second the cosmetic mesh straightens up after a bail.")]
        public float VisualRecoverSpeed = 720f;

        [Header("Debug (read-only)")]
        [SerializeField] private string _lastTrick = "-";
        [SerializeField] private string _lastSpin = "-";
        [SerializeField] private string _lastOutcome = "-";
        [SerializeField] private float _lastMultiplier = 1f;
        [SerializeField] private bool _knockedDown;

        private BoardController _board;
        private readonly NamedTrickRun _run = new();
        private TrickDefinition[] _definitions;
        private Quaternion _restRotation = Quaternion.identity;
        private Quaternion _liveRotation = Quaternion.identity;
        private float _knockdownTimer;
        private bool _wasGrounded = true;
        private int _bufferedSlot;
        private float _bufferExpiry;

        /// <summary>The trick currently turning, or null. Drives the ball's disturbance tag.</summary>
        public TrickDefinition RunningTrick => _run.InTrick ? _run.Trick : null;

        /// <summary>
        /// The tag #7's ball reads. Only a NAMED trick makes a player punishable (#6 rule 12) —
        /// an ollie or a pure spin never does, so blocking a goal stays free — and only for the
        /// trick's own duration (rule 13), not from press to touchdown.
        /// </summary>
        public bool InNamedTrick => TricksEnabled && !_knockedDown && _run.InTrick;

        public bool KnockedDown => _knockedDown;

        public string LastTrick => _lastTrick;
        public string LastSpin => _lastSpin;
        public string LastOutcome => _lastOutcome;
        public float LastMultiplier => _lastMultiplier;
        public float TrickProgress => _run.Progress;

        /// <summary>
        /// Raised on a bail, before the knockdown starts. #7 filed the rule this exists for: a
        /// carrier who bails should fumble the ball.
        /// </summary>
        public event Action Bailed;

        /// <summary>
        /// Raised when a trick and/or spin is credited on landing: trick name (may be null),
        /// spin name, and the spin multiplier. The bank on #20 is what will consume this.
        /// </summary>
        public event Action<TrickDefinition, string, float> Landed;

        private void Awake()
        {
            _board = GetComponent<BoardController>();
            RebuildDefinitions();
        }

        private void OnEnable()
        {
            // Taking ownership of the trick tag. While this is set, BoardController stops using
            // the stand-in it shipped with on #7 — which under rule 12 is inverted rather than
            // approximate, since it treats any spin as a trick.
            _board ??= GetComponent<BoardController>();
            _board.NamedTrickTag = () => InNamedTrick;
            _board.HeadingFlippedOnLanding += OnHeadingFlipped;
        }

        /// <summary>
        /// A landing snapped the heading around (#19). The simulation now points the other way
        /// and nothing downstream will ever know — but the player just did a 180, and the board
        /// should look like it. The mesh takes the 180 the simulation gave up.
        ///
        /// Same treatment a shuvit gets: the board rides backwards from here, which is correct,
        /// because with no switch stance either end leads equally well.
        /// </summary>
        private void OnHeadingFlipped()
        {
            _restRotation = _restRotation * Quaternion.Euler(0f, 180f, 0f);
        }

        private void OnDisable()
        {
            // Hand everything back. A disabled component must leave no trace on the board.
            if (_board == null)
            {
                return;
            }

            _board.NamedTrickTag = null;
            _board.HeadingFlippedOnLanding -= OnHeadingFlipped;
            _board.TrickVisualRotation = Quaternion.identity;
            ReleaseKnockdownHolds();
            _knockedDown = false;
            _run.Reset();
            _restRotation = Quaternion.identity;
            _liveRotation = Quaternion.identity;
        }

        /// <summary>
        /// Fires whenever the Inspector changes, including mid-Play. Tuning by driving is the
        /// point (#1's working style), and it must not cost a recompile between numbers — but it
        /// must not allocate a fresh table every physics step either.
        /// </summary>
        private void OnValidate()
        {
            RebuildDefinitions();
        }

        private void RebuildDefinitions()
        {
            int count = Tricks != null ? Tricks.Length : 0;
            _definitions = new TrickDefinition[count];

            for (int i = 0; i < count; i++)
            {
                _definitions[i] = Tricks[i] != null ? Tricks[i].ToDefinition() : null;
            }
        }

        /// <summary>
        /// Runs in FixedUpdate and after BoardController's own step, so the grounded flag and
        /// accumulated yaw it reads are this tick's, not last tick's. Script execution order is
        /// set in the scene builder.
        /// </summary>
        private void FixedUpdate()
        {
            float dt = Time.fixedDeltaTime;

            if (!TricksEnabled)
            {
                if (_knockedDown)
                {
                    EndKnockdown(respawn: false);
                }

                _wasGrounded = _board.Grounded;
                return;
            }

            if (_knockedDown)
            {
                TickKnockdown(dt);
                _wasGrounded = _board.Grounded;
                return;
            }

            bool grounded = _board.Grounded;

            // Buffered on every step, grounded or not — the press that matters most is the one
            // made a frame before leaving the ground.
            BufferInput();

            if (!grounded)
            {
                TryCommitFromBuffer();
                _run.Tick(dt);
            }
            else if (!_wasGrounded)
            {
                Touchdown();
            }

            _wasGrounded = grounded;
        }

        /// <summary>
        /// Picks up a trick request, whether it was made in the air or a moment before the pop.
        ///
        /// The buffer is not a nicety. Pressing "A and B together" means pressing them one frame
        /// apart in some order, and if the trick button lands first the board is still grounded
        /// when the request arrives — so without a buffer, half of all correctly-played chords
        /// would silently do nothing, and it would look like dropped inputs rather than a rule.
        /// </summary>
        private void BufferInput()
        {
            int slot = _board.LastInput.TrickSlot;

            // 0 means nothing asked for. Slots are 1-based in the input struct so that the
            // struct's default value means "no input", which is what a network input struct
            // has to mean.
            if (slot <= 0 || slot > _definitions.Length || _definitions[slot - 1] == null)
            {
                return;
            }

            _bufferedSlot = slot;
            _bufferExpiry = Time.time + InputBufferSeconds;
        }

        private void TryCommitFromBuffer()
        {
            if (_bufferedSlot <= 0)
            {
                return;
            }

            if (Time.time > _bufferExpiry)
            {
                _bufferedSlot = 0;
                return;
            }

            // Refused if one is already committed this air: one named trick per air (#6 rule 3),
            // and no cancelling the one you are in (rule 5).
            if (_run.TryCommit(_definitions[_bufferedSlot - 1]))
            {
                _bufferedSlot = 0;
            }
        }

        private void Touchdown()
        {
            float turns = _board.AirYawTurns;
            string spin = SpinClassifier.Classify(turns, SpinTolerance);
            float multiplier = SpinClassifier.Multiplier(turns, SpinMultiplierPerHalfTurn);

            TrickDefinition attempted = _run.Trick;
            TrickLandingOutcome outcome = _run.Land();

            _lastSpin = spin;
            _lastMultiplier = multiplier;

            switch (outcome)
            {
                case TrickLandingOutcome.Landed:
                    // The mesh keeps whatever the trick left it in. A full roll is 360 degrees
                    // and therefore identity; a shuvit is 180 and stays there, which is correct
                    // — with no switch stance (#19) the board rides either way round.
                    _restRotation = _restRotation * RotationFor(attempted, 1f);
                    _liveRotation = _restRotation;
                    _lastTrick = attempted.Name;
                    _lastOutcome = "LANDED";
                    Landed?.Invoke(attempted, spin, multiplier);
                    break;

                case TrickLandingOutcome.Bailed:
                    _lastTrick = attempted != null ? attempted.Name : "-";

                    if (BailEnabled)
                    {
                        _lastOutcome = "BAIL";
                        BeginKnockdown();
                    }
                    else
                    {
                        _lastOutcome = "bail (disabled)";
                        _liveRotation = _restRotation;
                    }

                    break;

                default:
                    _lastTrick = "-";
                    _lastOutcome = spin == SpinClassifier.GenericAir ? "air" : "spin";
                    _liveRotation = _restRotation;
                    Landed?.Invoke(null, spin, multiplier);
                    break;
            }
        }

        private void BeginKnockdown()
        {
            _knockedDown = true;
            _knockdownTimer = KnockdownSeconds;

            // The board keeps its momentum and slides (#6 rule 15). Zeroing the scales removes
            // drive and steering without removing velocity, which is the difference between a
            // knockdown and hitting a wall.
            _board.ExternalTopSpeedScale = 0f;
            _board.ExternalTurnScale = 0f;
            _board.OllieBlocked = true;
            _board.TrickCreditBlocked = true;

            Bailed?.Invoke();
        }

        private void TickKnockdown(float dt)
        {
            // Re-asserted every step: BallHandler writes TrickCreditBlocked from its own state
            // each tick, so a one-shot set here would be overwritten.
            _board.ExternalTopSpeedScale = 0f;
            _board.ExternalTurnScale = 0f;
            _board.OllieBlocked = true;

            _knockdownTimer -= dt;

            if (_knockdownTimer <= 0f)
            {
                EndKnockdown(RespawnAfterKnockdown);
            }
        }

        private void EndKnockdown(bool respawn)
        {
            _knockedDown = false;
            ReleaseKnockdownHolds();
            _run.Reset();
            _bufferedSlot = 0;
            _restRotation = Quaternion.identity;

            if (!respawn)
            {
                return;
            }

            SafePoint point = SafePoint.Nearest(transform.position);

            if (point != null)
            {
                _board.RespawnAt(point.transform.position, point.transform.eulerAngles.y);
            }
            else
            {
                // No safe points in the scene. #9 owes them; until then this is the old
                // behaviour rather than a hard failure.
                _board.Respawn();
            }
        }

        private void ReleaseKnockdownHolds()
        {
            _board.ExternalTopSpeedScale = 1f;
            _board.ExternalTurnScale = 1f;
            _board.OllieBlocked = false;
        }

        private Quaternion RotationFor(TrickDefinition trick, float progress)
        {
            if (trick == null)
            {
                return Quaternion.identity;
            }

            float degrees = trick.Turns * 360f * progress;

            // Roll is about the board's nose-to-tail axis, which is local Z — the same axis the
            // visual lean uses. Yaw is local Y.
            return trick.Axis == TrickAxis.Roll
                ? Quaternion.Euler(0f, 0f, degrees)
                : Quaternion.Euler(0f, degrees, 0f);
        }

        /// <summary>
        /// Cosmetic only, and outside the physics step because it changes nothing. The result
        /// is handed to BoardController, which composes it with the visual lean — so a board
        /// with no trick component gets identity and looks exactly as it always did.
        /// </summary>
        private void Update()
        {
            if (_board == null)
            {
                return;
            }

            if (!TricksEnabled)
            {
                _board.TrickVisualRotation = Quaternion.identity;
                return;
            }

            if (_run.Committed)
            {
                _liveRotation = _restRotation * RotationFor(_run.Trick, _run.Progress);
            }
            else
            {
                // Straighten up rather than snap: a bail leaves the mesh mid-roll, and popping
                // it level in one frame reads as a glitch.
                _liveRotation = Quaternion.RotateTowards(
                    _liveRotation, _restRotation, VisualRecoverSpeed * Time.deltaTime);
            }

            _board.TrickVisualRotation = _liveRotation;
        }
    }
}
