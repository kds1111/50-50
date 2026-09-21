using System.Collections.Generic;
using FiftyFifty.Ball;
using FiftyFifty.Board;
using FiftyFifty.Board.Grinds;
using FiftyFifty.Board.Tricks;
using FiftyFifty.CameraRig;
using FiftyFifty.Scoring;
using UnityEngine;
using UnityEngine.InputSystem;

namespace FiftyFifty.Match
{
    public enum MatchMode
    {
        /// <summary>You alone, full screen. Kickoffs and the timer still work if switched on.</summary>
        FreeSkate,

        /// <summary>Two players, split-screen, one machine (#24).</summary>
        MatchPlay,
    }

    /// <summary>
    /// Runs a match in the arena (#24): who is playing, the split screen, kickoffs and the clock.
    ///
    /// The scene is authored for ONE player. In Match Play this component clones that player's
    /// board at Play time for player two — every tuned number comes along — gives the clone side
    /// B, its own pad, camera and readout, and splits the screen. Nothing about player two is
    /// saved in the scene, which is what lets a mode menu pick Free Skate or Match Play later by
    /// setting one field.
    ///
    /// Kickoffs happen on load and after every goal: a short pause so the goal lands, everyone
    /// back to their KickoffSpot with anything in progress cancelled, a countdown with the boards
    /// held still, then go. Banks are untouched — the goal already settled both.
    ///
    /// The timing rules live in MatchFlow (plain C#, tested). This class only does what it says:
    /// on the fixed tick, and before the boards step, so a freeze lands on the same tick.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public class MatchDirector : MonoBehaviour
    {
        [Header("Mode")]
        [Tooltip("Free Skate: you alone, full screen. Match Play: split-screen 1v1 on one machine.")]
        public MatchMode Mode = MatchMode.MatchPlay;

        [Header("Players")]
        [Tooltip("Player one's board. Found in the scene if empty. In Match Play it is cloned for " +
                 "player two, tuning and all.")]
        public BoardController PlayerOne;

        [Tooltip("Tint each player's rider by side so the two identical boards can be told apart.")]
        public bool TintRidersBySide = true;

        public Color SideAColor = new(0.3f, 0.55f, 1f);
        public Color SideBColor = new(1f, 0.45f, 0.25f);

        [Header("Kickoff")]
        [Tooltip("Off: no resets and no countdown, ever. Goals still score.")]
        public bool KickoffEnabled = true;

        [Tooltip("Seconds play carries on after a goal before everyone is reset, so the goal lands.")]
        public float GoalPauseSeconds = 1.5f;

        [Tooltip("Seconds everyone is held on their spot before \"go\".")]
        public float CountdownSeconds = 2f;

        [Header("Match timer")]
        [Tooltip("Unpolished, for playing with friends. Starts with the match; stops during kickoffs.")]
        public bool TimerEnabled;

        [Tooltip("Match length. Read when a match starts.")]
        public float MatchMinutes = 5f;

        [Tooltip("Plays again once the clock has run out. Start on either pad does the same.")]
        public Key RestartKey = Key.Enter;

        [Header("Split screen")]
        [Tooltip("Top and bottom. Off puts the players side by side. Live: flip it while " +
                 "playing and both views and both readouts move with it (#30).")]
        public bool SplitTopBottom = true;

        [Header("Debug (read-only)")]
        [SerializeField] private MatchPhase _phase;
        [SerializeField] private float _clock;

        private sealed class Player
        {
            public BoardController Board;
            public BoardTrickController Tricks;
            public BoardGrindController Grinds;
            public BallHandler Handler;
            public PlayerBoardInput Input;
            public PlayerBank Bank;
            public Side Side;
            public BoardChaseCamera Camera;

            public BoardDebugHud Hud;

            /// <summary>This player's part of the screen, normalised, y down (GUI space).</summary>
            public Rect Region = new(0f, 0f, 1f, 1f);
        }

        private readonly MatchFlow _flow = new();
        private readonly List<Player> _players = new();
        // Poses, not Transforms (#26). Resetting happens inside the physics step, where
        // reading a transform gives the RENDERED pose rather than the simulated one. The spots
        // never move, so they are read once at startup — the one place reading transforms is fine.
        private readonly Dictionary<KickoffSpotKind, (Vector3 Position, float Yaw)> _spots = new();
        private BallController _ball;
        private Vector3 _playerOneStart;
        private float _playerOneStartYaw;
        private bool _restartRequested;
        private float _goShownUntil = -1f;
        private string _result;
        // Nullable on purpose: false would mean "already laid out side by side", which is
        // exactly the case that must still be applied on the first pass.
        private bool? _splitApplied;
        private GUIStyle _huge;
        private GUIStyle _mid;

        public MatchPhase Phase => _flow.Phase;

        /// <summary>
        /// Whether a goal counts right now (#29): the flow's own answer, and nothing else.
        ///
        /// This used to short-circuit on <see cref="KickoffEnabled"/>, meaning to leave scenes
        /// without kickoffs behaving as they always had. It punched a hole in the gate instead:
        /// the flow runs either way, the clock still reaches Over, and the left side answered
        /// true anyway — so with kickoffs off and the timer on, a ball crossing the line after
        /// full time settled banks the results screen had already been written from. #29's own
        /// third symptom, let back in by the guard meant to preserve the old behaviour.
        ///
        /// No guard is needed: with kickoffs off the countdown is zero-length, so the flow is
        /// Playing from the first call and stays there until a timer ends it.
        /// </summary>
        public bool ScoringOpen => _flow.ScoringOpen;

        private void OnEnable() => GoalTargetVolume.Scored += OnGoal;

        private void OnDisable()
        {
            GoalTargetVolume.Scored -= OnGoal;

            foreach (Player p in _players)
            {
                if (p.Board != null)
                {
                    p.Board.Frozen = false;
                }
            }

            // The ball is frozen by the same hand as the boards, so it is released by the same
            // one. Leaving it held would outlive the component that held it: a disabled director
            // leaves no trace, and a ball frozen for the rest of the session is the loudest
            // trace there is.
            if (_ball != null)
            {
                _ball.Frozen = false;
            }
        }

        private void Start()
        {
            if (PlayerOne == null)
            {
                PlayerOne = FindFirstObjectByType<BoardController>();
            }

            if (PlayerOne == null)
            {
                Debug.LogWarning("[50-50] MatchDirector: no board in the scene.");
                enabled = false;
                return;
            }

            _ball = FindFirstObjectByType<BallController>();

            foreach (KickoffSpot spot in FindObjectsByType<KickoffSpot>(FindObjectsSortMode.None))
            {
                Transform t = spot.transform;
                _spots[spot.Kind] = (t.position, t.eulerAngles.y);
            }

            // Scenery and a board that has not moved yet: the one place reading transforms is fine.
            _playerOneStart = PlayerOne.transform.position;
            _playerOneStartYaw = PlayerOne.transform.eulerAngles.y;

            Player one = Describe(PlayerOne.gameObject);
            one.Camera = FindCameraFor(PlayerOne.transform);
            _players.Add(one);

            if (Mode == MatchMode.MatchPlay)
            {
                SetUpMatchPlay(one);
            }

            SyncSettings();
            _flow.MatchSeconds = Mathf.Max(0f, MatchMinutes) * 60f;
            Handle(_flow.Begin());
        }

        private void Update()
        {
            if (_flow.Phase != MatchPhase.Over)
            {
                return;
            }

            // Read here, acted on in the tick: input is per frame, the match changes per tick.
            Keyboard keys = Keyboard.current;
            bool pressed = keys != null && RestartKey != Key.None && keys[RestartKey].wasPressedThisFrame;

            foreach (Gamepad pad in Gamepad.all)
            {
                pressed |= pad.startButton.wasPressedThisFrame;
            }

            _restartRequested |= pressed;
        }

        private void FixedUpdate()
        {
            SyncSettings();

            if (_restartRequested && _flow.Phase == MatchPhase.Over)
            {
                _restartRequested = false;

                foreach (PlayerBank bank in PlayerBank.All)
                {
                    bank.ResetForNewMatch();
                }

                _flow.MatchSeconds = Mathf.Max(0f, MatchMinutes) * 60f;
                _result = null;
                Handle(_flow.Begin());
            }

            Handle(_flow.Tick(Time.fixedDeltaTime));

            foreach (Player p in _players)
            {
                p.Board.Frozen = _flow.Frozen;
            }

            // #29: the ball is held for the same reasons the boards are. Without this it rolls
            // on through the final whistle and reaches a goal after the match has ended.
            if (_ball != null)
            {
                _ball.Frozen = _flow.Frozen;
            }

            _phase = _flow.Phase;
            _clock = _flow.ClockRemaining;
        }

        /// <summary>Live-tunable from the Inspector, like everything else.</summary>
        private void SyncSettings()
        {
            _flow.CountdownSeconds = KickoffEnabled ? CountdownSeconds : 0f;
            _flow.GoalPauseSeconds = GoalPauseSeconds;
            _flow.TimerEnabled = TimerEnabled;

            // #30: the split orientation used to be read once at setup, so flipping it while
            // playing did nothing — the one setting that was not live.
            if (Mode == MatchMode.MatchPlay && _splitApplied != SplitTopBottom)
            {
                ApplySplit();
            }
        }

        /// <summary>
        /// Lays out the two halves: top and bottom, or side by side. Called at setup and again
        /// whenever the toggle changes, so the orientation can be judged by flipping it mid-play
        /// rather than by restarting.
        /// </summary>
        private void ApplySplit()
        {
            if (_players.Count < 2)
            {
                return;
            }

            Player one = _players[0];
            Player two = _players[1];

            one.Region = SplitTopBottom ? new Rect(0f, 0f, 1f, 0.5f) : new Rect(0f, 0f, 0.5f, 1f);
            two.Region = SplitTopBottom ? new Rect(0f, 0.5f, 1f, 0.5f) : new Rect(0.5f, 0f, 0.5f, 1f);

            ApplyViewport(one);
            ApplyViewport(two);

            if (one.Hud != null)
            {
                one.Hud.ScreenRegion = one.Region;
            }

            if (two.Hud != null)
            {
                two.Hud.ScreenRegion = two.Region;
            }

            _splitApplied = SplitTopBottom;
        }

        private void OnGoal(GoalTargetVolume goal)
        {
            if (KickoffEnabled)
            {
                _flow.Goal();
            }
        }

        private void Handle(MatchSignal signal)
        {
            if ((signal & MatchSignal.Reset) != 0 && KickoffEnabled)
            {
                ResetEveryone();
            }

            if ((signal & MatchSignal.Go) != 0)
            {
                // Presentation time on purpose (#26): this deadline is read only by OnGUI and
                // feeds nothing simulated, so it may use the clock the display runs on.
                _goShownUntil = Time.time + 0.7f;
            }

            if ((signal & MatchSignal.Ended) != 0)
            {
                // Settled here, not in OnGUI: recomputing it every GUI pass let the winner
                // change after full time, which is #29's third symptom.
                _result = ResultLine();
                Debug.Log($"[50-50] Full time. {_result}");
            }
        }

        /// <summary>
        /// Everyone back to their spot with anything in progress cancelled — no bail, no credit,
        /// no fall. Banks are left alone: the goal already cemented one and forfeited the other.
        /// </summary>
        private void ResetEveryone()
        {
            foreach (Player p in _players)
            {
                p.Tricks?.CancelForKickoff();
                p.Grinds?.CancelForKickoff();
                p.Handler?.ResetPossession();
                p.Input?.ClearGrabToggle();

                (Vector3 position, float yaw) = SpotFor(p.Side);
                p.Board.RespawnAt(position, yaw);
                p.Camera?.SnapToTarget();
            }

            if (_ball == null)
            {
                return;
            }

            if (_spots.TryGetValue(KickoffSpotKind.Ball, out (Vector3 Position, float Yaw) ballSpot))
            {
                _ball.ResetTo(ballSpot.Position);
            }
            else
            {
                _ball.ResetToSpawn();
            }
        }

        /// <summary>
        /// A side's kickoff spot. Without a marker: player one's own starting place for their
        /// side, and its mirror through the centre for the other — the arena is rotationally
        /// symmetric (#9), so the mirror is always a fair spot.
        /// </summary>
        private (Vector3, float) SpotFor(Side side)
        {
            KickoffSpotKind kind = side == Side.A ? KickoffSpotKind.SideA : KickoffSpotKind.SideB;

            if (_spots.TryGetValue(kind, out (Vector3 Position, float Yaw) spot))
            {
                return (spot.Position, spot.Yaw);
            }

            if (side == _players[0].Side)
            {
                return (_playerOneStart, _playerOneStartYaw);
            }

            return (new Vector3(-_playerOneStart.x, _playerOneStart.y, -_playerOneStart.z), _playerOneStartYaw + 180f);
        }

        private void SetUpMatchPlay(Player one)
        {
            Side twoSide = GoalRule.Opponent(one.Side);
            (Vector3 position, float yaw) = SpotFor(twoSide);

            GameObject clone = Instantiate(PlayerOne.gameObject, position, Quaternion.Euler(0f, yaw, 0f));
            clone.name = "Board P2";

            Player two = Describe(clone);
            two.Side = twoSide;

            if (two.Bank != null)
            {
                two.Bank.Side = twoSide;
            }

            DealDevices(one, 0);
            DealDevices(two, 1);

            two.Camera = CloneCamera(one.Camera, clone);

            BoardDebugHud hud = FindHudFor(PlayerOne);

            if (hud != null)
            {
                one.Hud = hud;
                hud.ShowControls = false;

                BoardDebugHud twoHud = Instantiate(hud.gameObject).GetComponent<BoardDebugHud>();
                twoHud.name = "Debug HUD P2";
                twoHud.Board = two.Board;
                twoHud.Tricks = two.Tricks;
                twoHud.Bank = null;
                twoHud.Grinds = null;
                twoHud.ShowControls = false;
                two.Hud = twoHud;

                // R respawns player one only; one key cannot sensibly mean both.
                twoHud.RespawnKey = Key.None;
            }

            if (TintRidersBySide)
            {
                Tint(one);
                Tint(two);
            }

            _players.Add(two);

            // After the second player joins the list, not before: ApplySplit needs both halves,
            // and called any earlier it silently does nothing.
            ApplySplit();
        }

        private static Player Describe(GameObject board)
        {
            var player = new Player
            {
                Board = board.GetComponent<BoardController>(),
                Tricks = board.GetComponent<BoardTrickController>(),
                Grinds = board.GetComponent<BoardGrindController>(),
                Handler = board.GetComponent<BallHandler>(),
                Input = board.GetComponent<PlayerBoardInput>(),
                Bank = board.GetComponent<PlayerBank>(),
            };

            player.Side = player.Bank != null ? player.Bank.Side : Side.A;
            return player;
        }

        /// <summary>Pads in connection order; the keyboard stays with player one (#24).</summary>
        private static void DealDevices(Player player, int index)
        {
            if (player.Input == null)
            {
                return;
            }

            player.Input.PlayerIndex = index;
            player.Input.PlayersOnThisMachine = 2;
            player.Input.UseKeyboard = index == 0;
        }

        private static BoardChaseCamera CloneCamera(BoardChaseCamera original, GameObject target)
        {
            if (original == null)
            {
                return null;
            }

            BoardChaseCamera copy = Instantiate(original);
            copy.name = "Chase Camera P2";
            copy.Target = target.transform;
            copy.InputSource = target.GetComponent<BoardInputSource>();
            copy.Board = target.GetComponent<BoardController>();

            // One listener per scene. Player one keeps it.
            // Disabled at once: Destroy waits for the end of the frame, and two live listeners for
            // even one frame is a warning in the console.
            if (copy.TryGetComponent(out AudioListener listener))
            {
                listener.enabled = false;
                Destroy(listener);
            }

            copy.SnapToTarget();
            return copy;
        }

        /// <summary>GUI space is y-down; a camera's viewport is y-up.</summary>
        private static void ApplyViewport(Player player)
        {
            if (player.Camera == null || !player.Camera.TryGetComponent(out UnityEngine.Camera cam))
            {
                return;
            }

            Rect r = player.Region;
            cam.rect = new Rect(r.x, 1f - r.y - r.height, r.width, r.height);
        }

        private void Tint(Player player)
        {
            Transform rider = player.Board.transform.Find("Rider");

            if (rider != null && rider.TryGetComponent(out Renderer body))
            {
                body.material.color = player.Side == Side.A ? SideAColor : SideBColor;
            }
        }

        private static BoardChaseCamera FindCameraFor(Transform target)
        {
            foreach (BoardChaseCamera cam in FindObjectsByType<BoardChaseCamera>(FindObjectsSortMode.None))
            {
                if (cam.Target == target)
                {
                    return cam;
                }
            }

            return null;
        }

        private static BoardDebugHud FindHudFor(BoardController board)
        {
            foreach (BoardDebugHud hud in FindObjectsByType<BoardDebugHud>(FindObjectsSortMode.None))
            {
                if (hud.Board == board)
                {
                    return hud;
                }
            }

            return null;
        }

        private float ScoreFor(Side side)
        {
            float total = 0f;

            foreach (PlayerBank bank in PlayerBank.All)
            {
                if (bank.Side == side)
                {
                    total += bank.Score;
                }
            }

            return total;
        }

        private string ResultLine()
        {
            if (Mode == MatchMode.FreeSkate || _players.Count < 2)
            {
                return $"TIME  —  score {ScoreFor(_players[0].Side):0.00}";
            }

            float a = ScoreFor(Side.A);
            float b = ScoreFor(Side.B);

            if (Mathf.Abs(a - b) < 0.005f)
            {
                return $"DRAW  {a:0.00} – {b:0.00}";
            }

            return a > b ? $"SIDE A WINS  {a:0.00} – {b:0.00}" : $"SIDE B WINS  {b:0.00} – {a:0.00}";
        }

        /// <summary>
        /// The countdown, the clock and the result. Always drawn — H hides the debug readout,
        /// never these, because they are the game rather than a window into it.
        /// </summary>
        private void OnGUI()
        {
            if (_players.Count == 0)
            {
                return;
            }

            _huge ??= new GUIStyle(GUI.skin.label)
            {
                fontSize = 64, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter,
            };

            _mid ??= new GUIStyle(GUI.skin.label)
            {
                fontSize = 26, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter,
            };

            string big = null;

            if (_flow.Phase == MatchPhase.Countdown && KickoffEnabled)
            {
                big = Mathf.CeilToInt(_flow.PhaseRemaining).ToString();
            }
            else if (_flow.Phase == MatchPhase.Playing && Time.time < _goShownUntil)
            {
                big = "GO";
            }

            if (big != null)
            {
                foreach (Player p in _players)
                {
                    GUI.Label(ToPixels(p.Region), big, _huge);
                }
            }

            if (TimerEnabled)
            {
                int seconds = Mathf.CeilToInt(_flow.ClockRemaining);
                string clock = $"{seconds / 60}:{seconds % 60:00}";

                // On the seam between the halves in a top/bottom split; top centre otherwise.
                float y = Mode == MatchMode.MatchPlay && SplitTopBottom ? (Screen.height * 0.5f) - 20f : 8f;
                GUI.Label(new Rect(0f, y, Screen.width, 40f), clock, _mid);
            }

            if (_flow.Phase == MatchPhase.Over)
            {
                var whole = new Rect(0f, 0f, Screen.width, Screen.height);
                GUI.Label(new Rect(whole.x, whole.center.y - 70f, whole.width, 80f), _result ?? ResultLine(), _huge);
                GUI.Label(new Rect(whole.x, whole.center.y + 20f, whole.width, 40f),
                    "Enter / Start — play again", _mid);
            }
        }

        private static Rect ToPixels(Rect normalised) => new(
            normalised.x * Screen.width,
            normalised.y * Screen.height,
            normalised.width * Screen.width,
            normalised.height * Screen.height);
    }
}
