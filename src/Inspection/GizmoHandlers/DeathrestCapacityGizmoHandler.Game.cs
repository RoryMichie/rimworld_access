using System;
using System.Collections.Generic;
using System.Reflection;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Announcement handler for GeneGizmo_DeathrestCapacity (the Biotech deathrest
    /// status panel: "Deathrest" title, need bar, and "Buildings: cur / max" line).
    /// Verbatim port of three legacy GizmoNavigationState facets:
    ///   - the GetGizmoLabel "GeneGizmo_DeathrestCapacity" case (Type.DeathrestCapacity key),
    ///   - GetDeathrestCapacityStatus (need percent + bound-building count vs. capacity),
    ///   - the GeneGizmo_DeathrestCapacity branch of GetNonCommandGizmoDescription
    ///     (vanilla tooltip text plus our bound-buildings list, newlines flattened).
    /// The gizmo's `gene` field is protected in the game source, so it is read via a
    /// cached FieldInfo resolved once up the base chain; everything downstream is
    /// typed against the public Gene_Deathrest surface (DeathrestPercent,
    /// CurrentCapacity, DeathrestCapacity, BoundBuildings, Gene.pawn — all public
    /// per the decompiled source). No facet reads render-populated state.
    /// </summary>
    internal sealed class DeathrestCapacityGizmoHandler : GizmoHandlerBase
    {
        /// <summary>
        /// GeneGizmo_DeathrestCapacity.gene is protected, so reflection is required.
        /// Resolved once, walking the base chain so a modded subclass that inherits
        /// the field still resolves (mirrors the legacy GetType().GetField lookup).
        /// </summary>
        private static readonly FieldInfo GeneField = ResolveGeneField();

        private static FieldInfo ResolveGeneField()
        {
            for (Type t = typeof(GeneGizmo_DeathrestCapacity); t != null; t = t.BaseType)
            {
                FieldInfo field = t.GetField("gene",
                    BindingFlags.Instance | BindingFlags.NonPublic |
                    BindingFlags.Public | BindingFlags.DeclaredOnly);
                if (field != null)
                    return field;
            }
            return null;
        }

        private static Gene_Deathrest GetGene(Gizmo gizmo)
        {
            if (GeneField == null || !(gizmo is GeneGizmo_DeathrestCapacity))
                return null;
            return GeneField.GetValue(gizmo) as Gene_Deathrest;
        }

        public override bool TryGetLabel(Gizmo gizmo, out string label)
        {
            label = "RimWorldAccess.Inspection.Gizmo.Type.DeathrestCapacity".Translate();
            return true;
        }

        public override bool TryGetStatus(Gizmo gizmo, out string status)
        {
            status = null;

            Gene_Deathrest gene = GetGene(gizmo);
            if (gene == null)
                return false;

            // Deathrest progress percentage (the analog of the gizmo's need bar fill).
            string percentStr = $"{gene.DeathrestPercent * 100:F0}%";

            // Bound-building count vs. capacity, mirroring the gizmo's on-screen
            // "Buildings: cur / max" line (vanilla's own translated word).
            string buildingsLabel = "Buildings".Translate().CapitalizeFirst();
            string buildingStr = $"{buildingsLabel}: {gene.CurrentCapacity} / {gene.DeathrestCapacity}";

            status = $"{percentStr}, {buildingStr}";
            return true;
        }

        public override bool TryGetDescription(Gizmo gizmo, out string description)
        {
            description = null;

            if (!(gizmo is GeneGizmo_DeathrestCapacity))
                return false;

            try
            {
                // Vanilla's hover tooltip base text.
                string baseDesc = "DeathrestCapacityDesc".Translate();

                Gene_Deathrest gene = GetGene(gizmo);
                if (gene == null)
                {
                    // The type still owns its description even when the gene can't
                    // be resolved (mirrors the legacy branch's fallback).
                    description = GizmoTextUtility.FlattenNewlines(baseDesc);
                    return true;
                }

                var parts = new List<string> { baseDesc };

                // Vanilla's connected-buildings tooltip sentence.
                Pawn pawn = gene.pawn;
                if (pawn != null)
                {
                    string connected = "PawnIsConnectedToBuildings".Translate(
                        pawn.Named("PAWN"),
                        gene.CurrentCapacity.Named("CURRENT"),
                        gene.DeathrestCapacity.Named("MAX")).Resolve();
                    parts.Add(connected);
                }

                // Surface the names of bound buildings — sighted players see these via
                // hose lines drawn from each building to the deathrester's bed.
                List<Thing> bound = gene.BoundBuildings;
                if (bound != null)
                {
                    var names = new List<string>();
                    foreach (Thing thing in bound)
                    {
                        if (thing != null)
                            names.Add(thing.LabelShortCap);
                    }
                    if (names.Count > 0)
                    {
                        parts.Add("RimWorldAccess.Inspection.Gizmo.DeathrestBoundBuildings"
                            .Translate(names.ToCommaList(useAnd: true)));
                    }
                }

                description = GizmoTextUtility.FlattenNewlines(string.Join(". ", parts));
                return true;
            }
            catch
            {
                // Mirrors the legacy branch's swallow-and-fall-through: any surprise
                // (modded gene subclass, mid-despawn state) defers to the generic
                // description resolution rather than crashing the announcement.
                description = null;
                return false;
            }
        }
    }
}
