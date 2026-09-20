using FiftyFifty.Board.Tricks;
using NUnit.Framework;

namespace FiftyFifty.Tests
{
    /// <summary>
    /// The buffer that holds a trick press until the board is airborne enough to commit it (#6).
    /// It expires on simulation time, never on the wall clock (#26), so a replayed tick reaches
    /// the same answer as the tick it replaces.
    /// </summary>
    public class TrickInputBufferTests
    {
        private const float Tick = 0.02f;

        /// <summary>Whole steps, counted — adding floats in a loop condition drifts.</summary>
        private static void Steps(TrickInputBuffer buffer, int count)
        {
            for (int i = 0; i < count; i++)
            {
                buffer.Tick(Tick);
            }
        }

        [Test]
        public void An_empty_buffer_offers_nothing()
        {
            var buffer = new TrickInputBuffer { WindowSeconds = 0.25f };

            Assert.AreEqual(0, buffer.Live);

            buffer.Tick(Tick);

            Assert.AreEqual(0, buffer.Live);
        }

        [Test]
        public void A_press_stays_live_until_its_window_runs_out()
        {
            var buffer = new TrickInputBuffer { WindowSeconds = 0.25f };
            buffer.Accept(2);

            Assert.AreEqual(2, buffer.Live, "the press is there straight away");

            Steps(buffer, 12);

            Assert.AreEqual(2, buffer.Live, "0.24s in, still inside a 0.25s window");

            Steps(buffer, 2);

            Assert.AreEqual(0, buffer.Live, "the window has passed");
        }

        [Test]
        public void A_second_press_replaces_the_first_and_gets_a_full_window()
        {
            var buffer = new TrickInputBuffer { WindowSeconds = 0.25f };
            buffer.Accept(1);

            Steps(buffer, 10);

            buffer.Accept(3);

            Steps(buffer, 10);

            Assert.AreEqual(3, buffer.Live, "the newer press is the one held, with its own window");
        }

        [Test]
        public void Clearing_drops_the_press_at_once()
        {
            var buffer = new TrickInputBuffer { WindowSeconds = 0.25f };
            buffer.Accept(2);

            buffer.Clear();

            Assert.AreEqual(0, buffer.Live);
        }
    }
}
