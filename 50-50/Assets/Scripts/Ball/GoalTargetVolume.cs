using FiftyFifty.Match;
using FiftyFifty.Scoring;
using UnityEngine;

namespace FiftyFifty.Ball
{
    /// <summary>
    /// A goal (#20). The ball going in cements the attacking side's bank and forfeits the
    /// defending side's, regardless of who touched it last — so an own goal counts for the
    /// opponent.
    ///
    /// It still does not reset anything. A kickoff is #22's, which is why the lockout below
    /// exists: with nothing putting the ball back, it rattles around in the net.
    ///
    /// A carried ball has its collider off (#7), so it cannot be carried in. It has to be punched.
    /// </summary>
    [RequireComponent(typeof(BoxCollider))]
    public class GoalTargetVolume : MonoBehaviour
    {
        [Header("Scoring (#20)")]
        [Tooltip("The side whose net this is. A ball in here pays the OTHER side and costs this " +
                 "one its bank. Side A starts at the -Z end by convention.")]
        public Side DefendedBy = Side.A;

        [Tooltip("Off makes this the dumb target from #7 again: it counts entries and touches no bank.")]
        public bool ScoringEnabled = true;

        [Tooltip("Seconds after a goal during which this goal ignores the ball. Without a kickoff " +
                 "(#22) a ball in the net bounces in and out of this volume and would score on " +
                 "every bounce.")]
        public float LockoutSeconds = 3f;

        [Header("Ball return")]
        [Tooltip("Put the ball back at its spawn a moment after it goes in. Off by default — " +
                 "a reset that happens on its own is a kickoff, and kickoffs are #22's.")]
        public bool ReturnBallAfterEntry = false;

        [Tooltip("Seconds to wait before that return.")]
        public float ReturnDelay = 1.5f;

        [Header("Debug (read-only)")]
        [SerializeField] private int _entries;
        [SerializeField] private float _lastEntrySpeed;

        /// <summary>
        /// Raised on every counted goal, after the banks are settled. The match (#24) listens for
        /// it to run a kickoff; the goal itself never resets anything.
        /// </summary>
        public static event System.Action<GoalTargetVolume> Scored;

        public int Entries => _entries;
        public float LastEntrySpeed => _lastEntrySpeed;

        private MatchDirector _director;
        private BallController _pending;

        // Both counted down on the simulation step, never measured against the wall clock (#26):
        // the lockout guards a bank mutation, and the return moves a rigidbody. Wall-clock time
        // does not rewind, so a replayed tick would read a different answer than the tick it
        // replaces.
        private float _lockoutRemaining;
        private float _returnRemaining;

        private void Reset()
        {
            GetComponent<BoxCollider>().isTrigger = true;
        }

        /// <summary>
        /// Whether the match is in a phase where a goal counts (#29). No director, or one that
        /// is switched off, means there is no match to be outside of — so scoring stands.
        /// </summary>
        private bool MatchAllowsScoring =>
            _director == null || !_director.isActiveAndEnabled || _director.ScoringOpen;

        private void Start()
        {
            _director = FindFirstObjectByType<MatchDirector>();
        }

        private void OnTriggerEnter(Collider other)
        {
            var ball = other.GetComponentInParent<BallController>();

            if (ball == null || _lockoutRemaining > 0f)
            {
                return;
            }

            // #29: a goal only counts while the match is being played — and an entry that does
            // not count is not an entry at all. This gate used to sit further down, over the
            // settle alone, so a ball crossing the line during a pause still took the counter,
            // the log line and the whole lockout with it: the lockout then outlived the pause
            // and swallowed the first real goal of resumed play.
            if (!MatchAllowsScoring)
            {
                return;
            }

            _entries++;
            _lastEntrySpeed = ball.Velocity.magnitude;
            _lockoutRemaining = LockoutSeconds;

            Debug.Log($"[50-50] Goal #{_entries} in side {DefendedBy}'s net at {_lastEntrySpeed:0.0} m/s");

            // The phase was settled above; what is left is whether this volume scores at all.
            if (ScoringEnabled)
            {
                Settle();
            }

            Scored?.Invoke(this);

            if (ReturnBallAfterEntry)
            {
                _pending = ball;
                _returnRemaining = ReturnDelay;
            }
        }

        /// <summary>
        /// Every bank in the scene, both sides. Iterates a copy: cementing and forfeiting raise
        /// events, and a listener that disabled a bank would otherwise change the list mid-walk.
        /// </summary>
        private void Settle()
        {
            PlayerBank[] banks = new PlayerBank[PlayerBank.All.Count];

            for (int i = 0; i < banks.Length; i++)
            {
                banks[i] = PlayerBank.All[i];
            }

            foreach (PlayerBank bank in banks)
            {
                if (GoalRule.For(bank.Side, DefendedBy) == GoalOutcome.Cement)
                {
                    bank.ScoreGoal();
                }
                else
                {
                    bank.Concede();
                }
            }
        }

        private void FixedUpdate()
        {
            float dt = Time.fixedDeltaTime;

            if (_lockoutRemaining > 0f)
            {
                _lockoutRemaining -= dt;
            }

            if (_pending == null)
            {
                return;
            }

            _returnRemaining -= dt;

            if (_returnRemaining > 0f)
            {
                return;
            }

            _pending.ResetToSpawn();
            _pending = null;
        }
    }
}
