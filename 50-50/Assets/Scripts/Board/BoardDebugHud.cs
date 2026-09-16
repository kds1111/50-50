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

            GUILayout.BeginArea(new Rect(18, 18, 520, 260));

            GUILayout.Label($"{Board.Speed:0.0} m/s", _big);
            GUILayout.Label(Board.Grounded ? "grounded" : $"AIR  {Board.TimeInAir:0.00}s", _small);

            GUILayout.Space(10);
            GUILayout.Label(
                "pad:  LS steer / air attitude   RS camera   RT accelerate   LT brake   A ollie\n" +
                "keys: A,D steer (W,S air pitch)   arrows camera   W accelerate   S brake   SPACE ollie\n" +
                "R respawn",
                _small);

            GUILayout.EndArea();
        }
    }
}
