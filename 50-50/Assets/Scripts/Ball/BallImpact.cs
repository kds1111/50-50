using FiftyFifty.Board;
using FiftyFifty.Board.Grinds;
using UnityEngine;

namespace FiftyFifty.Ball
{
    /// <summary>
    /// What a ball does to a player it hits (#7). Sits on the board, next to the controller.
    ///
    /// The rule is narrow on purpose: you are only disturbed while airborne AND in a trick.
    /// A plain ollie is never punished, because ollieing to block a goal should not be a
    /// gamble, and on the ground nothing happens at all — the ball simply bounces off you.
    /// That narrowness is not only a design call, it deletes the whole "ball shoves the board
    /// around on the floor" physics path we would otherwise have to tune.
    ///
    /// Until #6 lands, "in a trick" is BoardController's yaw stand-in. Swap that one property
    /// for the real trick tag and this class does not change.
    /// </summary>
    [RequireComponent(typeof(BoardController))]
    public class BallImpact : MonoBehaviour
    {
        [Tooltip("Impulse that counts as a full-strength hit. Anything harder is capped.")]
        public float FullStrengthImpulse = 6f;

        [Tooltip("Hits weaker than this are ignored entirely, so a ball rolling into your wheels " +
                 "mid-spin does not read as a collision.")]
        public float MinimumImpulse = 0.4f;

        [Tooltip("Off makes you immune, for judging board feel with the ball switched out of it.")]
        public bool Enabled = true;

        [Header("Debug (read-only)")]
        [SerializeField] private float _lastImpulse;
        [SerializeField] private string _lastVerdict = "-";

        public string LastVerdict => _lastVerdict;

        private BoardController _board;
        private BoardGrindController _grind;

        private void Awake()
        {
            _board = GetComponent<BoardController>();
            _grind = GetComponent<BoardGrindController>();
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (!Enabled || _board == null)
            {
                return;
            }

            if (collision.rigidbody == null ||
                collision.rigidbody.GetComponent<BallController>() == null)
            {
                return;
            }

            float impulse = collision.impulse.magnitude;
            _lastImpulse = impulse;

            if (impulse < MinimumImpulse)
            {
                _lastVerdict = "ignored (too soft)";
                return;
            }

            // A grinder counts as grounded, but is not safe: a hit knocks you off the rail, and
            // coming off is a fall (#12). Asked before the grounded check for exactly that reason.
            if (_grind != null && _grind.Grinding)
            {
                _grind.KnockOff();
                _lastVerdict = $"KNOCKED OFF RAIL ({impulse:0.0})";
                return;
            }

            if (_board.Grounded)
            {
                _lastVerdict = "ignored (grounded)";
                return;
            }

            if (!_board.InTrick)
            {
                _lastVerdict = "ignored (ollie, not a trick)";
                return;
            }

            _board.Disturb(Mathf.Clamp01(impulse / Mathf.Max(0.01f, FullStrengthImpulse)));
            _lastVerdict = $"DISTURBED ({impulse:0.0})";
        }
    }
}
