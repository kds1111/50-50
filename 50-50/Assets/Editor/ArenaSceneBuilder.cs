using FiftyFifty.Ball;
using FiftyFifty.Board;
using FiftyFifty.Board.Grinds;
using FiftyFifty.Board.Tricks;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FiftyFifty.EditorTools
{
    /// <summary>
    /// A greybox arena for #9, built from the SLS Chicago reference in
    /// `docs/reference/arena-reference-sls-chicago.jpeg`.
    ///
    /// Two deliberate departures from that reference, both worth arguing with rather than
    /// inheriting:
    ///
    /// 1. IT IS SYMMETRIC. An SLS course is asymmetric because it is a judged run, scored by
    ///    people watching one skater at a time. 50-50 is a two-goal contest, so an asymmetric
    ///    course would hand one player better terrain than the other. This arena is
    ///    ROTATIONALLY symmetric — rotate it 180 degrees about the centre and it maps onto
    ///    itself — rather than mirrored, because a mirrored pitch gives the two halves opposite
    ///    handedness, and with tricks that are signed (a kickflip is a heelflip in a mirror) that
    ///    is not a fair swap.
    ///
    /// 2. THE AIRTIME IS AT THE SIDES, NOT THE ENDS. In the reference the biggest transitions
    ///    are at the ends of the course, which is exactly where the goals go here. The map's
    ///    design rests on trick terrain COMPETING with ball position — "the best trick terrain
    ///    must not be the best ball position" — and putting the big banks behind the goals makes
    ///    them the same place, which collapses the tension the whole game is built on. So the
    ///    banks sit along the side walls at mid-field: going for air pulls you off the direct
    ///    line to the goal, and that choice is the game.
    ///
    /// Both are guesses that want playing, not arguments that want winning. #9 is a prototype
    /// ticket precisely because this is decided by driving it.
    ///
    /// Menu: 50-50 > Rebuild Arena Scene
    /// </summary>
    public static class ArenaSceneBuilder
    {
        private const string ScenePath = "Assets/Scenes/Arena.unity";

        /// <summary>
        /// How much bigger than a playable pitch this test map is.
        ///
        /// 1 is the arena proportioned for an actual match — roughly 2.2:1, a little wider than
        /// the SLS reference because two players and a loose ball need room to be beaten
        /// laterally rather than only head-on.
        ///
        /// Above 1 it stops being a pitch and becomes somewhere to drive: room to reach top
        /// speed, hit a bank at it, and see what a spin and a crooked landing do without a wall
        /// arriving first. Obstacle SIZES are deliberately not scaled — a ramp ten times wider
        /// is not a ramp — so the cluster is tiled across the space instead.
        ///
        /// Change this one number to retune the whole map.
        ///
        /// 1.25 — 42.5 x 95 m. It was 10 (340 x 760 m), which played as too big: halved three
        /// times by the dev after the first session with the bank and grinds in it. At this size
        /// the layout is the pitch layout itself, centred, with a little room around it.
        /// </summary>
        private const float Scale = 1.25f;

        private const float HalfWidth = 17f * Scale;
        private const float HalfLength = 38f * Scale;
        private const float WallHeight = 4f * Scale;

        [MenuItem("50-50/Rebuild Arena Scene")]
        public static void Build()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            Selection.objects = new Object[0];

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            GreyboxParts.CreateLighting();
            CreateFloor();
            CreateWalls();

            // Goals face inward from each end, and DO scale — at this size a 9 m goal is a
            // rounding error you could never find, let alone shoot at.
            // Sides are assigned by ScoringWiring below: the -Z goal is side A's.
            ArenaParts.CreateGoal(new Vector3(0f, 0f, HalfLength - 2f), 0f, 9f * Scale, 4f * Scale);
            ArenaParts.CreateGoal(new Vector3(0f, 0f, -(HalfLength - 2f)), 180f, 9f * Scale, 4f * Scale);

            BuildHalf();

            GameObject board = GreyboxParts.CreateBoard(new Vector3(0f, 0.4f, -10f));
            var controller = board.GetComponent<BoardController>();
            var tricks = board.AddComponent<BoardTrickController>();
            board.AddComponent<BoardGrindController>();

            // Ball on the centre spot. There is no kickoff yet (#22), so after a goal the ball
            // stays where it went; the goal's lockout stops it scoring twice.
            BallController ball = GreyboxParts.CreateBall(new Vector3(0f, 1.5f, 0f));

            var handler = board.AddComponent<BallHandler>();
            handler.Ball = ball;
            handler.Board = controller;
            board.AddComponent<BallImpact>();

            GreyboxParts.CreateCamera(board);
            CreateHud(controller, tricks);

            // The bank (#20), and goal sides by the -Z-is-side-A convention. Same code as the
            // menu item, so a rebuilt scene and a wired one match.
            ScoringWiring.WireOpenScene();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            Debug.Log($"[50-50] Arena scene written to {ScenePath}");
        }

        /// <summary>
        /// Everything placed once, then again rotated 180 degrees about the centre. Writing the
        /// layout once is not a convenience — it is what makes the symmetry a property of the
        /// scene rather than something a person has to keep true by hand.
        /// </summary>
        private static void BuildHalf()
        {
            // The layout is written once at pitch scale and tiled across the map, so the space is
            // somewhere to drive rather than seven objects lost in a field. Each station is a
            // full copy, mirrored into the other half by Place as usual.
            int stations = Mathf.Max(1, Mathf.RoundToInt(Scale) / 2);

            // One station is the layout exactly as authored: centred, where every piece was
            // placed against a 34 x 76 pitch. The offsets below exist to spread SEVERAL copies
            // across a big map; applied to a single copy on a small one they push the big bank
            // through the side wall and the stairs past the end wall.
            if (stations == 1)
            {
                BuildStation(Vector3.zero);
                return;
            }

            for (int i = 0; i < stations; i++)
            {
                float z = HalfLength * ((i + 0.5f) / stations);
                float x = ((i % 2 == 0) ? 1f : -1f) * HalfWidth * 0.45f;
                BuildStation(new Vector3(x, 0f, z));
            }
        }

        /// <summary>One copy of the layout, placed relative to an origin.</summary>
        private static void BuildStation(Vector3 origin)
        {
            // The airtime. Against the side wall at mid-field, rising toward the wall: ride at
            // it, go up, come back down. Two different pitches so the arena offers a big trick
            // and a small one rather than one generic amount of air (#6 fixes trick durations in
            // seconds, so the ramps have to be tellable apart).
            Place(p => ArenaParts.CreateBank(p, 90f, 30f, 16f, 9f, "Bank L"), origin + new Vector3(12f, 0f, 8f));
            Place(p => ArenaParts.CreateBank(p, -90f, 18f, 12f, 7f, "Bank S"), origin + new Vector3(-13f, 0f, 22f));

            // A kicker pointing down the pitch. The one piece of terrain that does send you
            // toward a goal — deliberately small, so it is a shortcut rather than the whole game.
            Place(p => ArenaParts.CreateBank(p, 0f, 14f, 7f, 7f, "Kicker"), origin + new Vector3(5f, 0f, 27f));

            // Street furniture. Rails and ledges carry GrindRail (#12); the manual pad and the
            // stairs are deliberately not grindable.
            Place(p => ArenaParts.CreateLedge(p, 0f, 11f, 0.6f, "Hubba"), origin + new Vector3(-7f, 0f, 14f));
            Place(p => ArenaParts.CreateRail(p, 0f, 9f, 0.5f), origin + new Vector3(-2f, 0f, 30f));
            Place(p => ArenaParts.CreateManualPad(p, 0f, 4f, 9f), origin + new Vector3(9f, 0f, 19f));
            Place(p => ArenaParts.CreateStairs(p, 180f, 4, 6f), origin + new Vector3(13f, 0f, 31f));

            // Bail recovery. #6 respawns at the nearest safe point so one mistake costs the bank
            // and not the play; these are spread so no part of the pitch is far from one.
            Place(p => GreyboxParts.CreateSafePoint(p), origin + new Vector3(0f, 0.4f, 30f));
            Place(p => GreyboxParts.CreateSafePoint(p), origin + new Vector3(10f, 0.4f, 14f));
            Place(p => GreyboxParts.CreateSafePoint(p), origin + new Vector3(-10f, 0.4f, 6f));
        }

        /// <summary>Place a piece, then its 180-degree twin.</summary>
        private static void Place(System.Func<Vector3, GameObject> create, Vector3 position)
        {
            create(position);

            GameObject twin = create(new Vector3(-position.x, position.y, -position.z));
            twin.transform.rotation = Quaternion.Euler(0f, 180f, 0f) * twin.transform.rotation;
            twin.name += " (B)";
        }

        private static void CreateFloor()
        {
            GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.name = "Floor";
            floor.transform.localScale = new Vector3(HalfWidth * 2f, 1f, HalfLength * 2f);
            floor.transform.position = new Vector3(0f, -0.5f, 0f);
            GreyboxParts.Paint(floor, new Color(0.52f, 0.52f, 0.54f));

            // Halfway line, so which end is which is readable at speed.
            GameObject line = GameObject.CreatePrimitive(PrimitiveType.Cube);
            line.name = "Halfway Line";
            line.transform.localScale = new Vector3(HalfWidth * 2f, 0.02f, 0.3f * Scale);
            line.transform.position = new Vector3(0f, 0.005f, 0f);
            Object.DestroyImmediate(line.GetComponent<BoxCollider>());
            GreyboxParts.Paint(line, new Color(0.75f, 0.2f, 0.2f));
        }

        private static void CreateWalls()
        {
            const float thickness = 1f;
            float y = WallHeight * 0.5f;

            ArenaParts.CreateWall(
                new Vector3(0f, y, HalfLength + (thickness * 0.5f)),
                new Vector3(HalfWidth * 2f, WallHeight, thickness), "Wall +Z");

            ArenaParts.CreateWall(
                new Vector3(0f, y, -(HalfLength + (thickness * 0.5f))),
                new Vector3(HalfWidth * 2f, WallHeight, thickness), "Wall -Z");

            ArenaParts.CreateWall(
                new Vector3(HalfWidth + (thickness * 0.5f), y, 0f),
                new Vector3(thickness, WallHeight, HalfLength * 2f), "Wall +X");

            ArenaParts.CreateWall(
                new Vector3(-(HalfWidth + (thickness * 0.5f)), y, 0f),
                new Vector3(thickness, WallHeight, HalfLength * 2f), "Wall -X");
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
