using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Shared reflection-driven handler for the Gizmo_ModifyNumber&lt;T&gt; family: a
    /// bare Gizmo (not a Command) with a protected Title, protected ButtonUp/ButtonDown/
    /// ButtonCenter, and a protected T[] selection -- the exact same members, same
    /// shape, same body, duplicated VERBATIM between Hospitality's
    /// Gizmo_GuestBed/Gizmo_VendingMachine/Gizmo_VendingMachineContent and
    /// CashRegister's Gizmo_Radius (two different assemblies, no shared base type we
    /// can reference). Written entirely against the shape via reflection so one
    /// instance is registered for all four concrete types; lives in the Hospitality
    /// family because Hospitality owns three of the four, and CashRegisterCompat
    /// references it for its one. GizmoOnGUI never returns GizmoState.Clicked and no
    /// subclass overrides ProcessInput, so GizmoHandlerRegistry.HasActivation correctly
    /// reports these as read-only status displays: the slider-adjustment session
    /// (TryGetSliderAdapter) and the extra centre-button option are the only ways to
    /// change them, matching what a sighted player's mouse can do (drag the up/down
    /// buttons, click the centre one).
    ///
    /// Per-type model reads (status text, slider bounds, the centre button's spoken
    /// purpose) are the one part that cannot be shared, since each subclass wraps a
    /// different comp/building with different members -- dispatched by
    /// GetType().FullName.
    /// </summary>
    internal sealed class ModifyNumberGizmoHandler : GizmoHandlerBase
    {
        private sealed class Descriptor
        {
            public PropertyInfo TitleProperty;
            public MethodInfo ButtonUpMethod;
            public MethodInfo ButtonDownMethod;
            public MethodInfo ButtonCenterMethod;
            public FieldInfo SelectionField;
        }

        private static readonly Dictionary<Type, Descriptor> descriptors = new Dictionary<Type, Descriptor>();

        private static Descriptor GetDescriptor(Type gizmoType)
        {
            if (descriptors.TryGetValue(gizmoType, out Descriptor cached))
            {
                return cached;
            }

            Descriptor descriptor = new Descriptor
            {
                TitleProperty = AccessTools.Property(gizmoType, "Title"),
                ButtonUpMethod = AccessTools.Method(gizmoType, "ButtonUp"),
                ButtonDownMethod = AccessTools.Method(gizmoType, "ButtonDown"),
                ButtonCenterMethod = AccessTools.Method(gizmoType, "ButtonCenter"),
                SelectionField = AccessTools.Field(gizmoType, "selection"),
            };
            descriptors[gizmoType] = descriptor;
            return descriptor;
        }

        private static Array GetSelectionArray(Gizmo gizmo, Descriptor descriptor)
        {
            return descriptor.SelectionField?.GetValue(gizmo) as Array;
        }

        private static object GetFirstSelected(Gizmo gizmo, Descriptor descriptor)
        {
            Array selection = GetSelectionArray(gizmo, descriptor);
            return selection != null && selection.Length > 0 ? selection.GetValue(0) : null;
        }

        private static bool TryGetInt(object target, string propertyName, out int value)
        {
            value = 0;
            if (target == null)
            {
                return false;
            }

            PropertyInfo property = AccessTools.Property(target.GetType(), propertyName);
            object raw = property?.GetValue(target, null);
            if (raw == null)
            {
                return false;
            }

            value = Convert.ToInt32(raw);
            return true;
        }

        private static bool TryGetFloat(object target, string propertyName, out float value)
        {
            value = 0f;
            if (target == null)
            {
                return false;
            }

            PropertyInfo property = AccessTools.Property(target.GetType(), propertyName);
            object raw = property?.GetValue(target, null);
            if (raw == null)
            {
                return false;
            }

            value = Convert.ToSingle(raw);
            return true;
        }

        private static object GetVendingMachine(Gizmo gizmo)
        {
            return AccessTools.Field(gizmo.GetType(), "vendingMachine")?.GetValue(gizmo);
        }

        private static float GetVendingPriceSteps(object vendingMachine)
        {
            PropertyInfo propertiesProperty = AccessTools.Property(vendingMachine.GetType(), "Properties");
            object properties = propertiesProperty?.GetValue(vendingMachine, null);
            FieldInfo stepsField = properties != null ? AccessTools.Field(properties.GetType(), "priceSteps") : null;
            return stepsField != null ? Convert.ToSingle(stepsField.GetValue(properties)) : 5f;
        }

        private static string GetIncludeRegionText(Gizmo gizmo, Descriptor descriptor)
        {
            Array selection = GetSelectionArray(gizmo, descriptor);
            if (selection == null || selection.Length == 0)
            {
                return "-";
            }

            bool sawAny = false;
            bool allTrue = true;
            bool anyTrue = false;
            foreach (object register in selection)
            {
                if (!TryGetBool(register, "IncludeRegion", out bool included))
                {
                    return "-";
                }

                sawAny = true;
                if (included)
                {
                    anyTrue = true;
                }
                else
                {
                    allTrue = false;
                }
            }

            if (!sawAny)
            {
                return "-";
            }

            // Mirrors Gizmo_Radius.GetIncludeRegionString(): all true -> Yes, all false ->
            // No, mixed -> "-".
            if (allTrue)
            {
                return "Yes".Translate();
            }

            return anyTrue ? "-" : (string)"No".Translate();
        }

        private static bool TryGetBool(object target, string propertyName, out bool value)
        {
            value = false;
            if (target == null)
            {
                return false;
            }

            PropertyInfo property = AccessTools.Property(target.GetType(), propertyName);
            object raw = property?.GetValue(target, null);
            if (raw == null)
            {
                return false;
            }

            value = Convert.ToBoolean(raw);
            return true;
        }

        public override bool TryGetLabel(Gizmo gizmo, out string label)
        {
            label = null;
            try
            {
                Descriptor descriptor = GetDescriptor(gizmo.GetType());
                string title = descriptor.TitleProperty?.GetValue(gizmo, null)?.ToString();
                if (string.IsNullOrEmpty(title))
                {
                    return false;
                }

                label = title;
                return true;
            }
            catch (Exception ex)
            {
                ModLogger.Error($"ModifyNumberGizmoHandler.TryGetLabel failed: {ex.Message}");
                return false;
            }
        }

        public override bool TryGetStatus(Gizmo gizmo, out string status)
        {
            status = null;
            try
            {
                Descriptor descriptor = GetDescriptor(gizmo.GetType());
                switch (gizmo.GetType().FullName)
                {
                    case "Hospitality.Gizmo_GuestBed":
                        return TryGetBedStatus(gizmo, out status);
                    case "Hospitality.Gizmo_VendingMachine":
                        return TryGetVendingPriceStatus(gizmo, out status);
                    case "Hospitality.Gizmo_VendingMachineContent":
                        return TryGetVendingContentStatus(gizmo, out status);
                    case "CashRegister.Gizmo_Radius":
                        return TryGetRadiusStatus(gizmo, descriptor, out status);
                    default:
                        return false;
                }
            }
            catch (Exception ex)
            {
                ModLogger.Error($"ModifyNumberGizmoHandler.TryGetStatus failed: {ex.Message}");
                return false;
            }
        }

        private static bool TryGetBedStatus(Gizmo gizmo, out string status)
        {
            status = null;
            // Gizmo_GuestBed pre-formats these two TaggedString fields in its own
            // constructor (a single bed's own value, or a "min - max" range across a
            // multi-select) -- reading them directly gets the mod's exact wording with no
            // duplicated range-formatting logic of our own.
            FieldInfo feeField = AccessTools.Field(gizmo.GetType(), "rentalFee");
            FieldInfo attractivenessField = AccessTools.Field(gizmo.GetType(), "attractiveness");
            string fee = feeField?.GetValue(gizmo)?.ToString();
            string attractiveness = attractivenessField?.GetValue(gizmo)?.ToString();
            if (string.IsNullOrEmpty(fee) || string.IsNullOrEmpty(attractiveness))
            {
                return false;
            }

            status = "RimWorldAccess.Compat.Hospitality.BedFeeStatus".Translate(fee, attractiveness);
            return true;
        }

        private static bool TryGetVendingPriceStatus(Gizmo gizmo, out string status)
        {
            status = null;
            object vendingMachine = GetVendingMachine(gizmo);
            if (!TryGetInt(vendingMachine, "CurrentPrice", out int price))
            {
                return false;
            }

            status = "RimWorldAccess.Compat.Hospitality.VendingPriceStatus".Translate(((float)price).ToStringMoney("F0"));
            return true;
        }

        private static bool TryGetVendingContentStatus(Gizmo gizmo, out string status)
        {
            status = null;
            object vendingMachine = GetVendingMachine(gizmo);
            if (!TryGetInt(vendingMachine, "CurrentEmptyThreshold", out int threshold)
                || !TryGetInt(vendingMachine, "TotalSold", out int totalSold))
            {
                return false;
            }

            status = "RimWorldAccess.Compat.Hospitality.VendingContentStatus".Translate(
                ((float)threshold).ToStringMoney("F0"), ((float)totalSold).ToStringMoney("F0"));
            return true;
        }

        private static bool TryGetRadiusStatus(Gizmo gizmo, Descriptor descriptor, out string status)
        {
            status = null;
            object register = GetFirstSelected(gizmo, descriptor);
            if (!TryGetFloat(register, "Radius", out float radius))
            {
                return false;
            }

            float infiniteRadius = GetInfiniteRadius(register);
            string radiusText = radius >= infiniteRadius ? (string)"Infinite".Translate() : radius.ToString("N0");
            string includeText = GetIncludeRegionText(gizmo, descriptor);
            status = "RimWorldAccess.Compat.CashRegister.RadiusStatus".Translate(radiusText, includeText);
            return true;
        }

        private static float GetInfiniteRadius(object register)
        {
            FieldInfo field = register != null ? AccessTools.Field(register.GetType(), "InfiniteRadius") : null;
            return field != null ? Convert.ToSingle(field.GetValue(null)) : 46f;
        }

        public override bool TryGetSliderAdapter(Gizmo gizmo, out GizmoSliderAdapter adapter)
        {
            adapter = null;
            try
            {
                Descriptor descriptor = GetDescriptor(gizmo.GetType());
                if (descriptor.ButtonUpMethod == null || descriptor.ButtonDownMethod == null)
                {
                    return false;
                }

                switch (gizmo.GetType().FullName)
                {
                    case "Hospitality.Gizmo_GuestBed":
                        return TryGetBedSliderAdapter(gizmo, descriptor, out adapter);
                    case "Hospitality.Gizmo_VendingMachine":
                        return TryGetVendingPriceSliderAdapter(gizmo, descriptor, out adapter);
                    case "Hospitality.Gizmo_VendingMachineContent":
                        return TryGetVendingContentSliderAdapter(gizmo, descriptor, out adapter);
                    case "CashRegister.Gizmo_Radius":
                        return TryGetRadiusSliderAdapter(gizmo, descriptor, out adapter);
                    default:
                        return false;
                }
            }
            catch (Exception ex)
            {
                ModLogger.Error($"ModifyNumberGizmoHandler.TryGetSliderAdapter failed: {ex.Message}");
                return false;
            }
        }

        private static bool TryGetBedSliderAdapter(Gizmo gizmo, Descriptor descriptor, out GizmoSliderAdapter adapter)
        {
            adapter = null;
            // A single bed's fee is well-defined; a multi-select's isn't (the mod itself
            // only ever speaks a "min - max" range for that case), so decline rather than
            // report a misleading single value.
            Array selection = GetSelectionArray(gizmo, descriptor);
            if (selection == null || selection.Length != 1)
            {
                return false;
            }

            object bed = selection.GetValue(0);
            if (!TryGetInt(bed, "RentalFee", out int fee))
            {
                return false;
            }

            FieldInfo feeStepField = AccessTools.Field(bed.GetType(), "FeeStep");
            float step = feeStepField != null ? Convert.ToSingle(feeStepField.GetValue(null)) : 5f;
            float capturedValue = fee;
            MethodInfo buttonUp = descriptor.ButtonUpMethod;
            MethodInfo buttonDown = descriptor.ButtonDownMethod;

            adapter = new GizmoSliderAdapter
            {
                Title = "RimWorldAccess.Compat.Hospitality.BedRentalFeeTitle".Translate(),
                Value = capturedValue,
                // Building_GuestBed.SetRentalFee clamps to [0, int.MaxValue] -- genuinely
                // unbounded, so this is a sane display ceiling, not the real clamp.
                Min = 0f,
                Max = 100000f,
                Step = step,
                DescribeValue = v => ((float)v).ToStringMoney("F0"),
                Write = newValue => InvokeStep(gizmo, buttonUp, buttonDown, capturedValue, newValue),
            };
            return true;
        }

        private static bool TryGetVendingPriceSliderAdapter(Gizmo gizmo, Descriptor descriptor, out GizmoSliderAdapter adapter)
        {
            adapter = null;
            object vendingMachine = GetVendingMachine(gizmo);
            if (!TryGetInt(vendingMachine, "CurrentPrice", out int price))
            {
                return false;
            }

            float step = GetVendingPriceSteps(vendingMachine);
            float capturedValue = price;
            MethodInfo buttonUp = descriptor.ButtonUpMethod;
            MethodInfo buttonDown = descriptor.ButtonDownMethod;

            adapter = new GizmoSliderAdapter
            {
                Title = "RimWorldAccess.Compat.Hospitality.VendingPriceTitle".Translate(),
                Value = capturedValue,
                // CompVendingMachine.SetPrice clamps to [0, int.MaxValue].
                Min = 0f,
                Max = 100000f,
                Step = step,
                DescribeValue = v => ((float)v).ToStringMoney("F0"),
                Write = newValue => InvokeStep(gizmo, buttonUp, buttonDown, capturedValue, newValue),
            };
            return true;
        }

        private static bool TryGetVendingContentSliderAdapter(Gizmo gizmo, Descriptor descriptor, out GizmoSliderAdapter adapter)
        {
            adapter = null;
            object vendingMachine = GetVendingMachine(gizmo);
            if (!TryGetInt(vendingMachine, "CurrentEmptyThreshold", out int threshold))
            {
                return false;
            }

            float step = GetVendingPriceSteps(vendingMachine);
            float capturedValue = threshold;
            MethodInfo buttonUp = descriptor.ButtonUpMethod;
            MethodInfo buttonDown = descriptor.ButtonDownMethod;

            adapter = new GizmoSliderAdapter
            {
                Title = "RimWorldAccess.Compat.Hospitality.VendingThresholdTitle".Translate(),
                Value = capturedValue,
                // CompVendingMachine.SetEmptyThreshold clamps to [0, int.MaxValue].
                Min = 0f,
                Max = 100000f,
                Step = step,
                DescribeValue = v => ((float)v).ToStringMoney("F0"),
                Write = newValue => InvokeStep(gizmo, buttonUp, buttonDown, capturedValue, newValue),
            };
            return true;
        }

        private static bool TryGetRadiusSliderAdapter(Gizmo gizmo, Descriptor descriptor, out GizmoSliderAdapter adapter)
        {
            adapter = null;
            object register = GetFirstSelected(gizmo, descriptor);
            if (!TryGetFloat(register, "Radius", out float radius))
            {
                return false;
            }

            FieldInfo stepField = AccessTools.Field(register.GetType(), "RadiusStep");
            float step = stepField != null ? Convert.ToSingle(stepField.GetValue(null)) : 3f;
            float infiniteRadius = GetInfiniteRadius(register);
            float capturedValue = radius;
            MethodInfo buttonUp = descriptor.ButtonUpMethod;
            MethodInfo buttonDown = descriptor.ButtonDownMethod;

            adapter = new GizmoSliderAdapter
            {
                Title = "RimWorldAccess.Compat.CashRegister.RadiusTitle".Translate(),
                Value = capturedValue,
                Min = 0f,
                // Building_CashRegister.ChangeRadius clamps to [0, InfiniteRadius - 1] --
                // the mod's own real ceiling, not a guessed one.
                Max = infiniteRadius - 1f,
                Step = step,
                DescribeValue = v => v >= infiniteRadius - 1f ? (string)"Infinite".Translate() : v.ToString("N0"),
                Write = newValue => InvokeStep(gizmo, buttonUp, buttonDown, capturedValue, newValue),
            };
            return true;
        }

        /// <summary>
        /// Steps the gizmo exactly once via its OWN ButtonUp/ButtonDown -- the mod's own
        /// mutation vehicle (Category A) -- rather than writing any field ourselves. The
        /// adjustment session always requests exactly one Step away from the value this
        /// adapter was built with, so a single directional call reproduces it precisely.
        /// </summary>
        private static void InvokeStep(Gizmo gizmo, MethodInfo buttonUp, MethodInfo buttonDown, float capturedValue, float newValue)
        {
            try
            {
                if (newValue > capturedValue)
                {
                    buttonUp.Invoke(gizmo, null);
                }
                else if (newValue < capturedValue)
                {
                    buttonDown.Invoke(gizmo, null);
                }
            }
            catch (Exception ex)
            {
                ModLogger.Error($"ModifyNumberGizmoHandler step invoke failed: {ex.Message}");
            }
        }

        public override bool TryGetExtraOptions(Gizmo gizmo, List<FloatMenuOption> options)
        {
            try
            {
                Descriptor descriptor = GetDescriptor(gizmo.GetType());
                if (descriptor.ButtonCenterMethod == null)
                {
                    return false;
                }

                string label;
                switch (gizmo.GetType().FullName)
                {
                    case "Hospitality.Gizmo_GuestBed":
                        label = "RimWorldAccess.Compat.Hospitality.ResetRentalFee".Translate();
                        break;
                    case "Hospitality.Gizmo_VendingMachine":
                        label = "RimWorldAccess.Compat.Hospitality.SetAutoPrice".Translate();
                        break;
                    case "Hospitality.Gizmo_VendingMachineContent":
                        label = "RimWorldAccess.Compat.Hospitality.SetThresholdAuto".Translate();
                        break;
                    case "CashRegister.Gizmo_Radius":
                        label = "RimWorldAccess.Compat.CashRegister.ToggleIncludeRoom".Translate();
                        break;
                    default:
                        return false;
                }

                MethodInfo buttonCenter = descriptor.ButtonCenterMethod;
                options.Add(new FloatMenuOption(label, () =>
                {
                    try
                    {
                        buttonCenter.Invoke(gizmo, null);
                    }
                    catch (Exception ex)
                    {
                        ModLogger.Error($"ModifyNumberGizmoHandler ButtonCenter invoke failed: {ex.Message}");
                    }
                }));
                return true;
            }
            catch (Exception ex)
            {
                ModLogger.Error($"ModifyNumberGizmoHandler.TryGetExtraOptions failed: {ex.Message}");
                return false;
            }
        }

        public override bool TryGetDescription(Gizmo gizmo, out string description)
        {
            description = null;
            if (gizmo.GetType().FullName != "Hospitality.Gizmo_GuestBed" || !ModsConfig.RoyaltyActive)
            {
                return false;
            }

            try
            {
                Descriptor descriptor = GetDescriptor(gizmo.GetType());
                Array selection = GetSelectionArray(gizmo, descriptor);
                // Mirrors Gizmo_GuestBed.DrawTooltipBox's own `if (selection.Length > 1)
                // return;` -- the royal-title box only ever covers a single bed.
                if (selection == null || selection.Length != 1)
                {
                    return false;
                }

                object bed = selection.GetValue(0);
                AccessTools.Method(bed.GetType(), "UpdateRoyaltyStats")?.Invoke(bed, null);

                object stats = AccessTools.Property(bed.GetType(), "Stats")?.GetValue(bed, null);
                if (stats == null)
                {
                    return false;
                }

                RoyalTitleDef[] metRoyalTitles = AccessTools.Field(stats.GetType(), "metRoyalTitles")?.GetValue(stats) as RoyalTitleDef[];
                string nextTitleReq = AccessTools.Field(stats.GetType(), "textNextTitleReq")?.GetValue(stats)?.ToString();

                List<string> titleLabels = new List<string>();
                if (metRoyalTitles != null)
                {
                    foreach (RoyalTitleDef title in metRoyalTitles)
                    {
                        if (title != null)
                        {
                            titleLabels.Add(title.GetLabelCapForBothGenders());
                        }
                    }
                }

                string result = titleLabels.Count > 0
                    ? "RimWorldAccess.Compat.Hospitality.RoyalTitlesSatisfied".Translate(titleLabels.ToCommaList())
                    : "RimWorldAccess.Compat.Hospitality.NoRoyalTitlesSatisfied".Translate();

                if (!string.IsNullOrEmpty(nextTitleReq))
                {
                    result = result + " " + nextTitleReq;
                }

                description = result;
                return true;
            }
            catch (Exception ex)
            {
                ModLogger.Error($"ModifyNumberGizmoHandler.TryGetDescription failed: {ex.Message}");
                return false;
            }
        }
    }
}
