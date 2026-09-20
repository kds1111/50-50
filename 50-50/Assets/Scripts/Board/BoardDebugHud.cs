using FiftyFifty.Scoring;
using UnityEngine;
using UnityEngine.InputSystem;

namespace FiftyFifty.Board
{
    /// <summary>
    /// Minimal on-screen readout while tuning movement: speed, grounded state, airtime, and
    /// the controls. IMGUI so it needs no canvas, no prefabs and no wiring — delete the
    /// component when the real HUD arrives.
    ///
    /// Two layers (#24). The bank, the score and the opponent's line are the game and always
    /// show. Everything else is a window into the simulation, and H hides it — every readout on
    /// screen at once, so one key works for both halves of a split screen.
    /// </summary>
    public class BoardDebugHud : MonoBehaviour
    {
        public BoardController Board;

        [Tooltip("Optional. When present the HUD reports tricks, spins and bails (#6).")]
        public FiftyFifty.Board.Tricks.BoardTrickController Tricks;

        [Tooltip("Optional. Found on the board if left empty. When present the HUD shows the bank " +
                 "and score (#20) — the stand-in until #21 builds the real readout.")]
        public PlayerBank Bank;

        [Tooltip("Optional. Found on the board if left empty. When present the HUD shows the " +
                 "grind in progress (#12).")]
        public FiftyFifty.Board.Grinds.BoardGrindController Grinds;

        [Tooltip("Seconds the last bank event (credit, loss, goal) stays on screen.")]
        public float BankEventSeconds = 3f;

        [Tooltip("Key that puts the board back at its spawn point. None for no key — player two's " +
                 "readout in split-screen, so R does not respawn both.")]
        public Key RespawnKey = Key.R;

        [Tooltip("Shows and hides the debug part of every readout on screen. Bank, score and the " +
                 "opponent's line always stay.")]
        public Key ToggleKey = Key.H;

        [Tooltip("The part of the screen this readout lives in, 0 to 1, from the top left. Set by " +
                 "the match for split-screen (#24); the whole screen otherwise.")]
        public Rect ScreenRegion = new(0f, 0f, 1f, 1f);

        [Tooltip("The controls cheat-sheet. Off in split-screen, where it would fill half the screen.")]
        public bool ShowControls = true;

        /// <summary>Shared by every readout, so H flips both halves together.</summary>
        public static bool ShowDebug = true;

        private static int _lastToggleFrame = -1;

        private GUIStyle _big;
        private GUIStyle _small;

        private void Update()
        {
            Keyboard keys = Keyboard.current;

            if (keys == null)
            {
                return;
            }

            if (Board != null && RespawnKey != Key.None && keys[RespawnKey].wasPressedThisFrame)
            {
                Board.Respawn();
            }

            // Every readout sees the same key press; only the first one this frame flips it.
            if (ToggleKey != Key.None && keys[ToggleKey].wasPressedThisFrame && _lastToggleFrame != Time.frameCount)
            {
                ShowDebug = !ShowDebug;
                _lastToggleFrame = Time.frameCount;
            }
        }

        private void OnGUI()
        {
            if (Board == null)
            {
                return;
            }

            _big ??= new GUIStyle(GUI.skin.label) { fontSize = 26, fontStyle = FontStyle.Bold };
            _small ??= new GUIStyle(GUI.skin.label) { fontSize = 14 };

            if (Bank == null)
            {
                Bank = Board.GetComponent<PlayerBank>();
            }

            if (Grinds == null)
            {
                Grinds = Board.GetComponent<FiftyFifty.Board.Grinds.BoardGrindController>();
            }

            var region = new Rect(
                ScreenRegion.x * Screen.width,
                ScreenRegion.y * Screen.height,
                ScreenRegion.width * Screen.width,
                ScreenRegion.height * Screen.height);

            GUILayout.BeginArea(new Rect(
                region.x + 18f,
                region.y + 18f,
                Mathf.Min(560f, region.width - 36f),
                Mathf.Max(0f, region.height - 36f)));

            if (Bank != null && Bank.isActiveAndEnabled)
            {
                // Bank first and biggest: it is the number the whole game is about, and the
                // player has to be able to read it at speed (#21).
                GUILayout.Label($"BANK x{Bank.Bank:0.00}     SCORE {Bank.Score:0.00}", _big);
                GUILayout.Label(
                    Bank.SecondsSinceEvent < BankEventSeconds ? Bank.LastEvent : " ",
                    _small);

                // The opponent can see your multiplier (#8), and you can see theirs: it is what
                // makes them choose between hunting you and playing the ball.
                PlayerBank opponent = OpponentOf(Bank);

                if (opponent != null)
                {
                    GUILayout.Label($"OPP  x{opponent.Bank:0.00}     {opponent.Score:0.00}", _small);
                }

                GUILayout.Space(6);
            }

            if (!ShowDebug)
            {
                GUILayout.EndArea();
                return;
            }

            if (Grinds != null && Grinds.Grinding)
            {
                // What is riding on the rail right now. Under the minimum it is a graze, and a
                // graze is neither paid nor punished.
                string stake = Grinds.InGraze ? "graze" : $"+{Grinds.PendingPay:0.00} if you pop now";
                GUILayout.Label($"{Grinds.CurrentGrind}   {Grinds.GrindSeconds:0.00}s   {stake}", _big);
            }

            GUILayout.Label($"{Board.Speed:0.0} m/s", _big);
            GUILayout.Label(Board.Grounded ? "grounded" : $"AIR  {Board.TimeInAir:0.00}s", _small);

            if (Board.Powersliding)
            {
                GUILayout.Label($"POWERSLIDE   grip {Board.Grip:0.00}   slip {Board.SlipAngle:0}°", _small);
            }
            else if (Board.Sliding)
            {
                GUILayout.Label($"sliding   grip {Board.Grip:0.00}   slip {Board.SlipAngle:0}°", _small);
            }

            if (Tricks != null && Tricks.TricksEnabled)
            {
                GUILayout.Space(8);

                if (Tricks.KnockedDown)
                {
                    GUILayout.Label("BAILED — down", _big);
                }
                else if (Tricks.RunningTrick != null)
                {
                    // The progress bar is the point of the readout while tuning: it shows how
                    // much of the trick is left against how much air is left, which is the
                    // judgement #6 asks the player to learn without a readout in the real game.
                    GUILayout.Label(
                        $"{Tricks.RunningTrick.Name}   {Tricks.TrickProgress * 100f:0}%   " +
                        $"(vulnerable)",
                        _small);
                }

                GUILayout.Label(
                    $"last: {Tricks.LastTrick}   spin {Tricks.LastSpin}   {Tricks.LastOutcome}",
                    _small);
            }

            if (Grinds != null && Grinds.GrindsEnabled && Grinds.LastExit != "-")
            {
                GUILayout.Label($"last grind: {Grinds.LastExit}", _small);
            }

            if (ShowControls)
            {
                GUILayout.Space(10);
                GUILayout.Label(
                    "pad:  LS steer / air spin   RS camera   RT accelerate   LT brake   A ollie\n" +
                    "keys: A,D steer / air spin   W accelerate   S brake   arrows camera   SPACE ollie\n" +
                    "L3 / SHIFT powerslide   (brake held at rest reverses)\n" +
                    "tricks: hold A (SPACE) + B/X/Y  —  keys J / K / L\n" +
                    "RB / E grab (tap on, tap off)   LB / F punch   R3 / C reverse (ground)\n" +
                    "R respawn   H hide this readout",
                    _small);
            }

            GUILayout.EndArea();
        }

        private static PlayerBank OpponentOf(PlayerBank mine)
        {
            foreach (PlayerBank other in PlayerBank.All)
            {
                if (other != mine && other.Side != mine.Side)
                {
                    return other;
                }
            }

            return null;
        }
    }
}
