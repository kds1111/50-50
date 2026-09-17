using FiftyFifty.Ball;
using FiftyFifty.Board;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FiftyFifty.EditorTools
{
    /// <summary>
    /// Builds the ball test scene for issue #7: a walled box with two kickers, a goal mouth to
    /// shoot at, and a dummy defender standing in the middle holding the ball.
    ///
    /// It is not an arena — that is #9. It is the smallest space in which you can answer four
    /// questions: does the ball read at speed, does a grab feel earned, does a punch feel like a
    /// shot, and is being hit mid-trick fair.
    ///
    /// The dummy is the reason stripping is testable at all with one player: it grabs any loose
    /// ball that comes near it and never lets go, so "go and take it off him" is a drill you can
    /// repeat. Press T to put the ball back.
    ///
    /// Menu: 50-50 > Rebuild Ball Test Scene
    /// </summary>
    public static class BallTestSceneBuilder
    {
        private const string ScenePath = "Assets/Scenes/BallTest.unity";
        private const float ArenaSize = 60f;

        [MenuItem("50-50/Rebuild Ball Test Scene")]
        public static void Build()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            GreyboxParts.CreateLighting();
            GreyboxParts.CreateGround(ArenaSize);
            CreateWalls();

            GreyboxParts.CreateKicker(new Vector3(-8f, 0f, 14f));
            GreyboxParts.CreateKicker(new Vector3(9f, 0f, -12f), 180f);

            GoalTargetVolume target = CreateGoal(new Vector3(0f, 0f, ArenaSize * 0.5f - 0.5f));

            BallController ball = GreyboxParts.CreateBall(new Vector3(0f, 1.2f, 6f));
            BallHandler dummy = CreateDummyDefender(new Vector3(3f, 0f, 12f), ball);

            GameObject board = GreyboxParts.CreateBoard(new Vector3(0f, 0.4f, -8f));
            var controller = board.GetComponent<BoardController>();

            var handler = board.AddComponent<BallHandler>();
            handler.Ball = ball;
            handler.Board = controller;

            var impact = board.AddComponent<BallImpact>();

            GreyboxParts.CreateCamera(board);
            CreateHud(controller, handler, ball, impact, target);

            Debug.Log($"[50-50] Dummy defender '{dummy.name}' holds the ball until you take it.");

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            Debug.Log($"[50-50] Ball test scene written to {ScenePath}");
        }

        /// <summary>Four walls, so a punched ball comes back rather than vanishing.</summary>
        private static void CreateWalls()
        {
            float half = ArenaSize * 0.5f;
            CreateWall("Wall North", new Vector3(0f, 1.5f, half), new Vector3(ArenaSize, 3f, 1f));
            CreateWall("Wall South", new Vector3(0f, 1.5f, -half), new Vector3(ArenaSize, 3f, 1f));
            CreateWall("Wall East", new Vector3(half, 1.5f, 0f), new Vector3(1f, 3f, ArenaSize));
            CreateWall("Wall West", new Vector3(-half, 1.5f, 0f), new Vector3(1f, 3f, ArenaSize));
        }

        private static void CreateWall(string name, Vector3 position, Vector3 scale)
        {
            GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.name = name;
            wall.transform.position = position;
            wall.transform.localScale = scale;
            GreyboxParts.Paint(wall, new Color(0.24f, 0.25f, 0.28f));
        }

        /// <summary>
        /// A goal mouth: two posts, a bar, and a trigger between them that counts entries and
        /// does nothing else. Scoring is #8's, and a target that scored would prejudge it.
        /// </summary>
        private static GoalTargetVolume CreateGoal(Vector3 position)
        {
            var root = new GameObject("Goal Target");
            root.transform.position = position;

            const float width = 8f;
            const float height = 3.5f;

            CreatePost(root, new Vector3(-width * 0.5f, height * 0.5f, 0f), new Vector3(0.4f, height, 0.4f));
            CreatePost(root, new Vector3(width * 0.5f, height * 0.5f, 0f), new Vector3(0.4f, height, 0.4f));
            CreatePost(root, new Vector3(0f, height, 0f), new Vector3(width, 0.4f, 0.4f));

            var volume = new GameObject("Volume");
            volume.transform.SetParent(root.transform, false);
            volume.transform.localPosition = new Vector3(0f, height * 0.5f, 0.3f);

            var box = volume.AddComponent<BoxCollider>();
            box.size = new Vector3(width, height, 0.6f);
            box.isTrigger = true;

            return volume.AddComponent<GoalTargetVolume>();
        }

        private static void CreatePost(GameObject parent, Vector3 localPosition, Vector3 scale)
        {
            GameObject post = GameObject.CreatePrimitive(PrimitiveType.Cube);
            post.name = "Post";
            post.transform.SetParent(parent.transform, false);
            post.transform.localPosition = localPosition;
            post.transform.localScale = scale;
            GreyboxParts.Paint(post, new Color(0.9f, 0.3f, 0.25f));
        }

        /// <summary>
        /// Something to rob. No board, no input, no movement — it grabs whatever comes near it
        /// and holds on, which is exactly the fixture a strip mechanic needs before a bot exists.
        /// Whether a strip feels like skill or like a lottery is what #17 has to answer.
        /// </summary>
        private static BallHandler CreateDummyDefender(Vector3 position, BallController ball)
        {
            GameObject body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = "Dummy Defender";
            body.transform.position = position + new Vector3(0f, 1f, 0f);
            body.transform.localScale = new Vector3(0.5f, 1f, 0.5f);
            GreyboxParts.Paint(body, new Color(0.35f, 0.5f, 0.75f));

            var handler = body.AddComponent<BallHandler>();
            handler.Ball = ball;
            handler.Board = null;
            handler.AutoGrab = true;
            handler.HoldLimitEnabled = false;
            handler.PunchEnabled = false;
            handler.CarryRadius = 2.2f;
            handler.MaxClosingSpeed = 60f;      // it is a post: it catches anything
            handler.CarryOffset = new Vector3(0.45f, 0.35f, 0.3f);

            return handler;
        }

        private static void CreateHud(
            BoardController board,
            BallHandler handler,
            BallController ball,
            BallImpact impact,
            GoalTargetVolume target)
        {
            var go = new GameObject("Debug HUD");
            var hud = go.AddComponent<BallDebugHud>();
            hud.Board = board;
            hud.Handler = handler;
            hud.Ball = ball;
            hud.Impact = impact;
            hud.Target = target;
        }
    }
}
