using System;

namespace RimWorldAccess
{
    /// <summary>
    /// The lazy resolve-once gate compat files hand-roll as a resolved/failed bool pair: the first
    /// <see cref="Ensure"/> runs the resolve callback against a fresh <see cref="ReflectionSurface"/>,
    /// records the outcome, and every later call returns it without re-probing. The callback binds
    /// to locals, checks what it needs, and only then assigns the caller's fields -- returning false
    /// leaves the caller exactly as unready as an exception does.
    /// </summary>
    internal sealed class LazyReflectionGate
    {
        private readonly string surfaceName;
        private readonly Func<ReflectionSurface, bool> resolve;
        private bool resolved;
        private bool failed;

        public LazyReflectionGate(string surfaceName, Func<ReflectionSurface, bool> resolve)
        {
            this.surfaceName = surfaceName;
            this.resolve = resolve;
        }

        public bool Ensure()
        {
            if (resolved)
            {
                return !failed;
            }
            resolved = true;

            try
            {
                var surface = new ReflectionSurface(surfaceName);
                failed = !resolve(surface);
                return !failed;
            }
            catch (Exception ex)
            {
                ModLogger.Error($"{surfaceName}: reflection setup failed: {ex.Message}");
                failed = true;
                return false;
            }
        }
    }
}
