using System;
using System.Collections.Generic;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace RimWorldAccess
{
    /// <summary>
    /// The numeric quantity chooser, opened with Enter on a transfer row and navigable by
    /// increment or by typing a number. Every adjustment commits immediately through the
    /// caller's transferable, as dragging the row's slider does; closing commits nothing.
    /// Two step modes: the one-way ladder (0, 1, 2, 5, 10...) for the caravan screens, and the
    /// signed step-by-one mode trade opens, where Up is one more of the row's primary action,
    /// Shift/Ctrl step ten and a hundred, and a typed number may lead with - (sell) or + (buy).
    /// </summary>
    public static class QuantityMenuState
    {
        private static bool isActive = false;
        private static Transferable currentTransferable;
        private static List<int> quantityIncrements = new List<int>();

        // The one-way path uses [0, MaxCount] with the built-in mass/nutrition announcement;
        // trade opens a SIGNED range ([min sell, max buy]) with its own describe callback.
        private static Func<int> minProvider;
        private static Func<int> maxProvider;
        private static Func<int, string> describeQuantity;
        private static string currentItemLabel = "";

        // Step-by-one mode: +1 when Up means buying more, -1 when it means selling more; 0 = ladder.
        private static int primaryDirection;
        private static Func<int, string> signPrompt;

        /// <summary>
        /// Bounds are read through the providers on every use: items can despawn or change
        /// count while the menu is open.
        /// </summary>
        private static int MaxQuantity => maxProvider?.Invoke() ?? 0;
        private static int MinQuantity => minProvider?.Invoke() ?? 0;
        private static int selectedIncrementIndex = 0;
        private static TypeaheadSearchHelper typeahead = new TypeaheadSearchHelper();
        private static Action<int> onChanged;

        /// <summary>The live count, read straight off the transferable — this menu buffers nothing.</summary>
        private static int SelectedQuantity => currentTransferable?.CountToTransfer ?? 0;

        // Multi-digit number entry, separate from typeahead.
        private static string numericBuffer = "";
        private static float lastNumericInputTime = 0f;
        private const float NUMERIC_INPUT_TIMEOUT = 10.0f;

        public static bool IsActive => isActive;

        /// <summary>The transferable being edited, for the visual focus ring.</summary>
        internal static Transferable CurrentTransferable => currentTransferable;

        public static TypeaheadSearchHelper Typeahead => typeahead;

        public static bool HasActiveNumericInput => !string.IsNullOrEmpty(numericBuffer);

        /// <summary>Whether a typed - or + starts a signed number (step-by-one mode over a signed range).</summary>
        public static bool AcceptsSign => isActive && primaryDirection != 0 && MinQuantity < 0;

        /// <summary>
        /// Opens the menu over [0, MaxCount]. <paramref name="onChanged"/> is called on every
        /// adjustment with the requested quantity.
        /// </summary>
        public static void Open(TransferableOneWay transferable, Action<int> onChanged)
        {
            if (!GuardHelper.RequireItem(transferable)) return;
            OpenRange(transferable,
                () => 0,
                () => transferable.MaxCount,
                describe: null,
                onChanged,
                transferable.LabelCap.StripTags());
        }

        /// <summary>
        /// Opens the menu over an arbitrary, possibly signed range.
        /// <paramref name="describe"/> builds the leading announcement fragment for a
        /// candidate quantity (null = the built-in mass/nutrition text); the position
        /// fragment is appended either way.
        /// </summary>
        public static void OpenRange(Transferable transferable, Func<int> getMin, Func<int> getMax,
            Func<int, string> describe, Action<int> onChanged, string itemLabel,
            int primaryDirection = 0, Func<int, string> signPrompt = null)
        {
            if (!GuardHelper.RequireItem(transferable)) return;

            currentTransferable = transferable;
            minProvider = getMin;
            maxProvider = getMax;
            describeQuantity = describe;
            currentItemLabel = itemLabel ?? "";
            QuantityMenuState.onChanged = onChanged;
            QuantityMenuState.primaryDirection = Math.Sign(primaryDirection);
            QuantityMenuState.signPrompt = signPrompt;

            // Increments are fixed at open; clamping still goes through Min/MaxQuantity.
            quantityIncrements = GenerateIncrements(MinQuantity, MaxQuantity);

            selectedIncrementIndex = FindClosestIncrementIndex(SelectedQuantity);

            isActive = true;
            typeahead.ClearSearch();
            numericBuffer = "";
            SoundDefOf.Click.PlayOneShotOnCamera();

            TolkHelper.Speak("RimWorldAccess.UI.Quantity.Opened".Loc(currentItemLabel));
            AnnounceCurrentQuantity();
        }

        /// <summary>
        /// Closes the menu and releases the caller's hooks. Silent, so it is safe as a
        /// StateResetRegistry entry.
        /// </summary>
        public static void Close()
        {
            isActive = false;
            currentTransferable = null;
            onChanged = null;
            minProvider = null;
            maxProvider = null;
            describeQuantity = null;
            currentItemLabel = "";
            primaryDirection = 0;
            signPrompt = null;
            numericBuffer = "";
            typeahead.ClearSearch();
        }

        /// <summary>
        /// Commits one step through the caller's gated vehicle, then re-derives the cursor from
        /// what the transferable actually took, so a clamped or refused write leaves the cursor
        /// put and the announcement repeats the unchanged number.
        /// </summary>
        private static void SetQuantity(int requested)
        {
            onChanged?.Invoke(requested);
            selectedIncrementIndex = FindClosestIncrementIndex(SelectedQuantity);
        }

        /// <summary>
        /// Enter and Escape both close: every step already went into the transferable, so there
        /// is nothing to confirm and nothing to discard.
        /// </summary>
        public static void Dismiss()
        {
            if (!isActive) return;
            Close();
            SoundDefOf.Click.PlayOneShotOnCamera();
        }

        /// <summary>
        /// Moves to the next increment, or the next typeahead match while a search is live.
        /// </summary>
        public static void SelectNext()
        {
            if (primaryDirection != 0)
            {
                StepBy(1);
                return;
            }
            if (!isActive || quantityIncrements.Count == 0)
                return;

            if (typeahead.HasActiveSearch && !typeahead.HasNoMatches)
            {
                var labels = GetQuantityLabels();
                int newIndex = typeahead.GetNextMatch(selectedIncrementIndex);
                if (newIndex >= 0 && newIndex < quantityIncrements.Count)
                {
                    selectedIncrementIndex = newIndex;
                    SetQuantity(quantityIncrements[newIndex]);
                    AnnounceWithSearch();
                }
                return;
            }

            if (selectedIncrementIndex < quantityIncrements.Count - 1)
            {
                selectedIncrementIndex++;
                SetQuantity(quantityIncrements[selectedIncrementIndex]);
                SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                AnnounceCurrentQuantity();
            }
            else
            {
                TolkHelper.Speak("RimWorldAccess.UI.Quantity.Maximum".Loc());
            }
        }

        /// <summary>
        /// Moves to the previous increment, or the previous typeahead match while a search is live.
        /// </summary>
        public static void SelectPrevious()
        {
            if (primaryDirection != 0)
            {
                StepBy(-1);
                return;
            }
            if (!isActive || quantityIncrements.Count == 0)
                return;

            if (typeahead.HasActiveSearch && !typeahead.HasNoMatches)
            {
                var labels = GetQuantityLabels();
                int newIndex = typeahead.GetPreviousMatch(selectedIncrementIndex);
                if (newIndex >= 0 && newIndex < quantityIncrements.Count)
                {
                    selectedIncrementIndex = newIndex;
                    SetQuantity(quantityIncrements[newIndex]);
                    AnnounceWithSearch();
                }
                return;
            }

            if (selectedIncrementIndex > 0)
            {
                selectedIncrementIndex--;
                SetQuantity(quantityIncrements[selectedIncrementIndex]);
                SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                AnnounceCurrentQuantity();
            }
            else
            {
                TolkHelper.Speak("RimWorldAccess.UI.Quantity.Minimum".Loc());
            }
        }

        /// <summary><paramref name="steps"/> units in the primary direction (step-by-one mode), else one ladder rung; past either end speaks the bound.</summary>
        public static void StepBy(int steps)
        {
            if (!isActive)
                return;
            if (primaryDirection == 0)
            {
                if (steps > 0) SelectNext(); else SelectPrevious();
                return;
            }
            int target = Mathf.Clamp(SelectedQuantity + steps * primaryDirection, MinQuantity, MaxQuantity);
            if (target == SelectedQuantity)
            {
                TolkHelper.Speak((steps * primaryDirection > 0
                    ? "RimWorldAccess.UI.Quantity.Maximum"
                    : "RimWorldAccess.UI.Quantity.Minimum").Loc());
                return;
            }
            typeahead.ClearSearch();
            numericBuffer = "";
            SetQuantity(target);
            SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
            AnnounceCurrentQuantity();
        }

        public static void JumpToMax()
        {
            if (!isActive || quantityIncrements.Count == 0)
                return;

            selectedIncrementIndex = quantityIncrements.Count - 1;
            SetQuantity(quantityIncrements[selectedIncrementIndex]);
            typeahead.ClearSearch();
            SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
            AnnounceCurrentQuantity();
        }

        public static void JumpToMin()
        {
            if (!isActive || quantityIncrements.Count == 0)
                return;

            selectedIncrementIndex = 0;
            SetQuantity(quantityIncrements[0]);
            typeahead.ClearSearch();
            SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
            AnnounceCurrentQuantity();
        }

        /// <summary>
        /// Accumulates a typed digit into the multi-digit numeric buffer and jumps to it.
        /// </summary>
        public static void HandleTypeahead(char c)
        {
            if (!isActive)
                return;

            float currentTime = Time.realtimeSinceStartup;

            if (currentTime - lastNumericInputTime > NUMERIC_INPUT_TIMEOUT)
            {
                numericBuffer = "";
            }
            lastNumericInputTime = currentTime;

            if ((c == '-' || c == '+') && AcceptsSign && numericBuffer.Length == 0)
            {
                numericBuffer = c.ToString();
                string prompt = signPrompt != null ? signPrompt(c == '-' ? -1 : 1) : null;
                TolkHelper.SpeakData(prompt ?? "RimWorldAccess.UI.Quantity.TypingFragment".Translate(numericBuffer).ToString());
                return;
            }
            numericBuffer += c;

            if (int.TryParse(numericBuffer, out int targetQty))
            {
                ApplyTypedQuantity(targetQty);
                AnnounceWithBuffer();
            }
            else if (numericBuffer == "-" || numericBuffer == "+")
            {
                AnnounceWithBuffer();
            }
            else
            {
                TolkHelper.Speak("RimWorldAccess.UI.Quantity.InvalidNumber".Loc(numericBuffer));
                numericBuffer = "";
            }
        }

        /// <summary>An unsigned typed number follows the primary direction (step-by-one mode) or the current sign (ladder); an explicit sign is honored as typed.</summary>
        private static void ApplyTypedQuantity(int rawQty)
        {
            bool explicitSign = numericBuffer.StartsWith("-") || numericBuffer.StartsWith("+");
            int targetQty = rawQty;
            if (!explicitSign)
            {
                if (primaryDirection < 0 || (primaryDirection == 0 && SelectedQuantity < 0))
                {
                    targetQty = -Math.Abs(rawQty);
                }
            }
            targetQty = Mathf.Clamp(targetQty, MinQuantity, MaxQuantity);
            SetQuantity(targetQty);
        }

        /// <summary>
        /// Drops the last digit of the numeric buffer and re-applies what remains.
        /// </summary>
        public static void HandleBackspace()
        {
            if (!isActive || string.IsNullOrEmpty(numericBuffer))
                return;

            numericBuffer = numericBuffer.Substring(0, numericBuffer.Length - 1);

            if (string.IsNullOrEmpty(numericBuffer))
            {
                AnnounceCurrentQuantity();
            }
            else
            {
                if (int.TryParse(numericBuffer, out int targetQty))
                {
                    ApplyTypedQuantity(targetQty);
                }
                AnnounceWithBuffer();
            }
        }

        /// <summary>
        /// Increments spanning [min, max]: a {0, 1, 2, 5, 10, ...max} ladder, mirrored onto
        /// the negative side for signed ranges so Up/Down walk max-sell through zero to max-buy.
        /// </summary>
        private static List<int> GenerateIncrements(int min, int max)
        {
            var increments = new List<int> { 0 };
            AddSideIncrements(increments, max, 1);
            AddSideIncrements(increments, -min, -1);
            increments.Sort();
            return increments;
        }

        private static void AddSideIncrements(List<int> increments, int magnitude, int sign)
        {
            if (magnitude <= 0)
                return;
            int[] steps = { 1, 2, 5, 10, 25, 50, 100, 250, 500, 1000, 2500, 5000, 10000 };
            foreach (int step in steps)
            {
                if (step < magnitude && !increments.Contains(sign * step))
                {
                    increments.Add(sign * step);
                }
            }
            if (!increments.Contains(sign * magnitude))
            {
                increments.Add(sign * magnitude);
            }
        }

        /// <summary>
        /// The index of the increment nearest <paramref name="quantity"/>.
        /// </summary>
        private static int FindClosestIncrementIndex(int quantity)
        {
            if (quantityIncrements.Count == 0)
                return 0;

            int exactIndex = quantityIncrements.IndexOf(quantity);
            if (exactIndex >= 0)
                return exactIndex;

            int closestIndex = 0;
            int closestDiff = int.MaxValue;

            for (int i = 0; i < quantityIncrements.Count; i++)
            {
                int diff = Math.Abs(quantityIncrements[i] - quantity);
                if (diff < closestDiff)
                {
                    closestDiff = diff;
                    closestIndex = i;
                }
            }

            return closestIndex;
        }

        /// <summary>
        /// The increments as strings, which is what typeahead searches.
        /// </summary>
        private static List<string> GetQuantityLabels()
        {
            var labels = new List<string>();
            foreach (int qty in quantityIncrements)
            {
                labels.Add(qty.ToString());
            }
            return labels;
        }

        private static void AnnounceCurrentQuantity()
        {
            if (currentTransferable == null)
                return;

            string announcement = BuildQuantityAnnouncement(SelectedQuantity);
            TolkHelper.SpeakData(announcement);
        }

        /// <summary>
        /// The quantity announcement plus the live typeahead buffer.
        /// </summary>
        private static void AnnounceWithSearch()
        {
            if (currentTransferable == null)
                return;

            StringBuilder sb = new StringBuilder();
            sb.Append(BuildQuantityAnnouncement(SelectedQuantity));
            if (typeahead.HasActiveSearch)
            {
                sb.Append(" ");
                sb.Append("RimWorldAccess.UI.Quantity.TypingFragment".Translate(typeahead.SearchBuffer));
            }

            TolkHelper.SpeakData(sb.ToString());
        }

        /// <summary>
        /// The quantity announcement plus the numeric buffer, with no position fragment: a
        /// typed quantity need not be one of the increments.
        /// </summary>
        private static void AnnounceWithBuffer()
        {
            if (currentTransferable == null)
                return;

            StringBuilder sb = new StringBuilder();
            sb.Append(BuildQuantityFragment(SelectedQuantity));

            if (!string.IsNullOrEmpty(numericBuffer))
            {
                sb.Append(". ");
                sb.Append("RimWorldAccess.UI.Quantity.TypingFragment".Translate(numericBuffer));
            }

            TolkHelper.SpeakData(sb.ToString());
        }

        /// <summary>
        /// The full announcement for a quantity: "5. Mass: 2.5 kilograms. Nutrition: 0.25.
        /// Position 3 of 8", or the describe callback's phrasing followed by the position.
        /// </summary>
        private static string BuildQuantityAnnouncement(int quantity)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append(BuildQuantityFragment(quantity));
            if (primaryDirection != 0)
            {
                // Step-by-one mode: every integer is reachable, so a ladder position means nothing.
                return sb.ToString();
            }
            sb.Append(". ");
            sb.Append("RimWorldAccess.UI.Quantity.PositionFragment".Translate(
                selectedIncrementIndex + 1, quantityIncrements.Count));
            return sb.ToString();
        }

        /// <summary>
        /// The quantity's own fragment: the caller's describe callback when supplied, else the
        /// built-in count, mass, and nutrition line.
        /// </summary>
        private static string BuildQuantityFragment(int quantity)
        {
            if (describeQuantity != null)
            {
                return describeQuantity(quantity);
            }

            StringBuilder sb = new StringBuilder();
            sb.Append(quantity.ToString());

            float massPerItem = currentTransferable.ThingDef.BaseMass;
            sb.Append(". ");
            sb.Append("RimWorldAccess.UI.Quantity.MassFragment".Translate((massPerItem * quantity).ToString("F2")));

            if (currentTransferable.ThingDef.IsNutritionGivingIngestible)
            {
                float nutritionPerItem = currentTransferable.ThingDef.ingestible.CachedNutrition;
                sb.Append(". ");
                sb.Append("RimWorldAccess.UI.Quantity.NutritionFragment".Translate((nutritionPerItem * quantity).ToString("F2")));
            }

            return sb.ToString();
        }

        // Key handling lives on QuantityMenuScope, which routes layout-resolved characters
        // through its CharSink straight to HandleTypeahead.
    }
}
