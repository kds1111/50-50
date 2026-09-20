using System;
using FiftyFifty.Board.Tricks;
using FiftyFifty.Scoring;
using UnityEngine;

namespace FiftyFifty.Board.Grinds
{
    /// <summary>
    /// Grinds (#12): lock onto a rail or a ledge edge, ride it, and get off — cleanly or not.
    ///
    /// ADDITIVE, like the trick system. It plugs into BoardController through one hook
    /// (<see cref="IBoardMotionOverride"/>) and only takes the board while locked. Untick it and
    /// the hook is null, and the board is exactly what it was before grinds existed.
    ///
    /// The model, as grilled on #12:
    ///   - LOCK-ON, not physics contact. An airborne board coming down near a GrindRail line,
    ///     travelling along it fast enough, is snapped onto it. Position follows the line and
    ///     velocity runs along it; the solver never balances the board on a bar.
    ///   - The NAME is the board's angle to the rail at lock-on: 50-50 along it, Boardslide across
    ///     it, nearest wins. The heading snaps to match and stays there.
    ///   - Locking on is a TOUCHDOWN. A trick still running bails; a finished one and its spin are
    ///     credited there and then. Popping out is a fresh air.
    ///   - On the rail: momentum only. Friction slows you, slopes pull you, the throttle and
    ///     steering do nothing.
    ///   - OFF the rail: ollie out (clean, and the stick picks a side), ride off the end (clean),
    ///     stall below a speed (a fall), or get hit by the ball (a fall). A fall is the trick
    ///     system's own bail. Anything under the bank's minimum grind time is a graze and is
    ///     forgiven whichever way it ended.
    ///
    /// Clean exits bank the grind through PlayerBank; the price is the bank's (#8).
    /// </summary>
    [RequireComponent(typeof(BoardController))]
    public class BoardGrindController : MonoBehaviour, IBoardMotionOverride
    {
        /// <summary>One row of the grind table. Every grind shares one rate to start (#8).</summary>
        [Serializable]
        public class GrindSettings
        {
            public string Name = GrindRules.FiftyFifty;

            [Tooltip("Bank added per second on the rail, once past the bank's minimum grind time. " +
                     "Stored per name so pricing grinds apart later is a data change.")]
            public float RatePerSecond = 0.2f;
        }

        [Header("Master switch")]
        [Tooltip("Off: rails and ledges are scenery again, exactly as before #12. No recompile.")]
        public bool GrindsEnabled = true;

        [Header("The table")]
        [Tooltip("Names come from the board's angle to the rail at lock-on (GrindRules). A name " +
                 "missing from this table grinds but pays nothing.")]
        public GrindSettings[] Grinds =
        {
            new GrindSettings { Name = GrindRules.FiftyFifty, RatePerSecond = 0.2f },
            new GrindSettings { Name = GrindRules.Boardslide, RatePerSecond = 0.2f },
        };

        [Header("Lock-on")]
        [Tooltip("How far sideways from a grind line the board can be and still lock on, metres.")]
        public float SnapRadius = 0.35f;

        [Tooltip("How far above or below riding height on the line still locks on, metres.")]
        public float SnapHeight = 0.3f;

        [Tooltip("Most the direction of travel may be off the rail's line, degrees. Crossing a " +
                 "rail at a steep angle is jumping over it, not grinding it.")]
        public float MaxTravelAngle = 45f;

        [Tooltip("Slowest speed along the rail that locks on, m/s.")]
        public float MinLockSpeed = 2f;

        [Tooltip("No locking on within this many metres of a line's end — there would be nothing " +
                 "left to grind.")]
        public float EndMargin = 0.1f;

        [Header("On the rail")]
        [Tooltip("Deceleration while grinding, m/s squared. One value for every grind to start.")]
        public float Friction = 1.5f;

        [Tooltip("Below this speed along the rail you stall off it, m/s. A stall is a fall.")]
        public float StallSpeed = 1f;

        [Tooltip("Sideways speed a fall pushes you off with, m/s, so you drop beside the rail " +
                 "rather than onto it.")]
        public float FallOffSideSpeed = 1.5f;

        [Header("Popping out")]
        [Tooltip("Sideways speed added when you ollie out with the stick held left or right, m/s. " +
                 "Stick neutral pops straight up.")]
        public float PopOutSideSpeed = 2.5f;

        [Tooltip("Stick travel needed to count as choosing a side.")]
        [Range(0f, 1f)] public float SideStickDeadzone = 0.3f;

        [Tooltip("Seconds in the air before you can lock back onto the line you just left. Stops a " +
                 "pop re-locking instantly; a real hop back onto the same rail still works.")]
        public float RelockAirSeconds = 0.25f;

        [Tooltip("If the board ends up this far from where the grind put it — a respawn, a " +
                 "teleport — the grind simply ends, with no credit and no fall.")]
        public float LostTrackDistance = 1f;

        [Header("Wiring")]
        [Tooltip("Found on this object if left empty. A fall bails through it.")]
        public BoardTrickController Tricks;

        [Tooltip("Found on this object if left empty. Clean exits are banked through it.")]
        public PlayerBank Bank;

        [Header("Debug (read-only)")]
        [SerializeField] private bool _grinding;
        [SerializeField] private string _grindName = "-";
        [SerializeField] private float _seconds;
        [SerializeField] private float _speedOnRail;
        [SerializeField] private string _lastExit = "-";

        private BoardController _board;
        private readonly GrindLockLimits _limits = new();
        private GrindRail _rail;
        private int _lineIndex;
        private GrindRail.Line _line;
        private float _t;
        private float _direction;
        private float _heading;
        private float _approachSide;
        private bool _knockPending;
        private GrindRail _lastRail;
        private int _lastLineIndex = -1;
        private float _airSinceExit = 999f;

        public bool Grinding => _grinding;
        public string CurrentGrind => _grinding ? _grindName : null;
        public float GrindSeconds => _seconds;
        public string LastExit => _lastExit;

        /// <summary>What popping out right now would add to the bank. 0 during a graze.</summary>
        public float PendingPay => _grinding && Bank != null && Bank.isActiveAndEnabled
            ? Bank.PreviewGrind(_grindName, _seconds, RateFor(_grindName))
            : 0f;

        /// <summary>Still inside the bank's minimum time: nothing earned, nothing at risk.</summary>
        public bool InGraze => _grinding && IsGraze(_seconds);

        private void Awake()
        {
            _board = GetComponent<BoardController>();

            if (Tricks == null)
            {
                Tricks = GetComponent<BoardTrickController>();
            }

            if (Bank == null)
            {
                Bank = GetComponent<PlayerBank>();
            }
        }

        private void OnEnable()
        {
            if (_board == null)
            {
                _board = GetComponent<BoardController>();
            }

            _board.MotionOverride = this;
        }

        private void OnDisable()
        {
            if (_board == null)
            {
                return;
            }

            if (_grinding)
            {
                Release(Vector3.zero);
                _lastExit = "disabled";
            }

            if (ReferenceEquals(_board.MotionOverride, this))
            {
                _board.MotionOverride = null;
            }
        }

        /// <summary>
        /// The ball hit a grinding player (#12 Q4). Handled on the next step, inside the
        /// simulation, rather than from the collision callback.
        /// </summary>
        public void KnockOff()
        {
            if (_grinding)
            {
                _knockPending = true;
            }
        }

        /// <summary>Called by BoardController at the top of every step. True while this owns the board.</summary>
        public bool Step(BoardController board, float dt)
        {
            bool blocked = !GrindsEnabled || (Tricks != null && Tricks.KnockedDown);

            if (!_grinding)
            {
                if (!board.Grounded)
                {
                    _airSinceExit += dt;
                }

                return !blocked && TryLock(board, dt);
            }

            Rigidbody rb = board.Body;
            Vector3 expected = _line.PointAt(_t) + (Vector3.up * board.RideHeight);

            if ((rb.position - expected).magnitude > LostTrackDistance)
            {
                // Teleported — a respawn. The board is somewhere else now, moving however the
                // respawn left it; handing it the grind's velocity would fling it from the spawn.
                Abandon();
                return false;
            }

            if (blocked)
            {
                return End(GrindExit.Interrupted, board);
            }

            if (_knockPending)
            {
                return End(GrindExit.KnockedOff, board);
            }

            BoardInputState input = board.LastInput;

            if (input.PopPressed && board.CanOllie)
            {
                // Stay locked for this one tick so the controller's own ollie fires from here:
                // one pop, the same pop as everywhere else.
                End(GrindExit.Popped, board);
                AddSideKick(rb, input.Steer);
                return true;
            }

            float speed = _speedOnRail - (Friction * dt);
            speed += Vector3.Dot(Physics.gravity * board.GravityScale, _line.Dir * _direction) * dt;
            _speedOnRail = speed;

            if (speed < StallSpeed)
            {
                return End(GrindExit.Stalled, board);
            }

            _t += _direction * speed * dt;
            _seconds += dt;

            if (_t < 0f || _t > _line.Length)
            {
                return End(GrindExit.RodeOffEnd, board);
            }

            Hold(board, dt);
            return true;
        }

        private bool TryLock(BoardController board, float dt)
        {
            Rigidbody rb = board.Body;
            Vector3 position = rb.position;
            Vector3 velocity = rb.linearVelocity;

            _limits.SnapRadius = SnapRadius;
            _limits.SnapHeight = SnapHeight;
            _limits.MaxTravelAngle = MaxTravelAngle;
            _limits.MinLockSpeed = MinLockSpeed;

            Vector3 flatVelocity = Vector3.ProjectOnPlane(velocity, Vector3.up);
            GrindRail bestRail = null;
            int bestLine = -1;
            float bestScore = float.MaxValue;
            float bestT = 0f;
            float bestAlong = 0f;

            foreach (GrindRail rail in GrindRail.All)
            {
                for (int i = 0; i < rail.Lines.Count; i++)
                {
                    if (rail == _lastRail && i == _lastLineIndex && _airSinceExit < RelockAirSeconds)
                    {
                        continue;
                    }

                    GrindRail.Line line = rail.Lines[i];
                    float t = Vector3.Dot(position - line.A, line.Dir);

                    if (t < EndMargin || t > line.Length - EndMargin)
                    {
                        continue;
                    }

                    Vector3 offset = position - (line.PointAt(t) + (Vector3.up * board.RideHeight));
                    Vector3 flatLine = Vector3.ProjectOnPlane(line.Dir, Vector3.up);
                    float travelAngle = flatVelocity.sqrMagnitude < 0.01f || flatLine.sqrMagnitude < 0.0001f
                        ? 90f
                        : Vector3.Angle(flatVelocity, flatLine);

                    float along = Vector3.Dot(velocity, line.Dir);

                    var probe = new GrindLockProbe
                    {
                        Airborne = !board.Grounded,
                        VerticalSpeed = velocity.y,
                        Lateral = Vector3.ProjectOnPlane(offset, Vector3.up).magnitude,
                        Vertical = offset.y,
                        TravelAngle = Mathf.Min(travelAngle, 180f - travelAngle),
                        AlongSpeed = Mathf.Abs(along),
                    };

                    if (!GrindRules.CanLock(probe, _limits))
                    {
                        continue;
                    }

                    float score = probe.Lateral + Mathf.Abs(probe.Vertical);

                    if (score < bestScore)
                    {
                        bestScore = score;
                        bestRail = rail;
                        bestLine = i;
                        bestT = t;
                        bestAlong = along;
                    }
                }
            }

            if (bestRail == null)
            {
                return false;
            }

            _rail = bestRail;
            _lineIndex = bestLine;
            _line = bestRail.Lines[bestLine];
            _t = bestT;
            _direction = Mathf.Sign(bestAlong);
            _speedOnRail = Mathf.Abs(bestAlong);
            _grindName = GrindRules.Name(board.Heading, _line.Heading);
            _heading = GrindRules.SnapHeading(board.Heading, _line.Heading);
            _seconds = 0f;
            _knockPending = false;

            // Which side of the line the board came in from — the side a fall drops it off.
            Vector3 right = Vector3.Cross(Vector3.up, _line.Dir);
            float side = Vector3.Dot(position - _line.PointAt(_t), right);
            _approachSide = side >= 0f ? 1f : -1f;

            _grinding = true;
            Hold(board, dt);
            return true;
        }

        /// <summary>
        /// Keep the board on the line: a velocity that lands it exactly where the grind says it
        /// is at the end of this step. Velocity rather than a teleport, so interpolation stays
        /// smooth and nothing in the physics scene sees the board jump.
        /// </summary>
        private void Hold(BoardController board, float dt)
        {
            Rigidbody rb = board.Body;
            Vector3 next = _line.PointAt(_t + (_direction * _speedOnRail * dt)) + (Vector3.up * board.RideHeight);
            rb.linearVelocity = (next - rb.position) / Mathf.Max(0.0001f, dt);
            board.SetHeading(_heading);
        }

        /// <summary>
        /// The grind is over. Settles what it was worth and hands the board back. Returns whether
        /// this step is still the grind's — true only for a pop, which needs one more grounded tick.
        /// </summary>
        private bool End(GrindExit exit, BoardController board)
        {
            GrindOutcome outcome = GrindRules.Resolve(exit, IsGraze(_seconds));
            string name = _grindName;
            float seconds = _seconds;

            // Anything that is not a clean exit drops the rider beside the rail rather than onto
            // it — including a bail from elsewhere, or the solver would balance them on the bar.
            bool clean = exit == GrindExit.Popped || exit == GrindExit.RodeOffEnd;
            Vector3 side = clean
                ? Vector3.zero
                : Vector3.Cross(Vector3.up, _line.Dir) * (_approachSide * FallOffSideSpeed);

            Release(side);

            string result = outcome switch
            {
                GrindOutcome.Credit => Credit(name, seconds),
                GrindOutcome.Fall => Tricks != null && Tricks.Bail() ? "FELL — bail" : "fell (bail off)",
                _ => exit == GrindExit.Interrupted ? "interrupted" : "graze",
            };

            _lastExit = $"{name}  {seconds:0.00}s  {ExitWord(exit)}  {result}";
            return exit == GrindExit.Popped;
        }

        private string Credit(string name, float seconds)
        {
            if (Bank == null || !Bank.isActiveAndEnabled)
            {
                return "(no bank)";
            }

            BankCredit credit = Bank.CreditGrind(name, seconds, RateFor(name));
            return credit.Counted ? $"+{credit.Added:0.00}" : "no credit";
        }

        /// <summary>Unlock and leave the board travelling along the rail at grind speed.</summary>
        private void Release(Vector3 extra)
        {
            if (_board != null && _board.Body != null)
            {
                _board.Body.linearVelocity = (_line.Dir * (_direction * _speedOnRail)) + extra;
            }

            Unlock();
        }

        /// <summary>A kickoff (#24): off the rail with no credit and no fall. The kickoff moves the board.</summary>
        public void CancelForKickoff()
        {
            if (_grinding)
            {
                _lastExit = $"{_grindName}  {_seconds:0.00}s  cancelled (kickoff)";
                Unlock();
            }

            _lastRail = null;
            _airSinceExit = 999f;
        }

        /// <summary>Drop the grind without touching the board at all.</summary>
        private void Abandon()
        {
            _lastExit = $"{_grindName}  {_seconds:0.00}s  lost track (respawn)";
            Unlock();
        }

        private void Unlock()
        {
            _lastRail = _rail;
            _lastLineIndex = _lineIndex;
            _airSinceExit = 0f;
            _grinding = false;
            _knockPending = false;
            _rail = null;
        }

        /// <summary>Stick left or right at the pop throws you to that side of your travel.</summary>
        private void AddSideKick(Rigidbody rb, float steer)
        {
            if (Mathf.Abs(steer) < SideStickDeadzone)
            {
                return;
            }

            Vector3 travel = _line.Dir * _direction;
            Vector3 right = Vector3.Cross(Vector3.up, travel).normalized;
            rb.linearVelocity += right * (Mathf.Sign(steer) * PopOutSideSpeed);
        }

        private bool IsGraze(float seconds) => Bank != null && Bank.isActiveAndEnabled
            ? Bank.IsGraze(seconds)
            : seconds <= BankRules.DefaultGrindMinSeconds;

        private float RateFor(string name)
        {
            if (Grinds == null)
            {
                return 0f;
            }

            foreach (GrindSettings row in Grinds)
            {
                if (row != null && row.Name == name)
                {
                    return row.RatePerSecond;
                }
            }

            return 0f;
        }

        private static string ExitWord(GrindExit exit) => exit switch
        {
            GrindExit.Popped => "popped",
            GrindExit.RodeOffEnd => "rode off",
            GrindExit.Stalled => "stalled",
            GrindExit.KnockedOff => "knocked off",
            _ => "ended",
        };
    }
}
