using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Single cached-reflection accessor for the handful of vanilla members our placement code
    /// needs that aren't public. Previously the private Designator_Place.placingRot field was
    /// looked up independently in four places (two of them re-resolving the FieldInfo on every
    /// keypress instead of caching it), and the gravship landing marker was reached via a
    /// GetType().Name string check plus a fresh reflection chain in three places even though
    /// Designator_MoveGravship and GravshipLandingMarker are both public types with public
    /// members - no reflection is actually needed for gravship, just a direct cast.
    /// </summary>
    public static class BuildingReflection
    {
        private static readonly FieldInfo placingRotField =
            AccessTools.Field(typeof(Designator_Place), "placingRot");

        private static readonly FieldInfo writeStuffField =
            AccessTools.Field(typeof(Designator_Build), "writeStuff");

        private static readonly MethodInfo handleRotationMethod =
            AccessTools.Method(typeof(Designator_Place), "HandleRotation", new[] { typeof(RotationDirection) });

        /// <summary>
        /// Whether the placingRot field lookup succeeded. Exposed so callers that historically
        /// did nothing at all when the field was unavailable (rather than falling back to
        /// Rot4.North) can preserve that behavior.
        /// </summary>
        public static bool HasPlacingRotField => placingRotField != null;

        /// <summary>
        /// Gets the current placing rotation of a Designator_Place. Returns Rot4.North if the
        /// field could not be resolved (or the designator is null).
        /// </summary>
        public static Rot4 GetPlacingRot(Designator_Place designator)
        {
            if (designator == null || placingRotField == null)
                return Rot4.North;
            return (Rot4)placingRotField.GetValue(designator);
        }

        /// <summary>
        /// Sets the placing rotation of a Designator_Place. No-op if the field could not be
        /// resolved (or the designator is null).
        /// </summary>
        public static void SetPlacingRot(Designator_Place designator, Rot4 rotation)
        {
            if (designator == null || placingRotField == null)
                return;
            // MUTATION-C: mirrors vanilla Designator_Place.HandleRotationShortcuts, which mutates
            // placingRot directly on rotate-key input (Designator_Place.cs:227-236; Selected()
            // resets it to PlacingDef.defaultPlacingRot at :258). Keyboard rotation is our
            // accessible equivalent of those same key branches; no gate exists on the vanilla path.
            placingRotField.SetValue(designator, rotation);
        }

        /// <summary>
        /// Runs vanilla's own turn-to-face-a-wall on a Designator_Place: its private HandleRotation,
        /// whose attachment branch keeps rotating until the thing faces a wall attachment support
        /// (Designator_Place.cs:221-238). The game's own method performs the mutation and its own
        /// loop picks the facing; reflection is only how a private method gets called. No-op if the
        /// lookup failed.
        /// </summary>
        public static void HandleAttachmentRotation(Designator_Place designator, RotationDirection direction)
        {
            if (designator == null || handleRotationMethod == null)
                return;
            handleRotationMethod.Invoke(designator, new object[] { direction });
        }

        /// <summary>
        /// Checks if a designator is the gravship landing-placement designator by game identity
        /// (type check) rather than by GetType().Name string comparison.
        /// </summary>
        public static bool IsGravshipDesignator(Designator designator)
        {
            return designator is Designator_MoveGravship;
        }

        /// <summary>
        /// Gets the landing marker for a gravship-placement designator via a direct cast and
        /// public field read - both Designator_MoveGravship.marker and GravshipLandingMarker
        /// are public, so no reflection is involved. Returns null if the designator isn't a
        /// gravship-placement designator or has no marker.
        /// </summary>
        public static GravshipLandingMarker GetGravshipMarker(Designator designator)
        {
            return (designator as Designator_MoveGravship)?.marker;
        }

        /// <summary>
        /// Sets the writeStuff flag on a Designator_Build. No-op if the field could not be
        /// resolved (or the designator is null).
        /// </summary>
        public static void SetWriteStuff(Designator_Build designator, bool value)
        {
            if (designator == null || writeStuffField == null)
                return;
            // MUTATION-C: mirrors the stuff float-menu delegate in Designator_Build.ProcessInput
            // (stuffDef = localStuffDef; writeStuff = true); writeStuff has no public setter and
            // SetStuffDef alone leaves the label in pre-material form.
            writeStuffField.SetValue(designator, value);
        }

        private static readonly FieldInfo shownItemField =
            AccessTools.Field(typeof(FloatMenuOption), "shownItem");

        /// <summary>
        /// The ThingDef a FloatMenuOption displays as its icon (private shownItem field,
        /// set by the shownItemForIcon ctor argument). For Designator_Build stuff menus,
        /// vanilla and mods alike pass the stuff def here. Null if absent or unresolved.
        /// </summary>
        public static ThingDef GetShownItem(FloatMenuOption option)
        {
            if (option == null || shownItemField == null)
                return null;
            return shownItemField.GetValue(option) as ThingDef;
        }
    }
}
