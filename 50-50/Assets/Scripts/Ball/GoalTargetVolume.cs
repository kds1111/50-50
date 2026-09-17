using UnityEngine;

namespace FiftyFifty.Ball
{
    /// <summary>
    /// A hole to shoot at. Deliberately dumb (#7): it counts entries and says so, and that is
    /// all. No score, no cementing, no kickoff — the bank and the match lifecycle are #8's, and
    /// a target that scored would prejudge them.
    ///
    /// It exists to answer one question: does shooting feel good?
    /// </summary>
    [RequireComponent(typeof(BoxCollider))]
    public class GoalTargetVolume : MonoBehaviour
    {
        [Tooltip("Put the ball back at its spawn a moment after it goes in. Off by default — " +
                 "a reset that happens on its own is a kickoff, and kickoffs are #8's decision.")]
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

            if (ball == null)
            {
                return;
            }

            _entries++;
            _lastEntrySpeed = ball.Velocity.magnitude;
            _lastEntryTime = Time.time;

            Debug.Log($"[50-50] Target hit #{_entries} at {_lastEntrySpeed:0.0} m/s");

            if (ReturnBallAfterEntry)
            {
                _pending = ball;
                _returnAt = Time.time + ReturnDelay;
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
