using System.Collections.Generic;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Transport pod selection mode: the map cursor navigates, Space toggles the pod at the cursor,
    /// Enter confirms the group. A map-cursor toggle-selection mode, not a navigable list — arrow
    /// keys are deliberately unclaimed and fall through to the map, and the selection itself lives
    /// in the game's own Find.Selector rather than a mod-tracked cursor.
    /// </summary>
    public static class TransportPodSelectionState
    {
        /// <summary>Whether pod selection mode is active.</summary>
        public static bool IsActive { get; private set; }

        private static Map currentMap;

        private static CompTransporter sourcePod;

        /// <summary>Pods groupable with the source, flood-filled once at Open time.</summary>
        private static HashSet<CompTransporter> groupablePods;

        /// <summary>Opens pod selection mode; a source pod with no adjacent pods loads directly instead.</summary>
        public static void Open(CompTransporter sourceTransporter)
        {
            if (!GuardHelper.RequireMap(SpeechPriority.High)) return;

            if (sourceTransporter == null)
            {
                TolkHelper.Speak("RimWorldAccess.TransportPods.Selection.NoPodSelected".Loc(), SpeechPriority.High);
                return;
            }

            currentMap = Find.CurrentMap;
            sourcePod = sourceTransporter;

            var groupableList = TransportPodHelper.GetGroupablePodsFor(sourceTransporter, currentMap);

            if (groupableList.Count == 0)
            {
                sourcePod = null;
                LoadSinglePod(sourceTransporter);
                return;
            }

            // The groupable set, source pod included, validates each toggle during selection.
            groupablePods = new HashSet<CompTransporter>(groupableList);
            groupablePods.Add(sourceTransporter);

            Find.Selector.ClearSelection();
            Find.Selector.Select(sourceTransporter.parent);

            IsActive = true;

            int totalGroupable = groupablePods.Count;
            TolkHelper.Speak("RimWorldAccess.TransportPods.Selection.EnterMode".Loc(totalGroupable), SpeechPriority.High);
        }

        /// <summary>Loads a single pod without entering selection mode.</summary>
        private static void LoadSinglePod(CompTransporter transporter)
        {
            if (transporter?.parent == null)
                return;

            Find.Selector.ClearSelection();
            Find.Selector.Select(transporter.parent);

            foreach (var gizmo in transporter.CompGetGizmosExtra())
            {
                if (gizmo is Command_LoadToTransporter loadCommand)
                {
                    loadCommand.ProcessInput(null);
                    return;
                }
            }

            TolkHelper.Speak("RimWorldAccess.TransportPods.Gizmo.LoadCommandMissing".Loc(), SpeechPriority.High);
            Find.Selector.ClearSelection();
        }

        /// <summary>Closes pod selection mode without grouping.</summary>
        public static void Close()
        {
            IsActive = false;
            currentMap = null;
            sourcePod = null;
            groupablePods = null;

            Find.Selector.ClearSelection();

            TolkHelper.Speak("RimWorldAccess.TransportPods.Selection.Cancelled".Loc(), SpeechPriority.Normal);
        }

        /// <summary>
        /// Silent session-boundary reset: unlike <see cref="Close"/> it speaks nothing and clears
        /// every field including the game's own selection, so it is safe unconditionally at a
        /// save-load or main-menu boundary.
        /// </summary>
        internal static void ResetHard()
        {
            IsActive = false;
            currentMap = null;
            sourcePod = null;
            groupablePods = null;
            Find.Selector.ClearSelection();
        }

        /// <summary>Toggles selection of any transport pod at the cursor; arrow keys fall through to the map.</summary>
        internal static void TogglePodAtCursor()
        {
            IntVec3 cursorPos = MapNavigationState.CurrentCursorPosition;

            if (!cursorPos.InBounds(currentMap))
            {
                TolkHelper.Speak("RimWorldAccess.Input.Cursor.InvalidPosition".Loc(), SpeechPriority.Normal);
                return;
            }

            var pods = TransportPodHelper.GetTransportPodsAt(cursorPos, currentMap);

            if (pods.Count == 0)
            {
                TolkHelper.Speak("RimWorldAccess.TransportPods.Selection.NoPodHere".Loc(), SpeechPriority.Normal);
                return;
            }

            var pod = pods[0];
            if (pod?.parent == null)
                return;

            Thing podThing = pod.parent;

            if (Find.Selector.IsSelected(podThing))
            {
                Find.Selector.Deselect(podThing);
                int selectedCount = GetSelectedPodCount();
                TolkHelper.Speak("RimWorldAccess.TransportPods.Selection.PodDeselected".Loc(selectedCount), SpeechPriority.Normal);
            }
            else
            {
                if (groupablePods == null || !groupablePods.Contains(pod))
                {
                    TolkHelper.Speak("RimWorldAccess.TransportPods.Selection.NotAdjacent".Loc(), SpeechPriority.Normal);
                    return;
                }

                if (pod.LoadingInProgressOrReadyToLaunch)
                {
                    TolkHelper.Speak("RimWorldAccess.TransportPods.Selection.AlreadyLoading".Loc(), SpeechPriority.Normal);
                    return;
                }

                Find.Selector.Select(podThing);
                int selectedCount = GetSelectedPodCount();
                TolkHelper.Speak("RimWorldAccess.TransportPods.Selection.PodSelected".Loc(selectedCount), SpeechPriority.Normal);
            }
        }

        private static int GetSelectedPodCount()
        {
            int count = 0;
            foreach (object obj in Find.Selector.SelectedObjects)
            {
                if (obj is ThingWithComps thing && thing.TryGetComp<CompTransporter>() != null)
                    count++;
            }
            return count;
        }

        /// <summary>
        /// Confirms the selection and opens the loading dialog through the game's own gizmo. Clears
        /// IsActive BEFORE invoking it, so this scope is already popped by the time the dialog's own
        /// scope takes over and no coexistence handling is needed.
        /// </summary>
        internal static void ConfirmSelection()
        {
            int selectedCount = GetSelectedPodCount();

            if (selectedCount == 0)
            {
                TolkHelper.Speak("RimWorldAccess.TransportPods.Selection.NoPodsSelected".Loc(), SpeechPriority.High);
                return;
            }

            CompTransporter firstTransporter = null;
            foreach (object obj in Find.Selector.SelectedObjects)
            {
                if (obj is ThingWithComps thing)
                {
                    var transporter = thing.TryGetComp<CompTransporter>();
                    if (transporter != null)
                    {
                        firstTransporter = transporter;
                        break;
                    }
                }
            }

            if (firstTransporter == null)
            {
                TolkHelper.Speak("RimWorldAccess.TransportPods.Selection.PodsUnavailable".Loc(), SpeechPriority.High);
                Close();
                return;
            }

            IsActive = false;
            currentMap = null;
            // Both exit paths clear sourcePod and groupablePods rather than leaving them for the
            // next Open().
            sourcePod = null;
            groupablePods = null;

            Command_LoadToTransporter loadCommand = null;
            foreach (var gizmo in firstTransporter.CompGetGizmosExtra())
            {
                if (gizmo is Command_LoadToTransporter cmd)
                {
                    loadCommand = cmd;
                    break;
                }
            }

            if (loadCommand == null)
            {
                TolkHelper.Speak("RimWorldAccess.TransportPods.Selection.LoadCommandMissingMayBeLoading".Loc(), SpeechPriority.High);
                Find.Selector.ClearSelection();
                return;
            }

            // InheritInteractionsFrom populates the gizmo's transporters list, which the gizmo grid
            // would normally do.
            foreach (object obj in Find.Selector.SelectedObjects)
            {
                if (obj is ThingWithComps thing && thing != firstTransporter.parent)
                {
                    var otherTransporter = thing.TryGetComp<CompTransporter>();
                    if (otherTransporter != null)
                    {
                        foreach (var otherGizmo in otherTransporter.CompGetGizmosExtra())
                        {
                            if (otherGizmo is Command_LoadToTransporter otherLoadCmd)
                            {
                                loadCommand.InheritInteractionsFrom(otherLoadCmd);
                                break;
                            }
                        }
                    }
                }
            }

            loadCommand.ProcessInput(null);
        }

        /// <summary>The currently selected transporters, from the game's own selector.</summary>
        public static IEnumerable<CompTransporter> GetSelectedTransporters()
        {
            if (!IsActive)
                yield break;

            foreach (object obj in Find.Selector.SelectedObjects)
            {
                if (obj is ThingWithComps thing)
                {
                    var transporter = thing.TryGetComp<CompTransporter>();
                    if (transporter != null)
                        yield return transporter;
                }
            }
        }
    }
}
