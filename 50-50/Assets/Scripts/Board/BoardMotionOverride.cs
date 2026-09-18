namespace FiftyFifty.Board
{
    /// <summary>
    /// Something that can take the board's motion away from the controller for a tick — a grind
    /// (#12) is the one that exists.
    ///
    /// It is asked at the top of every step, before gravity. Returning true means "I moved the
    /// board this tick": the controller then skips gravity, suspension, drive, grip and the air
    /// spin, and counts itself grounded, so the ollie and trick touchdown work unchanged on top of
    /// it. Returning false hands the tick back and nothing is different.
    ///
    /// The contract with #16's law: an override may set position and velocity, and may set the
    /// heading through <see cref="BoardController.SetHeading"/>. It never rotates the rigidbody —
    /// orientation stays the controller's, built from the heading as it always is.
    /// </summary>
    public interface IBoardMotionOverride
    {
        bool Step(BoardController board, float dt);
    }
}
