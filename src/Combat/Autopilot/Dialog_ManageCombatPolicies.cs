using System.Collections.Generic;
using RimWorld;
using RimWorldAccess.Shell;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Combat-policy manager on vanilla's <see cref="Dialog_ManagePolicies{T}"/> chassis; the
    /// contents pane is the behavior toggles, row rects recorded at draw for the focus ring.
    /// </summary>
    public class Dialog_ManageCombatPolicies : Dialog_ManagePolicies<CombatPolicy>
    {
        private const float RowHeight = 30f;
        private const float RowGap = 6f;

        private int recordedFrame = -1;
        private readonly Rect[] rowGuiRects = new Rect[CombatPolicyToggles.KeyParts.Length];
        private readonly Rect[] rowScreenRects = new Rect[CombatPolicyToggles.KeyParts.Length];
        private readonly GuiSpace.ClipKey[] rowClips = new GuiSpace.ClipKey[CombatPolicyToggles.KeyParts.Length];

        public Dialog_ManageCombatPolicies(CombatPolicy policy) : base(policy)
        {
        }

        protected override string TitleKey
        {
            get { return "RimWorldAccess.Autopilot.PolicyTitle"; }
        }

        protected override string TipKey
        {
            get { return "RimWorldAccess.Autopilot.PolicyTip"; }
        }

        protected override CombatPolicy CreateNewPolicy()
        {
            return CombatAutopilotComponent.MakeNewPolicy();
        }

        protected override CombatPolicy GetDefaultPolicy()
        {
            return CombatAutopilotComponent.DefaultPolicy();
        }

        protected override void SetDefaultPolicy(CombatPolicy policy)
        {
            CombatAutopilotComponent.SetDefaultPolicy(policy);
        }

        protected override AcceptanceReport TryDeletePolicy(CombatPolicy policy)
        {
            return CombatAutopilotComponent.TryDeletePolicy(policy);
        }

        protected override List<CombatPolicy> GetPolicies()
        {
            return CombatAutopilotComponent.AllPolicies;
        }

        protected override void DoContentsRect(Rect rect)
        {
            Widgets.DrawMenuSection(rect);
            CombatPolicy policy = SelectedPolicy;
            if (policy == null)
            {
                return;
            }
            Rect inner = rect.ContractedBy(10f);
            recordedFrame = Time.frameCount;
            float y = inner.y;
            for (int i = 0; i < CombatPolicyToggles.KeyParts.Length; i++)
            {
                var row = new Rect(inner.x, y, inner.width, RowHeight);
                y += RowHeight + RowGap;
                string keyPart = CombatPolicyToggles.KeyParts[i];
                bool value = CombatPolicyToggles.Get(policy, i);
                Widgets.CheckboxLabeled(row, ("RimWorldAccess.Autopilot." + keyPart + ".Label").Translate(), ref value);
                CombatPolicyToggles.Set(policy, i, value);
                if (Mouse.IsOver(row))
                {
                    TooltipHandler.TipRegion(row, ("RimWorldAccess.Autopilot." + keyPart + ".Desc").Translate());
                }
                rowGuiRects[i] = row;
                rowScreenRects[i] = GuiSpace.ToScreen(row);
                rowClips[i] = GuiSpace.CurrentClip();
            }
        }

        /// <summary>Toggle row rect in absolute UI points for the focus ring; empty when not drawn recently.</summary>
        internal Rect RowScreenRect(int index)
        {
            if (index < 0 || index >= rowGuiRects.Length || Time.frameCount - recordedFrame > 1)
            {
                return default(Rect);
            }
            return GuiSpace.VisibleScreenRectFrom(rowGuiRects[index], rowScreenRects[index], rowClips[index], rowGuiRects[index]);
        }
    }
}
