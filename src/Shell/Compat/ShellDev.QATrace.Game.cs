#if DEBUG
namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Dev-bridge face of the QA flight recorder (<see cref="FlightRecorder"/>, which owns the
    /// writer and records in every build). DEBUG-only callers — tripwires, oracles, the bridge —
    /// come through here.
    /// </summary>
    public static partial class ShellDev
    {
        /// <summary>DEBUG diagnostic tee into the flight recorder log.</summary>
        internal static void QARecord(string kind, string line)
        {
            FlightRecorder.Record(kind, line);
        }

        /// <summary>Drops a labeled marker into the log — call over the dev bridge between test segments so the log reads back with the transcript's section breaks.</summary>
        public static string QAMark(string note)
        {
            FlightRecorder.Record("mark", note);
            return "marked: " + note;
        }

        /// <summary>Current QA flight recorder log path (or a placeholder if none started yet) and entry count.</summary>
        public static string QATraceStatus()
        {
            return FlightRecorder.Status();
        }
    }
}
#endif
