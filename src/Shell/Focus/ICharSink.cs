namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Character consumer a focused scope may expose (typeahead search, text
    /// entry). The dispatcher offers layout-aware typed characters to the
    /// top-most live scope with a sink before any chord matching, replacing
    /// TypeaheadConsumerRegistry's hand-mirrored priority numbers with plain
    /// stack order.
    ///
    /// PURE: links into the test project.
    /// </summary>
    public interface ICharSink
    {
        /// <summary>
        /// Handle one typed character. Return true to consume it (the event
        /// stops here); false to let it continue down the stack.
        /// </summary>
        bool HandleChar(char c);
    }
}
