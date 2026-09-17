using FiftyFifty.Board;
using UnityEngine;

namespace FiftyFifty.Ball
{
    /// <summary>
    /// Everything a body can do to the ball: grab it, carry it, punch it, and lose it (#7).
    ///
    /// Put one on the player's board and one on a dummy defender — it works either way. With a
    /// BoardController it takes its intent from that board's last input and applies the carry
    /// debuffs to it; without one it is a post that can hold a ball and be robbed, which is what
    /// makes stripping testable before a bot exists.
    ///
    /// Three rules from the grill are load-bearing here, and all three are toggles:
    ///   - Releasing does not throw. PUNCH is the shot, and punching while carrying releases
    ///     and strikes in the same tick.
    ///   - The hold limit ends in a fumble plus a grab cooldown. Deliberately NOT a bail —
    ///     bail semantics belong to #6.
    ///   - Carrying costs top speed, not the bank. Tricks stay legal while carrying, because
    ///     blocking trick credit would also make the carrier immune to mid-air disturbance,
    ///     and carrying would become the safest state in the game.
    ///
    /// Runs after the board (execution order) so it reads input the board has already taken
    /// this tick. It must never call Read() itself — that would eat the board's one-shot latches.
    /// </summary>
    [DefaultExecutionOrder(100)]
    public class BallHandler : MonoBehaviour
    {
        [Header("Wiring")]
        [Tooltip("The ball. Found in the scene if left empty.")]
        public BallController Ball;

        [Tooltip("The board this handler rides on. Leave empty for a dummy that just stands there.")]
        public BoardController Board;

        [Header("Carry point")]
        [Tooltip("Where the ball sits while carried, in local space. Offset to one side and at " +
                 "chest height on purpose: a ball held straight ahead sits in the middle of the " +
                 "screen, in the lane you are about to punch it down.")]
        public Vector3 CarryOffset = new Vector3(0.38f, 1.05f, 0.25f);

        [Header("Grab")]
        public bool GrabEnabled = true;

        [Tooltip("How close the ball has to be to the carry point to be caught.")]
        public float CarryRadius = 1.6f;

        [Tooltip("Fastest the ball may be closing on you and still be caught, m/s. This is what " +
                 "stops a hard shot being vacuumed out of the air — it bounces off instead.")]
        public float MaxClosingSpeed = 9f;

        [Tooltip("Grabbing is allowed in the air. An aerial catch is a real play (#7).")]
        public bool AllowAirborneGrab = true;

        [Tooltip("Seconds after you let go before you can grab again, so holding the button " +
                 "through a punch does not instantly re-catch your own shot.")]
        public float RegrabDelay = 0.35f;

        [Header("Hold limit")]
        [Tooltip("Off makes possession unlimited — useful for tuning the carry, wrong for a game.")]
        public bool HoldLimitEnabled = true;

        [Tooltip("Seconds you may hold the ball before it is fumbled.")]
        public float HoldSeconds = 3f;

        [Tooltip("Seconds you cannot grab after the timer runs out. Without this, carry into " +
                 "re-grab into carry is a permanent state and the timer is not a limit.")]
        public float GrabCooldown = 1.5f;

        [Tooltip("How hard a fumble throws the ball forward. Forward rather than at your feet, " +
                 "so running the clock out still advances the ball instead of stalling play.")]
        public float FumbleForwardSpeed = 6f;

        [Header("Punch — the shot")]
        public bool PunchEnabled = true;

        [Tooltip("Impulse a punch adds, m/s. Your own velocity is added on top.")]
        public float PunchPower = 14f;

        [Tooltip("Upward part of a punch, as a fraction of its power. A little lift keeps shots " +
                 "off the floor without turning every punch into a lob.")]
        [Range(0f, 1f)] public float PunchLift = 0.18f;

        [Tooltip("Total width of the punch arc in front of you, in degrees.")]
        public float PunchConeDegrees = 90f;

        [Tooltip("How far a punch reaches, measured flat along the ground, in metres.")]
        public float PunchReach = 2f;

        [Tooltip("How far above the punch origin the ball can be and still be hit.")]
        public float PunchReachUp = 1.2f;

        [Tooltip("How far below the punch origin the ball can be and still be hit. Generous: " +
                 "a loose ball sits on the floor, roughly a metre under your hands.")]
        public float PunchReachDown = 1.8f;

        [Tooltip("Seconds between punches.")]
        public float PunchCooldown = 0.4f;

        [Tooltip("ON: you must drop the ball before you can shoot it — a slower, more contested " +
                 "game. OFF (default): punching while carrying releases and strikes in one press.")]
        public bool TwoBeatRelease = false;

        [Tooltip("A punch that strips a carrier sends the ball away along your aim. Off drops it " +
                 "at their feet instead.")]
        public bool StripSendsBall = true;

        [Header("Carry debuffs — play these against each other")]
        [Tooltip("Top speed lost while carrying, as a fraction.")]
        [Range(0f, 0.9f)] public float CarryTopSpeedPenalty = 0.15f;

        [Tooltip("Steering lost while carrying, as a fraction.")]
        [Range(0f, 0.9f)] public float CarryTurnPenalty = 0f;

        [Tooltip("Forbid the ollie while carrying.")]
        public bool CarryBlocksOllie = false;

        [Tooltip("Tricks land but are credited nothing while carrying. WARNING: with this on, a " +
                 "carrier is never 'in a trick', so ball contact cannot disturb them either — " +
                 "carrying becomes the safest state in the game. Default off for that reason.")]
        public bool CarryBlocksTrickCredit = false;

        [Header("Dummy defender")]
        [Tooltip("Grabs the ball whenever it is loose and in range, with no input at all. " +
                 "Turns a static prop into something to practise stripping against.")]
        public bool AutoGrab = false;

        [Header("Debug (read-only)")]
        [SerializeField] private bool _carrying;
        [SerializeField] private float _holdRemaining;
        [SerializeField] private float _grabCooldownRemaining;
        [SerializeField] private float _punchCooldownRemaining;
        [SerializeField] private float _lastPunchSpeed;
        [SerializeField] private string _lastEvent = "-";

        public bool Carrying => _carrying;

        /// <summary>Would a punch connect right now? On the HUD, so a whiff is legible.</summary>
        public bool BallInPunchArc => Ball != null && !Ball.Carried && InPunchArc();
        public float HoldRemaining => _holdRemaining;
        public float GrabCooldownRemaining => _grabCooldownRemaining;
        public float PunchCooldownRemaining => _punchCooldownRemaining;
        public float LastPunchSpeed => _lastPunchSpeed;
        public string LastEvent => _lastEvent;

        /// <summary>Where the ball sits while this handler carries it.</summary>
        public Vector3 CarryWorldPoint => BodyPosition + (BodyRotation * CarryOffset);

        // Board.Body is null outside play mode (Awake has not run), which gizmos hit.
        private Vector3 BodyPosition =>
            Board != null && Board.Body != null ? Board.Body.position : transform.position;

        // A dummy never moves, so reading its transform is safe. A board's transform is the
        // rendered pose during the physics step and must never be read there.
        private Quaternion BodyRotation =>
            Board != null ? Quaternion.Euler(0f, Board.Heading, 0f) : transform.rotation;

        private Vector3 BodyVelocity =>
            Board != null && Board.Body != null ? Board.Body.linearVelocity : Vector3.zero;

        private Vector3 AimDirection => BodyRotation * Vector3.forward;

        private void Awake()
        {
            if (Ball == null)
            {
                Ball = FindFirstObjectByType<BallController>();
            }

            if (Board == null)
            {
                Board = GetComponent<BoardController>();
            }
        }

        private void FixedUpdate()
        {
            SimulateTick(Time.fixedDeltaTime);
        }

        /// <summary>One step of possession. Public and dt-explicit, like everything else here.</summary>
        public void SimulateTick(float dt)
        {
            _grabCooldownRemaining = Mathf.Max(0f, _grabCooldownRemaining - dt);
            _punchCooldownRemaining = Mathf.Max(0f, _punchCooldownRemaining - dt);

            if (Ball == null)
            {
                return;
            }

            BoardInputState input = Board != null ? Board.LastInput : default;
            bool grabHeld = AutoGrab || input.GrabHeld;

            if (_carrying)
            {
                Ball.SetCarryPoint(CarryWorldPoint, BodyVelocity);
                TickHoldTimer(dt);
            }

            if (input.PunchPressed)
            {
                TryPunch();
            }

            if (_carrying && !grabHeld)
            {
                Drop();
            }
            else if (!_carrying && grabHeld)
            {
                TryGrab();
            }

            ApplyCarryDebuffs();
        }

        private void TickHoldTimer(float dt)
        {
            if (!HoldLimitEnabled)
            {
                _holdRemaining = HoldSeconds;
                return;
            }

            _holdRemaining -= dt;

            if (_holdRemaining > 0f)
            {
                return;
            }

            // Fumbled, not bailed. What a bail costs is #6's decision, not this ticket's.
            Ball.Release(BodyVelocity + (AimDirection * FumbleForwardSpeed), Ball.FumbleClip);
            _carrying = false;
            _grabCooldownRemaining = GrabCooldown;
            _lastEvent = "FUMBLE (held too long)";
        }

        private void TryGrab()
        {
            if (!GrabEnabled || _grabCooldownRemaining > 0f || Ball.Carried)
            {
                return;
            }

            if (!AllowAirborneGrab && Board != null && !Board.Grounded)
            {
                return;
            }

            if (Vector3.Distance(Ball.Position, CarryWorldPoint) > CarryRadius)
            {
                return;
            }

            // Closing speed, not raw speed: a ball travelling alongside you at 20 m/s is catchable,
            // one arriving at your chest at 20 m/s is a shot and should bounce off.
            Vector3 relative = Ball.Velocity - BodyVelocity;
            Vector3 toCarrier = (CarryWorldPoint - Ball.Position).normalized;
            float closing = Vector3.Dot(relative, toCarrier);

            if (closing > MaxClosingSpeed)
            {
                _lastEvent = $"missed catch (closing {closing:0.0} m/s)";
                return;
            }

            Ball.Attach(this, CarryWorldPoint, BodyVelocity);
            _carrying = true;
            _holdRemaining = HoldSeconds;
            _lastEvent = "GRAB";
        }

        private void Drop()
        {
            Ball.Release(BodyVelocity);
            _carrying = false;
            _grabCooldownRemaining = Mathf.Max(_grabCooldownRemaining, RegrabDelay);
            _lastEvent = "drop";
        }

        private void TryPunch()
        {
            if (!PunchEnabled || _punchCooldownRemaining > 0f)
            {
                return;
            }

            _punchCooldownRemaining = PunchCooldown;

            if (_carrying)
            {
                if (TwoBeatRelease)
                {
                    _lastEvent = "punch blocked (two-beat: drop first)";
                    return;
                }

                _carrying = false;
                _grabCooldownRemaining = Mathf.Max(_grabCooldownRemaining, RegrabDelay);
                Strike("PUNCH (from carry)");
                return;
            }

            if (!InPunchArc())
            {
                _lastEvent = "punch — whiff";
                return;
            }

            // Stripping someone. The ball is at their carry point, so aiming at the body and
            // aiming at the ball are nearly the same shot — which is the distinction #17 has
            // to decide is worth keeping.
            if (Ball.Carried && Ball.Holder is BallHandler victim && victim != this)
            {
                victim.ForceRelease();

                if (!StripSendsBall)
                {
                    Ball.Release(Vector3.zero, Ball.PunchClip);
                    _lastEvent = "STRIP (dropped at their feet)";
                    return;
                }

                Strike("STRIP");
                return;
            }

            Strike("PUNCH");
        }

        private void Strike(string label)
        {
            Vector3 direction = (AimDirection + (Vector3.up * PunchLift)).normalized;
            Vector3 velocity = BodyVelocity + (direction * PunchPower);

            Ball.Release(velocity, Ball.PunchClip);

            _lastPunchSpeed = velocity.magnitude;
            _lastEvent = $"{label} {_lastPunchSpeed:0.0} m/s";
        }

        /// <summary>
        /// The arc is measured FLAT, with height handled separately. Measuring the angle in 3D
        /// looks right and plays wrong: the punch starts at chest height and a loose ball sits
        /// on the floor, so standing next to it puts the ball nearly 60 degrees below your aim
        /// and every punch at your own feet whiffs.
        /// </summary>
        private bool InPunchArc()
        {
            Vector3 origin = PunchOrigin;
            Vector3 toBall = Ball.Position - origin;

            float height = toBall.y;
            if (height > PunchReachUp + Ball.Radius || height < -(PunchReachDown + Ball.Radius))
            {
                return false;
            }

            Vector3 flat = new Vector3(toBall.x, 0f, toBall.z);
            if (flat.magnitude > PunchReach + Ball.Radius)
            {
                return false;
            }

            // Right on top of the ball: there is no direction to compare, so it counts.
            if (flat.sqrMagnitude < 0.0001f)
            {
                return true;
            }

            Vector3 aimFlat = new Vector3(AimDirection.x, 0f, AimDirection.z);

            return Vector3.Angle(aimFlat, flat) <= PunchConeDegrees * 0.5f;
        }

        private Vector3 PunchOrigin => BodyPosition + (BodyRotation * new Vector3(0f, CarryOffset.y, 0f));

        /// <summary>Someone took it off us. Called by whoever did.</summary>
        public void ForceRelease()
        {
            _carrying = false;
            _grabCooldownRemaining = Mathf.Max(_grabCooldownRemaining, GrabCooldown);
            _lastEvent = "STRIPPED";
        }

        private void ApplyCarryDebuffs()
        {
            if (Board == null)
            {
                return;
            }

            Board.ExternalTopSpeedScale = _carrying ? 1f - CarryTopSpeedPenalty : 1f;
            Board.ExternalTurnScale = _carrying ? 1f - CarryTurnPenalty : 1f;
            Board.OllieBlocked = _carrying && CarryBlocksOllie;
            Board.TrickCreditBlocked = _carrying && CarryBlocksTrickCredit;
        }

        /// <summary>Puts possession back to nothing. Used by the test scene's reset keys.</summary>
        public void ResetPossession()
        {
            _carrying = false;
            _holdRemaining = HoldSeconds;
            _grabCooldownRemaining = 0f;
            _punchCooldownRemaining = 0f;
            _lastEvent = "-";
            ApplyCarryDebuffs();
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.2f, 0.9f, 0.6f, 0.9f);
            Gizmos.DrawWireSphere(CarryWorldPoint, 0.12f);

            Vector3 origin = PunchOrigin;
            Gizmos.color = new Color(1f, 0.5f, 0.15f, 0.9f);

            float half = PunchConeDegrees * 0.5f;
            for (float angle = -half; angle <= half; angle += half)
            {
                Vector3 direction = Quaternion.AngleAxis(angle, Vector3.up) * AimDirection;
                Gizmos.DrawLine(origin, origin + (direction * PunchReach));
            }
        }
    }
}
