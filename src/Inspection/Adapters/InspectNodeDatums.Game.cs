using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Language-independent tags for section/heading nodes inside a tab's
    /// subtree (gear categories, social sections, training sections, health
    /// sections, log sections). Never compare a section by its translated
    /// label — this tag is the identity.
    /// </summary>
    public enum InspectSectionKind
    {
        GearEquipment,
        GearApparel,
        GearInventory,
        MoodBreakThresholds,
        SocialRelations,
        SocialIdeoligion,
        SocialRomance,
        IdeoligionRoles,
        TrainingMaster,
        TrainingSkills,
        HealthOperations,
        HealthSettings,
        HealthCapacities,
        LogCombat,
        LogSocial,
    }

    /// <summary>
    /// Datum for a section/heading node within a pawn tab's subtree. These
    /// nodes previously carried the parent <see cref="Pawn"/> itself, which
    /// made them indistinguishable from object nodes under type-keyed
    /// dispatch (rework §B.3 item 0b).
    /// </summary>
    public sealed class InspectSectionDatum
    {
        public Pawn Pawn { get; }
        public InspectSectionKind Kind { get; }

        public InspectSectionDatum(Pawn pawn, InspectSectionKind kind)
        {
            Pawn = pawn;
            Kind = kind;
        }
    }

    /// <summary>
    /// Datum for a gear action row (Drop / Consume / View info) under a gear
    /// item. Replaces an anonymous-type payload that could not serve as a
    /// registry key (rework §B.3 item 0a).
    /// </summary>
    public sealed class GearActionDatum
    {
        public Pawn Pawn { get; }
        public InteractiveGearHelper.GearItem Gear { get; }
        public GearAction Action { get; }

        public GearActionDatum(Pawn pawn, InteractiveGearHelper.GearItem gear, GearAction action)
        {
            Pawn = pawn;
            Gear = gear;
            Action = action;
        }
    }

    /// <summary>
    /// Datum for a combat/social log entry row, read from the POV of the
    /// inspected pawn. Replaces an anonymous-type payload (rework §B.3
    /// item 0a).
    /// </summary>
    public sealed class LogEntryDatum
    {
        public Pawn Pawn { get; }
        public LogEntry Entry { get; }

        public LogEntryDatum(Pawn pawn, LogEntry entry)
        {
            Pawn = pawn;
            Entry = entry;
        }
    }

    /// <summary>
    /// Datum for an info-card stat row whose value column is empty (a heading
    /// or spacer row). Deliberately NOT a <see cref="StatDrawEntry"/> so the
    /// Alt+I hyperlink walk in InfoCardState passes over it and keeps climbing
    /// to a real stat ancestor — previously expressed by nulling Data (rework
    /// §B.3 item 0c).
    /// </summary>
    public sealed class EmptyValueStatDatum
    {
        public StatDrawEntry Entry { get; }

        public EmptyValueStatDatum(StatDrawEntry entry)
        {
            Entry = entry;
        }
    }
}
