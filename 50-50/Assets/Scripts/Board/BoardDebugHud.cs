using FiftyFifty.Scoring;
using UnityEngine;
using UnityEngine.InputSystem;

namespace FiftyFifty.Board
{
    /// <summary>
    /// Minimal on-screen readout while tuning movement: speed, grounded state, airtime, and
    /// the controls. IMGUI so it needs no canvas, no prefabs and no wiring — delete the
    /// component when the real HUD arrives.
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

        [Tooltip("Key that puts the board back at its spawn point.")]
        public Key RespawnKey = Key.R;

        private GUIStyle _big;
        private GUIStyle _small;

        private void Update()
        {
            Keyboard keys = Keyboard.current;
            if (Board != null && keys != null && keys[RespawnKey].wasPressedThisFrame)
            {
                Board.Respawn();
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

            GUILayout.BeginArea(new Rect(18, 18, 560, 420));

            if (Bank != null && Bank.isActiveAndEnabled)
            {
                // Bank first and biggest: it is the number the whole game is about, and the
                // player has to be able to read it at speed (#21).
                GUILayout.Label($"BANK x{Bank.Bank:0.00}     SCORE {Bank.Score:0.00}", _big);
                GUILayout.Label(
                    Bank.SecondsSinceEvent < BankEventSeconds ? Bank.LastEvent : " ",
                    _small);
                GUILayout.Space(6);
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

            GUILayout.Space(10);
            GUILayout.Label(
                "pad:  LS steer / air spin   RS camera   RT accelerate   LT brake   A ollie\n" +
                "keys: A,D steer / air spin   W accelerate   S brake   arrows camera   SPACE ollie\n" +
                "L3 / SHIFT powerslide   (brake held at rest reverses)\n" +
                "tricks: hold A (SPACE) + B/X/Y  —  keys J / K / L\n" +
                "RB / E grab (tap on, tap off)   LB / F punch   R3 / C reverse (ground)\n" +
                "R respawn",
                _small);

            GUILayout.EndArea();
        }
    }
}
