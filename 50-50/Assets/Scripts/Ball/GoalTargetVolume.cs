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

        public int Entries => _entries;
        public float LastEntrySpeed => _lastEntrySpeed;
        public float SecondsSinceEntry => Time.time - _lastEntryTime;

        private float _lastEntryTime = -999f;
        private BallController _pending;
        private float _returnAt;

        private void Reset()
        {
            GetComponent<BoxCollider>().isTrigger = true;
        }

        private void OnTriggerEnter(Collider other)
        {
            var ball = other.GetComponentInParent<BallController>();

            if (ball == null || Time.time - _lastEntryTime < LockoutSeconds)
            {
                return;
            }

            _entries++;
            _lastEntrySpeed = ball.Velocity.magnitude;
            _lastEntryTime = Time.time;

            Debug.Log($"[50-50] Goal #{_entries} in side {DefendedBy}'s net at {_lastEntrySpeed:0.0} m/s");

            if (ScoringEnabled)
            {
                Settle();
            }

            if (ReturnBallAfterEntry)
            {
                _pending = ball;
                _returnAt = Time.time + ReturnDelay;
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

        private void Update()
        {
            if (_pending == null || Time.time < _returnAt)
            {
                return;
            }

            _pending.ResetToSpawn();
            _pending = null;
        }
    }
}
