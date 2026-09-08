using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Teaches the generic pawn-table tier Hospitality's two hand-copied
    /// "area of accommodation" / "shopping area" columns
    /// (<c>Hospitality.MainTab.PawnColumnWorker_AreaBase</c> and its two
    /// concrete subclasses), which otherwise fall to
    /// <see cref="FallbackColumnHandler"/>: navigable and sortable, but the
    /// cell value is honestly "not readable" and Enter does nothing (recon
    /// §1.2 — the mod's own copy of vanilla's mouse-drag area strip has no
    /// button primitive at all).
    ///
    /// Cell value and mutation both ride the worker's OWN protected
    /// <c>GetArea(Pawn)</c> / <c>SetArea(Pawn, Area)</c> (reflected off the
    /// shared base type; MethodInfo.Invoke on an abstract method performs
    /// virtual dispatch, so one binding serves both concrete subclasses —
    /// the same idiom <see cref="TextColumnHandler"/> and friends use).
    /// Enter opens a windowless float menu built from the mod's OWN area
    /// enumerator (<c>Hospitality.Utilities.GuestUtility.GetAreas(Map)</c>,
    /// which applies the mod's own <c>TDPackAreas.IsAllowed</c> filter) plus
    /// a null "no restriction" entry, each option writing through the SAME
    /// reflected <c>SetArea</c> — Category A, the worker's own mutator, even
    /// though the OPTION LIST itself is hand-built (no existing vanilla/mod
    /// generator produces one for this column, unlike
    /// <see cref="AllowedAreaColumnHandler"/>'s vanilla
    /// <c>MakeAllowedAreaListFloatMenu</c>).
    ///
    /// Resolved against the shared base type, so ONE registration
    /// (<see cref="RegisterHospitalityColumns"/>) covers both
    /// <c>PawnColumnWorker_AccommodationArea</c> and
    /// <c>PawnColumnWorker_ShoppingArea</c> through the type-chain walk;
    /// the two are distinguished at read/write time only where their
    /// wording differs (the null-area word — see remarks below).
    /// </summary>
    internal sealed class HospitalityAreaColumnHandler : PawnColumnHandler
    {
        private readonly MethodInfo getArea;
        private readonly MethodInfo setArea;
        private readonly MethodInfo isGuest;
        private readonly MethodInfo getAreas;
        private readonly Type shoppingAreaType;

        internal HospitalityAreaColumnHandler(MethodInfo getArea, MethodInfo setArea, MethodInfo isGuest,
            MethodInfo getAreas, Type shoppingAreaType)
        {
            this.getArea = getArea;
            this.setArea = setArea;
            this.isGuest = isGuest;
            this.getAreas = getAreas;
            this.shoppingAreaType = shoppingAreaType;
        }

        /// <summary>
        /// Guarded registration: resolves Hospitality's types/methods by name
        /// (never referenced at compile time) and, when the mod is absent or
        /// its shape has drifted from what this handler expects, no-ops
        /// silently rather than leaving a half-registered handler behind.
        /// Called once, from <c>HospitalityCompat</c> (the lead wires the
        /// single call line at commit time — this class owns only the
        /// handler and the registration seam, not the compat call site).
        /// </summary>
        public static void RegisterHospitalityColumns()
        {
            try
            {
                Type areaBaseType = AccessTools.TypeByName("Hospitality.MainTab.PawnColumnWorker_AreaBase");
                Type shoppingAreaType = AccessTools.TypeByName("Hospitality.MainTab.PawnColumnWorker_ShoppingArea");
                Type guestUtilityType = AccessTools.TypeByName("Hospitality.Utilities.GuestUtility");
                if (areaBaseType == null || guestUtilityType == null)
                {
                    return;
                }

                MethodInfo getArea = AccessTools.Method(areaBaseType, "GetArea", new[] { typeof(Pawn) });
                MethodInfo setArea = AccessTools.Method(areaBaseType, "SetArea", new[] { typeof(Pawn), typeof(Area) });
                MethodInfo isGuest = AccessTools.Method(guestUtilityType, "IsGuest", new[] { typeof(Pawn) });
                MethodInfo getAreas = AccessTools.Method(guestUtilityType, "GetAreas", new[] { typeof(Map) });
                if (getArea == null || setArea == null || isGuest == null || getAreas == null)
                {
                    Log.Warning("[RimWorld Access] Hospitality area column handler: expected members not found; the mod build may have drifted. Area columns stay read-only.");
                    return;
                }

                PawnColumnHandlerRegistry.Register(areaBaseType,
                    new HospitalityAreaColumnHandler(getArea, setArea, isGuest, getAreas, shoppingAreaType));
                Log.Message("[RimWorld Access] Hospitality compat: area column handler registered.");
            }
            catch (Exception ex)
            {
                Log.Error("[RimWorld Access] Hospitality area column handler registration failed: " + ex.Message);
            }
        }

        private bool IsShoppingArea(PawnColumnWorker worker)
        {
            return shoppingAreaType != null && shoppingAreaType.IsInstanceOfType(worker);
        }

        /// <summary>
        /// <c>area?.Label</c>, exactly what both the mod's own strips draw
        /// per option (recon §1.2's <c>AreaGUI.DoAreaSelector</c> /
        /// <c>GuestUtility.DoAreaSelector</c> label expressions) for the
        /// CURRENTLY selected option. The two null-area words differ
        /// per column (accommodation: "unrestricted"; shopping: the mod's
        /// own "AreaNoShopping" wording, "No shopping") — both minted as our
        /// own keys rather than calling either vanilla's or the mod's
        /// Translate() on our behalf, per the localization doctrine (never
        /// translate a third party's raw keys for them).
        /// </summary>
        public override string CellText(PawnColumnDef def, Pawn pawn)
        {
            PawnColumnWorker worker = def.Worker;
            if (!(bool)isGuest.Invoke(null, new object[] { pawn }))
            {
                // Vanilla's own DoCell returns early for a non-guest — blank,
                // not a fabricated word (recon §9.6).
                return "";
            }
            Area area = (Area)getArea.Invoke(worker, new object[] { pawn });
            if (area != null)
            {
                return area.Label.StripTags();
            }
            return (IsShoppingArea(worker)
                ? "RimWorldAccess.Compat.Hospitality.NoShoppingArea"
                : "RimWorldAccess.Compat.Hospitality.NoAccommodationArea").Loc().ToString();
        }

        public override PawnColumnActivation ActivateCell(PawnColumnDef def, Pawn pawn, PawnTable table)
        {
            PawnColumnWorker worker = def.Worker;
            if (!(bool)isGuest.Invoke(null, new object[] { pawn }))
            {
                return PawnColumnActivation.NotHandled;
            }
            Map map = pawn.MapHeld;
            if (map == null)
            {
                return PawnColumnActivation.NotHandled;
            }
            IEnumerable areas = getAreas.Invoke(null, new object[] { map }) as IEnumerable;
            if (areas == null)
            {
                return PawnColumnActivation.NotHandled;
            }

            bool shopping = IsShoppingArea(worker);
            string noneLabel = (shopping
                ? "RimWorldAccess.Compat.Hospitality.NoShoppingArea"
                : "RimWorldAccess.Compat.Hospitality.NoAccommodationArea").Loc().ToString();

            var options = new List<FloatMenuOption>();
            options.Add(new FloatMenuOption(noneLabel, delegate
            {
                setArea.Invoke(worker, new object[] { pawn, null });
            }));
            foreach (object boxed in areas)
            {
                var area = (Area)boxed;
                if (area == null)
                {
                    continue;
                }
                Area capturedArea = area;
                options.Add(new FloatMenuOption(capturedArea.Label, delegate
                {
                    setArea.Invoke(worker, new object[] { pawn, capturedArea });
                }));
            }
            WindowlessFloatMenuState.Open(options, colonistOrders: false);
            return PawnColumnActivation.OpenedUI;
        }

        /// <summary>
        /// The float-menu picker above replaces the mod's own N-option
        /// mouse-drag strip entirely, so the geometry-scoped captured-extras
        /// router (<see cref="GenericPawnTableScope"/>) must not ALSO surface
        /// that strip's bare option labels as unmirrored extras — they would
        /// otherwise flood the table with up to a dozen rows per guest
        /// (recon §9.3).
        /// </summary>
        public override bool SuppressCapturedExtras(PawnColumnDef def)
        {
            return true;
        }
    }
}
