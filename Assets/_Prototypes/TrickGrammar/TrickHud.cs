using UnityEngine;

namespace Prototypes.TrickGrammar
{
    /// <summary>
    /// PROTOTYPE. The EA Flick-It prototype was a text readout before it was a game —
    /// stick motions in, trick name and ratings out. This is that, for our classifier.
    ///
    /// IMGUI on purpose: zero scene setup, zero prefabs, deletable in one line.
    /// The raw accumulator values matter as much as the name — they are how you tell
    /// whether the board lied or the classifier did.
    /// </summary>
    public class TrickHud : MonoBehaviour
    {
        public BoardController Board;

        private GUIStyle _big;
        private GUIStyle _small;

        private void OnGUI()
        {
            if (Board == null)
            {
                return;
            }

            _big ??= new GUIStyle(GUI.skin.label) { fontSize = 30, fontStyle = FontStyle.Bold };
            _small ??= new GUIStyle(GUI.skin.label) { fontSize = 15 };

            GUILayout.BeginArea(new Rect(20, 20, 620, 420));

            GUILayout.Label($"SCHEME: {Board.Scheme}", _big);
            GUILayout.Label(
                Board.Scheme == TrickScheme.Consequence
                    ? "Rotation is whatever physics gave you. Lean at pop is your only influence."
                    : "Right stick at pop = flip (X) / shuvit (Y). Left stick in air = body spin.",
                _small);

            GUILayout.Space(12);

            GUILayout.Label($"LAST: {Board.LastTrick}", _big);
            GUILayout.Label($"grade: {Board.LastGrade}", _small);
            GUILayout.Label(Board.LastRotation.ToString(), _small);

            GUILayout.Space(12);

            GUILayout.Label(Board.Grounded ? "grounded" : "AIRBORNE", _small);
            GUILayout.Label($"live: {Board.Live}", _small);
            GUILayout.Label($"speed {Board.Speed:0.0} m/s   pop charge {Board.PopCharge:0.00}", _small);

            GUILayout.Space(12);

            GUILayout.Label($"no-bail: {(Board.NoBail ? "ON (scoring rules off)" : "off")}", _small);
            GUILayout.Label(
                "1 / 2 switch scheme   B toggle no-bail   R respawn\n" +
                "pad: LS steer, RT/LT push/brake, A hold+release to ollie, RS trick, LS spin\n" +
                "keys: A/D steer, W/S push, SPACE ollie, arrows trick, Q/E spin",
                _small);

            GUILayout.EndArea();
        }
    }
}
