using System;
using FiftyFifty.Board.Tricks;
using NUnit.Framework;

namespace FiftyFifty.Tests
{
    /// <summary>
    /// Where a bail puts you back (#25): the fall point first, then rings fanning outward, and
    /// which way you face when you get there. Geometry and rule only — the probing of actual
    /// ground is Unity's job and is judged by playing.
    /// </summary>
    public class RespawnPlacementTests
    {
        private const float Tolerance = 0.0005f;

        private static void AssertSpot((float X, float Z) spot, float x, float z, string what)
        {
            Assert.AreEqual(x, spot.X, Tolerance, what + " (x)");
            Assert.AreEqual(z, spot.Z, Tolerance, what + " (z)");
        }

        [Test]
        public void Where_you_fell_is_tried_before_anywhere_else()
        {
            (float X, float Z)[] spots = RespawnPlacement.Offsets(new[] { 1f }, new[] { 4 });

            AssertSpot(spots[0], 0f, 0f, "the fall point itself comes first");
        }

        [Test]
        public void A_ring_is_evenly_spaced_around_its_radius()
        {
            // One ring of four at 2 m: north, east, south, west, in that turn order.
            (float X, float Z)[] spots = RespawnPlacement.Offsets(new[] { 2f }, new[] { 4 });

            Assert.AreEqual(5, spots.Length, "the fall point plus four");
            AssertSpot(spots[1], 0f, 2f, "first sample is straight ahead");
            AssertSpot(spots[2], 2f, 0f, "quarter turn");
            AssertSpot(spots[3], 0f, -2f, "half turn");
            AssertSpot(spots[4], -2f, 0f, "three quarter turn");
        }

        [Test]
        public void Rings_are_searched_nearest_first()
        {
            (float X, float Z)[] spots = RespawnPlacement.Offsets(new[] { 1f, 4f }, new[] { 2, 2 });

            float previous = -1f;

            foreach ((float X, float Z) spot in spots)
            {
                float distance = (float)Math.Sqrt((spot.X * spot.X) + (spot.Z * spot.Z));
                Assert.GreaterOrEqual(distance + Tolerance, previous, "spots never step back inward");
                previous = distance;
            }

            Assert.AreEqual(5, spots.Length, "the fall point plus two rings of two");
        }

        [Test]
        public void You_face_the_way_you_were_travelling()
        {
            float facing = RespawnPlacement.Facing(speed: 8f, travelHeading: 30f, boardHeading: 200f, travelSpeedFloor: 2f);

            Assert.AreEqual(30f, facing, Tolerance);
        }

        [Test]
        public void Too_slow_to_say_where_you_were_going_uses_the_board()
        {
            // Below the floor, travel direction is a slide or a shove rather than an intention,
            // so the board's own heading is the better answer. The floor is this rule's own; no
            // other system shares it.
            float facing = RespawnPlacement.Facing(speed: 0.4f, travelHeading: 30f, boardHeading: 200f, travelSpeedFloor: 2f);

            Assert.AreEqual(200f, facing, Tolerance);
        }
    }
}
