namespace RimWorldAccess.Shell
{
    /// <summary>What a navigation attempt did. The scope speaks; models never announce.</summary>
    public enum MoveKind
    {
        /// <summary>Cursor moved to a new position.</summary>
        Moved,
        /// <summary>Already at the boundary and wrapping is off — cursor unchanged.</summary>
        AtEdge,
        /// <summary>Moved by wrapping past a boundary (the wrap setting).</summary>
        Wrapped,
        /// <summary>Container has no items.</summary>
        Empty,
    }

    /// <summary>Result of one cursor move: what happened plus the resulting index.</summary>
    public readonly struct MoveResult
    {
        public readonly MoveKind Kind;
        public readonly int Index;

        public MoveResult(MoveKind kind, int index)
        {
            Kind = kind;
            Index = index;
        }

        /// <summary>True when the cursor is now somewhere new (moved or wrapped).</summary>
        public bool Changed
        {
            get { return Kind == MoveKind.Moved || Kind == MoveKind.Wrapped; }
        }

        public override string ToString()
        {
            return Kind + "@" + Index;
        }
    }
}
