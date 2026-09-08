namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Marks the window in which the shell is running a keyboard action: a
    /// scope claim handler or a live CharSink, the only two places a keystroke
    /// turns into behaviour. Patches downstream read it to tell a state change
    /// the user drove from the keyboard apart from one a mouse click drove
    /// (<c>DesignatorManagerPatch</c> is the first consumer).
    ///
    /// Depth-counted because handlers nest: a claim handler may re-enter the
    /// stack, and every Begin is paired with an End in a finally.
    ///
    /// This is deliberately NOT a frame stamp. The focused window's GUI pass
    /// can run before the dispatcher inside the same frame, so a same-frame
    /// stamp cannot separate the two origins (QA R6).
    ///
    /// PURE: links into the test project.
    /// </summary>
    public static class ShellKeyboardOrigin
    {
        private static int depth;

        /// <summary>True while a shell keyboard action handler is on the call stack.</summary>
        public static bool Active
        {
            get { return depth > 0; }
        }

        public static void Begin()
        {
            depth++;
        }

        public static void End()
        {
            if (depth > 0)
            {
                depth--;
            }
        }
    }
}
