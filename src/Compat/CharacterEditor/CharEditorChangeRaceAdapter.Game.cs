using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorldAccess.Shell;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Adapter for <c>CharacterEditor.DialogChangeRace</c>. One ComboBox filter (race)
    /// plus one Checkbox filter (keep race-specific apparel, backed by the dialog's own
    /// <c>SearchTool.ofilter1</c> slot); results are the pawn-kind candidates for the currently
    /// selected race. Confirm invokes <c>DoAndClose</c> directly (no <c>OnAcceptKeyPressed</c>
    /// override exists on this dialog). KNOWN QUIRKS preserved, not fixed:
    /// the race list is derived from the SHARED static list-source tab
    /// (<c>CharacterEditor.CEditor.ListName</c>), not from the target pawn's own race category;
    /// and OK re-evaluates the pawn's CURRENT humanlike-ness and silently discards the kind
    /// selection for a non-humanlike target.
    ///
    /// Only a deliberate Enter/Space (<see cref="SelectResult"/>) ever stages a kind here:
    /// <c>DoAndClose</c> consumes whatever <c>selectedPKD</c> holds at Confirm time, and browsing
    /// must never change anything (see <see cref="Shell.CharEditorBrowserScope"/>'s class remarks),
    /// so no cursor sweep can restage it. Row labels also disambiguate same-named
    /// kinds from different mods (<see cref="DescribeResultLabel"/>)
    /// and fall back to <c>defName</c> for a def whose <c>label</c> is null (<see cref="KindLabel"/>) --
    /// <c>PawnKindTool.ListOfPawnKindDefByRace</c> sorts null labels first, so an unnamed kind would
    /// otherwise occupy row 0 with nothing to say.
    /// </summary>
    internal sealed class ChangeRaceAdapter : CharEditorBrowserAdapterBase
    {
        /// <summary>
        /// Cache for <see cref="DuplicateKinds"/>: the results list the cached duplicate set was
        /// computed against, so a describe pass over every visible row (typeahead, a full-row
        /// re-announce) builds the duplicate-label groups ONCE per snapshot rather than once per
        /// row. Invalidated by comparing element-wise against the freshly fetched results list --
        /// cheap, and correct even in the rare case the mod's own <c>HashSet&lt;PawnKindDef&gt;</c>
        /// enumerates in a different order than last time (a benign extra rebuild, never a wrong
        /// answer).
        /// </summary>
        private List<PawnKindDef> cachedKindResults;
        private HashSet<PawnKindDef> cachedDuplicateKinds;

        public ChangeRaceAdapter(Window dialog)
            : base(dialog)
        {
        }

        public override string Title => "RimWorldAccess.CharEd.Browser.ChangeRaceTitle".Translate().ToString();

        protected override IReadOnlyList<CharEditorBrowserFilter> BuildFilters()
        {
            return new List<CharEditorBrowserFilter>
            {
                new CharEditorBrowserFilter
                {
                    Label = "RimWorldAccess.CharEd.Browser.RaceFilter".Translate(),
                    Candidates = () => CharEditorBrowserCompat.ChangeRaceCandidates(dialog).Select(RaceLabel).ToList(),
                    CurrentIndex = delegate
                    {
                        ThingDef current = CharEditorBrowserCompat.ChangeRaceSelected(dialog);
                        return CharEditorBrowserCompat.ChangeRaceCandidates(dialog).FindIndex(r => r == current);
                    },
                    SetCandidate = delegate(int candidateIndex)
                    {
                        List<ThingDef> candidates = CharEditorBrowserCompat.ChangeRaceCandidates(dialog);
                        if (candidateIndex >= 0 && candidateIndex < candidates.Count)
                            CharEditorBrowserCompat.ChangeRaceSetRace(dialog, candidates[candidateIndex]);
                    },
                    // The dialog's own current race is read directly, not looked up in the candidate
                    // list: the list comes from the SHARED list-source tab (see class remarks), so the
                    // selected race is not always in it.
                    ValueLabel = () => RaceLabel(CharEditorBrowserCompat.ChangeRaceSelected(dialog)),
                },
                Checkbox("RimWorldAccess.CharEd.Browser.ChangeRace.RaceSpecificDress".Translate(),
                    () => CharEditorBrowserCompat.ChangeRaceRaceSpecificDress(dialog),
                    delegate
                    {
                        bool current = CharEditorBrowserCompat.ChangeRaceRaceSpecificDress(dialog);
                        CharEditorBrowserCompat.ChangeRaceSetRaceSpecificDress(dialog, !current);
                    }),
            };
        }

        public override int ResultCount => CharEditorBrowserCompat.ChangeRaceKindCandidates(dialog).Count;

        public override string DescribeResultLabel(int index)
        {
            var results = CharEditorBrowserCompat.ChangeRaceKindCandidates(dialog);
            if (index < 0 || index >= results.Count)
                return "";
            PawnKindDef kind = results[index];
            string label = KindLabel(kind);
            if (!DuplicateKinds(results).Contains(kind))
                return label;
            // Two or more rows in the CURRENT results share this label -- the mod's own
            // IsFromMod(modname) is exactly this same modContentPack comparison
            // (PawnKindTool.cs:15-18), so the source it names is the source a sighted player
            // would look up too.
            string source = kind?.modContentPack?.Name ?? kind?.defName ?? "";
            return "RimWorldAccess.CharEd.Browser.KindFromMod".Translate(label, source).ToString();
        }

        public override string DescribeResultTooltip(int index)
        {
            var results = CharEditorBrowserCompat.ChangeRaceKindCandidates(dialog);
            return index >= 0 && index < results.Count ? results[index]?.description : null;
        }

        /// <summary>Builds (or returns the cached) set of kinds whose bare label is shared by 2+ rows in the current results snapshot -- see the cache fields' remarks.</summary>
        private HashSet<PawnKindDef> DuplicateKinds(List<PawnKindDef> results)
        {
            if (cachedKindResults != null && cachedKindResults.SequenceEqual(results))
                return cachedDuplicateKinds;

            var countByLabel = new Dictionary<string, int>();
            foreach (PawnKindDef kind in results)
            {
                string label = KindLabel(kind);
                countByLabel.TryGetValue(label, out int count);
                countByLabel[label] = count + 1;
            }
            var duplicates = new HashSet<PawnKindDef>();
            foreach (PawnKindDef kind in results)
            {
                if (countByLabel[KindLabel(kind)] > 1)
                    duplicates.Add(kind);
            }
            cachedKindResults = results;
            cachedDuplicateKinds = duplicates;
            return duplicates;
        }

        public override int SelectedResultIndex
        {
            get
            {
                var results = CharEditorBrowserCompat.ChangeRaceKindCandidates(dialog);
                PawnKindDef selected = CharEditorBrowserCompat.ChangeRaceSelectedKind(dialog);
                return results.IndexOf(selected);
            }
        }

        public override void SelectResult(int index)
        {
            var results = CharEditorBrowserCompat.ChangeRaceKindCandidates(dialog);
            if (index >= 0 && index < results.Count)
                CharEditorBrowserCompat.ChangeRaceSetSelectedKind(dialog, results[index]);
        }

        public override bool Confirm()
        {
            return CharEditorBrowserCompat.ChangeRaceConfirm(dialog);
        }

        private string RaceLabel(ThingDef race)
        {
            return race == null ? AllLabel() : race.LabelCap.ToString();
        }

        private string KindLabel(PawnKindDef kind)
        {
            if (kind == null)
                return AllLabel();
            // Def.LabelCap returns null when label is null/empty -- PawnKindTool.ListOfPawnKindDefByRace
            // sorts null labels first, so a def with no label would otherwise occupy row 0 and say
            // nothing. defName is live def data (never fabricated), matching every other def-name
            // fallback this facade uses.
            string label = kind.LabelCap.ToString();
            return label.NullOrEmpty() ? kind.defName : label;
        }
    }
}
