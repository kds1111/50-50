using FiftyFifty.Board;
using FiftyFifty.Board.Tricks;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FiftyFifty.EditorTools
{
    /// <summary>
    /// The scene for #6. Three ramps of clearly different steepness, and nothing else.
    ///
    /// Three rather than one because #6 rule 4 fixed trick durations in SECONDS, and #6 rule 26
    /// then refused the player a readout telling them whether a trick will fit. That judgement
    /// therefore has to be learnable by feel, which only works if the arena offers a small
    /// number of ramps that each give a memorable, repeatable amount of air. This scene is the
    /// smallest version of that, and it is also the rig for measuring what those amounts are —
    /// the durations shipped on BoardTrickController are placeholders and cannot be chosen
    /// honestly until they are measured here and then against #9's real arena.
    ///
    /// BoardTest stays ball-free and trick-free as the clean movement reference; BallTest stays
    /// the ball scene. Neither gains trick code, which is half of why the trick system can be
    /// backed out at any time.
    ///
    /// Menu: 50-50 > Rebuild Trick Test Scene
    /// </summary>
    public static class TrickTestSceneBuilder
    {
        private const string ScenePath = "Assets/Scenes/TrickTest.unity";

        [MenuItem("50-50/Rebuild Trick Test Scene")]
        public static void Build()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            Selection.objects = new Object[0];

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            GreyboxParts.CreateLighting();
            GreyboxParts.CreateGround(90f);

            // Small, medium, big. Spread apart so each can be hit at speed without the previous
            // one feeding into it, and labelled so a measured airtime can be written down
            // against a ramp rather than against "the one on the left".
            GreyboxParts.CreateKicker(new Vector3(-22f, 0f, 20f), 0f, 9f, "Kicker S");
            GreyboxParts.CreateKicker(new Vector3(0f, 0f, 24f), 0f, 17f, "Kicker M");
            GreyboxParts.CreateKicker(new Vector3(22f, 0f, 28f), 0f, 26f, "Kicker L");

            // Landing-side safe points, so a bail puts you back near where you were rather than
            // at spawn. #9 owes the real ones.
            GreyboxParts.CreateSafePoint(new Vector3(-22f, 0.4f, 34f));
            GreyboxParts.CreateSafePoint(new Vector3(0f, 0.4f, 38f));
            GreyboxParts.CreateSafePoint(new Vector3(22f, 0.4f, 42f));
            GreyboxParts.CreateSafePoint(new Vector3(0f, 0.4f, 0f));

            GameObject board = GreyboxParts.CreateBoard(new Vector3(0f, 0.4f, -6f));
            var tricks = board.AddComponent<BoardTrickController>();

            GreyboxParts.CreateCamera(board);
            CreateHud(board.GetComponent<BoardController>(), tricks);

            // The bank (#20), and goal sides by the -Z-is-side-A convention. Same code as the
            // menu item, so a rebuilt scene and a wired one match.
            ScoringWiring.WireOpenScene();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            Debug.Log($"[50-50] Trick test scene written to {ScenePath}");
        }

        private static void CreateHud(BoardController board, BoardTrickController tricks)
        {
            var go = new GameObject("Debug HUD");
            var hud = go.AddComponent<BoardDebugHud>();
            hud.Board = board;
            hud.Tricks = tricks;
        }
    }
}
