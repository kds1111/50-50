using FiftyFifty.Board;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FiftyFifty.EditorTools
{
    /// <summary>
    /// Builds the movement test scene for issue #16: a flat plane and one kicker, nothing else.
    /// No ball, on purpose — when the board feels wrong, you need a scene with nothing else in
    /// it to confirm that. The ball lives in BallTest (#7).
    ///
    /// Generated from code so the scene can be rebuilt after the board changes shape, and so
    /// what is being tested is readable here rather than buried in a .unity file. Anything you
    /// move by hand in the scene stays moved — rebuilding replaces the scene, so only rebuild
    /// when you want the original layout back.
    ///
    /// Menu: 50-50 > Rebuild Board Test Scene
    /// </summary>
    public static class BoardTestSceneBuilder
    {
        private const string ScenePath = "Assets/Scenes/BoardTest.unity";

        [MenuItem("50-50/Rebuild Board Test Scene")]
        public static void Build()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            GreyboxParts.CreateLighting();
            GreyboxParts.CreateGround();
            GreyboxParts.CreateKicker(new Vector3(0f, 0f, 22f));

            GameObject board = GreyboxParts.CreateBoard(new Vector3(0f, 0.4f, 0f));
            GreyboxParts.CreateCamera(board);
            CreateHud(board.GetComponent<BoardController>());

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            Debug.Log($"[50-50] Board test scene written to {ScenePath}");
        }

        private static void CreateHud(BoardController board)
        {
            var go = new GameObject("Debug HUD");
            var hud = go.AddComponent<BoardDebugHud>();
            hud.Board = board;
        }
    }
}
