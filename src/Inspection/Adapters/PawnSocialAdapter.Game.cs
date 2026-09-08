using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.Sound;

namespace RimWorldAccess
{
    /// <summary>
    /// Adapter for the pawn Social tab ("Social" category). Builds the
    /// relations subtree (with pregnancy-approach and romance menus) plus the
    /// ideoligion/roles subtree and per-pawn social command actions.
    /// </summary>
    internal sealed class PawnSocialAdapter : InspectNodeAdapter
    {
        public override string CategoryKey => "Social";

        public override TabHandlerType Handler => TabHandlerType.RichNavigation;

        public override void BuildChildren(InspectionTreeItem categoryItem, object obj, InspectionMode mode)
        {
            Pawn pawn = InspectionTreeBuilder.GetPawnFromThing(obj);
            if (pawn == null)
                return;
            BuildSocialChildren(categoryItem, pawn, mode);
        }

        /// <summary>
        /// Builds children for Social category.
        /// </summary>
        private static void BuildSocialChildren(InspectionTreeItem parentItem, Pawn pawn, InspectionMode mode)
        {
            if (parentItem.Children.Count > 0)
                return; // Already built

            // Add Relations as expandable item
            var relationsItem = new InspectionTreeItem
            {
                Type = InspectionTreeItem.ItemType.SubCategory,
                Label = "Relations".Translate().ToString(),
                Data = new InspectSectionDatum(pawn, InspectSectionKind.SocialRelations),
                IndentLevel = parentItem.IndentLevel + 1,
                IsExpandable = true,
                IsExpanded = false
            };
            relationsItem.OnActivate = () => BuildSocialRelationsChildren(relationsItem, pawn);
            InspectNodeFactory.Attach(parentItem, relationsItem);

            // Add Ideology if applicable
            if (ModsConfig.IdeologyActive && pawn.ideo != null)
            {
                var ideologyItem = new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.SubCategory,
                    Label = "StatsReport_Ideoligion".Translate().ToString(),
                    Data = new InspectSectionDatum(pawn, InspectSectionKind.SocialIdeoligion),
                    IndentLevel = parentItem.IndentLevel + 1,
                    IsExpandable = true,
                    IsExpanded = false
                };
                ideologyItem.OnActivate = () => BuildIdeologyChildren(ideologyItem, pawn);
                InspectNodeFactory.Attach(parentItem, ideologyItem);
            }

            // Add Try Romance if applicable (Biotech DLC, eligible pawn, full inspection mode)
            if (mode != InspectionMode.ReadOnly && SocialTabHelper.CanTryRomance(pawn))
            {
                BuildRomanceMenu(parentItem, pawn);
            }

            // Banish / Execute - the per-pawn commands vanilla draws on the bio/character card.
            // Surfaced here (after relations, ideology, and romance) so all per-pawn social actions
            // live together. Each is independently gated by PawnCommandActionHelper and is unrelated
            // to the Ideology DLC. Read-only inspection (e.g. caravan formation) omits these.
            if (mode != InspectionMode.ReadOnly)
            {
                PawnCommandActionHelper.AddPawnCommandActions(parentItem, pawn, null);
            }
        }

        /// <summary>
        /// Builds children for Relations sub-category.
        /// </summary>
        private static void BuildSocialRelationsChildren(InspectionTreeItem parentItem, Pawn pawn)
        {
            if (parentItem.Children.Count > 0)
                return; // Already built

            var relations = SocialTabHelper.GetRelations(pawn);

            if (relations.Count == 0)
            {
                var noRelationsItem = new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.DetailText,
                    Label = "RimWorldAccess.Pawns.Social.Relation.NoRelations".Translate(),
                    IndentLevel = parentItem.IndentLevel + 1,
                    IsExpandable = false
                };
                InspectNodeFactory.Attach(parentItem, noRelationsItem);
                return;
            }

            foreach (var relation in relations)
            {
                string relationsStr = relation.Relations.Count > 0
                    ? string.Join(", ", relation.Relations)
                    : (string)"Acquaintance".Translate();
                var relationItem = new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.Item,
                    Label = "RimWorldAccess.Pawns.Social.Relation.Entry".Translate(
                        relation.OtherPawnName,
                        relationsStr,
                        relation.MyOpinion.ToString("+0;-0;0")),
                    Data = relation,
                    IndentLevel = parentItem.IndentLevel + 1,
                    IsExpandable = true,
                    IsExpanded = false
                };
                relationItem.OnActivate = () => BuildRelationDetailChildren(relationItem, pawn, relation);
                InspectNodeFactory.Attach(parentItem, relationItem);
            }
        }

        /// <summary>
        /// Builds detail children for a specific relation.
        /// </summary>
        private static void BuildRelationDetailChildren(InspectionTreeItem relationItem, Pawn inspectedPawn, SocialTabHelper.RelationInfo relation)
        {
            if (relationItem.Children.Count > 0)
                return; // Already built

            bool pregnancyApproachInserted = false;
            int childIndent = relationItem.IndentLevel + 1;

            for (int i = 0; i < relation.DetailLines.Count; i++)
            {
                var detailItem = new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.DetailText,
                    Label = relation.DetailLines[i].StripTags(),
                    IndentLevel = childIndent,
                    IsExpandable = false
                };
                InspectNodeFactory.Attach(relationItem, detailItem);

                // Insert pregnancy approach right after the Relationship line
                if (!pregnancyApproachInserted && i == relation.RelationshipLineIndex
                    && relation.CanChangePregnancyApproach && ModsConfig.BiotechActive)
                {
                    BuildPregnancyApproachMenu(relationItem, inspectedPawn, relation);
                    pregnancyApproachInserted = true;
                }
            }

            // Fallback: if no Relationship line was found, still add pregnancy approach at end
            if (!pregnancyApproachInserted && relation.CanChangePregnancyApproach && ModsConfig.BiotechActive)
            {
                BuildPregnancyApproachMenu(relationItem, inspectedPawn, relation);
            }
        }

        /// <summary>
        /// Builds a pregnancy approach sub-menu within a relation's detail children.
        /// </summary>
        private static void BuildPregnancyApproachMenu(InspectionTreeItem parentItem, Pawn pawn, SocialTabHelper.RelationInfo relation)
        {
            var currentApproach = relation.CurrentPregnancyApproach;

            var approachItem = new InspectionTreeItem
            {
                Type = InspectionTreeItem.ItemType.SubCategory,
                Label = $"{"PregnancyApproach".Translate()}: {currentApproach.GetLabel().CapitalizeFirst()}",
                Data = relation,
                IndentLevel = parentItem.IndentLevel + 1,
                IsExpandable = true,
                IsExpanded = false
            };

            approachItem.OnActivate = () =>
            {
                if (approachItem.Children.Count > 0)
                    return; // Already built

                int childIndent = approachItem.IndentLevel + 1;

                // Check if pregnancy is possible between these two pawns
                AcceptanceReport canProduce = PregnancyUtility.CanEverProduceChild(pawn, relation.OtherPawn);
                if (!canProduce.Accepted)
                {
                    InspectNodeFactory.Attach(approachItem, new InspectionTreeItem
                    {
                        Type = InspectionTreeItem.ItemType.DetailText,
                        Label = $"{"PregnancyNotPossible".Translate()}: {canProduce.Reason.CapitalizeFirst()}",
                        IndentLevel = childIndent,
                        IsExpandable = false
                    });
                    return;
                }

                foreach (PregnancyApproach approach in Enum.GetValues(typeof(PregnancyApproach)))
                {
                    bool isCurrent = approach == relation.CurrentPregnancyApproach;
                    string optionLabel = isCurrent
                        ? (string)"RimWorldAccess.Inspection.CurrentMarker".Translate(approach.GetDescription())
                        : approach.GetDescription();

                    var optionItem = new InspectionTreeItem
                    {
                        Type = InspectionTreeItem.ItemType.Action,
                        Label = optionLabel,
                        IndentLevel = childIndent,
                        IsExpandable = false
                    };

                    if (!isCurrent)
                    {
                        var capturedApproach = approach;
                        optionItem.OnActivate = () =>
                        {
                            SocialTabHelper.SetPregnancyApproach(pawn, relation.OtherPawn, capturedApproach);
                            relation.CurrentPregnancyApproach = capturedApproach;
                            InspectionTreeBuilder.RebuildBranchInPlace(approachItem, () =>
                            {
                                approachItem.Children.Clear();
                                approachItem.IsExpanded = false;
                                approachItem.Label = $"{"PregnancyApproach".Translate()}: {capturedApproach.GetLabel().CapitalizeFirst()}";
                            });
                        };
                    }

                    InspectNodeFactory.Attach(approachItem, optionItem);
                }
            };

            InspectNodeFactory.Attach(parentItem, approachItem);
        }

        /// <summary>
        /// Builds the Try Romance sub-menu within the Social category.
        /// Shows romance targets with success chance, gated behind Biotech DLC and pawn eligibility.
        /// Follows the same lazy-loading pattern as BuildPregnancyApproachMenu.
        /// </summary>
        private static void BuildRomanceMenu(InspectionTreeItem parentItem, Pawn pawn)
        {
            string romanceLabel = "TryRomanceButtonLabel".Translate();
            int childIndent = parentItem.IndentLevel + 1;

            // Check cooldown first
            if (SocialTabHelper.IsRomanceOnCooldown(pawn, out string cooldownText))
            {
                InspectNodeFactory.Attach(parentItem, new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.DetailText,
                    Label = $"{romanceLabel}: {cooldownText}",
                    IndentLevel = childIndent,
                    IsExpandable = false
                });
                return;
            }

            // Check initiator eligibility
            var eligibility = SocialTabHelper.GetRomanceInitiatorEligibility(pawn);
            if (!eligibility.Accepted)
            {
                if (!eligibility.Reason.NullOrEmpty())
                {
                    InspectNodeFactory.Attach(parentItem, new InspectionTreeItem
                    {
                        Type = InspectionTreeItem.ItemType.DetailText,
                        Label = $"{romanceLabel}: {eligibility.Reason}",
                        IndentLevel = childIndent,
                        IsExpandable = false
                    });
                }
                return;
            }

            // Eligible: create expandable SubCategory with lazy-loaded targets
            var romanceItem = new InspectionTreeItem
            {
                Type = InspectionTreeItem.ItemType.SubCategory,
                Label = romanceLabel,
                Data = new InspectSectionDatum(pawn, InspectSectionKind.SocialRomance),
                IndentLevel = childIndent,
                IsExpandable = true,
                IsExpanded = false
            };

            romanceItem.OnActivate = () =>
            {
                if (romanceItem.Children.Count > 0)
                    return; // Already built

                var targets = SocialTabHelper.GetRomanceTargets(pawn);

                if (targets.Count == 0)
                {
                    InspectNodeFactory.Attach(romanceItem, new InspectionTreeItem
                    {
                        Type = InspectionTreeItem.ItemType.DetailText,
                        Label = "TryRomanceNoOptsMessage".Translate(pawn),
                        IndentLevel = romanceItem.IndentLevel + 1,
                        IsExpandable = false
                    });
                    return;
                }

                int targetIndent = romanceItem.IndentLevel + 1;

                foreach (var target in targets)
                {
                    if (target.IsViable)
                    {
                        string targetLabel = "RimWorldAccess.Pawns.Social.Romance.TargetEntry".Translate(
                            target.TargetName,
                            target.Chance.ToStringPercent(),
                            "chance".Translate());

                        var capturedTarget = target;
                        var targetItem = new InspectionTreeItem
                        {
                            Type = InspectionTreeItem.ItemType.Action,
                            Label = targetLabel,
                            Data = target.Target,
                            IndentLevel = targetIndent,
                            IsExpandable = false
                        };

                        targetItem.OnActivate = () =>
                        {
                            if (SocialTabHelper.InitiateRomance(pawn, capturedTarget.Target))
                            {
                                TolkHelper.Speak("RimWorldAccess.Pawns.Social.Romance.WillTry".Loc(pawn.LabelShort, capturedTarget.TargetName));
                            }
                            else
                            {
                                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                            }
                        };

                        targetItem.OnInfo = () =>
                        {
                            string breakdown = SocialTabHelper.BuildRomanceBreakdown(
                                pawn, capturedTarget.Target);
                            StatBreakdownState.Open(
                                "RimWorldAccess.Pawns.Social.Romance.BreakdownHeader".Translate(
                                    capturedTarget.TargetName,
                                    "RomanceChance".Translate(),
                                    capturedTarget.Chance.ToStringPercent()),
                                breakdown);
                        };

                        InspectNodeFactory.Attach(romanceItem, targetItem);
                    }
                    else
                    {
                        InspectNodeFactory.Attach(romanceItem, new InspectionTreeItem
                        {
                            Type = InspectionTreeItem.ItemType.DetailText,
                            Label = "RimWorldAccess.Pawns.Social.Romance.TargetUnavailable".Translate(
                                target.TargetName, target.Reason),
                            IndentLevel = targetIndent,
                            IsExpandable = false
                        });
                    }
                }
            };

            InspectNodeFactory.Attach(parentItem, romanceItem);
        }

        /// <summary>
        /// Builds children for Ideology sub-category.
        /// </summary>
        private static void BuildIdeologyChildren(InspectionTreeItem parentItem, Pawn pawn)
        {
            if (parentItem.Children.Count > 0)
                return; // Already built

            var ideologyInfo = SocialTabHelper.GetIdeologyInfo(pawn);
            if (ideologyInfo == null)
            {
                var noIdeologyItem = new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.DetailText,
                    Label = "RimWorldAccess.Pawns.Social.Ideology.NotAvailable".Translate(),
                    IndentLevel = parentItem.IndentLevel + 1,
                    IsExpandable = false
                };
                InspectNodeFactory.Attach(parentItem, noIdeologyItem);
                return;
            }

            int childIndent = parentItem.IndentLevel + 1;

            // Add ideology name
            InspectNodeFactory.Attach(parentItem, new InspectionTreeItem
            {
                Type = InspectionTreeItem.ItemType.DetailText,
                Label = "RimWorldAccess.Pawns.Social.Ideology.Header".Translate(ideologyInfo.IdeoName),
                IndentLevel = childIndent,
                IsExpandable = false
            });

            // Add combined certainty with change rate (matches game tooltip format)
            string certaintyText = "Certainty".Translate().CapitalizeFirst();
            string certaintyLabel = $"{certaintyText}: {ideologyInfo.Certainty:P0}";
            float changePerDay = pawn.ideo.CertaintyChangePerDay;
            if (Math.Abs(changePerDay) > 0.001f)
            {
                string rateText = changePerDay.ToStringPercent();
                if (changePerDay > 0) rateText = "+" + rateText;
                certaintyLabel += $" ({"CertaintyChangePerDay".Translate()}: {rateText})";
            }
            InspectNodeFactory.Attach(parentItem, new InspectionTreeItem
            {
                Type = InspectionTreeItem.ItemType.DetailText,
                Label = certaintyLabel,
                IndentLevel = childIndent,
                IsExpandable = false
            });

            // Add Roles expandable section with assign/unassign actions
            var availableRoles = SocialTabHelper.GetAvailableRoles(pawn);
            if (availableRoles.Count > 0)
            {
                var rolesItem = new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.SubCategory,
                    Label = "IdeoRoles".Translate().CapitalizeFirst(),
                    Data = new InspectSectionDatum(pawn, InspectSectionKind.IdeoligionRoles),
                    IndentLevel = childIndent,
                    IsExpandable = true,
                    IsExpanded = false
                };
                rolesItem.OnActivate = () => BuildRolesChildren(rolesItem, pawn, availableRoles);
                InspectNodeFactory.Attach(parentItem, rolesItem);
            }
        }

        /// <summary>
        /// Builds children for the Roles section under Ideology.
        /// Lists each active role with its current holder and assign/unassign actions.
        /// </summary>
        private static void BuildRolesChildren(InspectionTreeItem parentItem, Pawn pawn, List<Precept_Role> roles)
        {
            if (parentItem.Children.Count > 0)
                return; // Already built

            int childIndent = parentItem.IndentLevel + 1;

            foreach (var role in roles)
            {
                Pawn currentHolder = role.ChosenPawnSingle();
                string holderName = currentHolder != null ? currentHolder.LabelShort.StripTags() : (string)"NoRoleAssigned".Translate();
                string roleLabel = "RimWorldAccess.Pawns.Social.Role.LabelWithHolder"
                    .Translate(role.LabelCap, holderName);

                var roleItem = new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.Item,
                    Label = roleLabel,
                    Data = role,
                    IndentLevel = childIndent,
                    IsExpandable = true,
                    IsExpanded = false
                };

                var capturedRole = role;
                roleItem.OnActivate = () => BuildRoleDetailChildren(roleItem, pawn, capturedRole);
                InspectNodeFactory.Attach(parentItem, roleItem);
            }
        }

        /// <summary>
        /// Builds detail children for a specific role, including assign/unassign actions.
        /// </summary>
        private static void BuildRoleDetailChildren(InspectionTreeItem roleItem, Pawn pawn, Precept_Role role)
        {
            if (roleItem.Children.Count > 0)
                return; // Already built

            int childIndent = roleItem.IndentLevel + 1;
            bool pawnHoldsRole = role.IsAssigned(pawn);
            bool pawnIsEligible = SocialTabHelper.IsEligibleForRole(role, pawn);

            // Role changes go through the RoleChange ritual dialog — the same
            // flow vanilla's "Choose role..." button opens. Vanilla only draws
            // that button for free non-slave colonists; the actions match.
            if (pawn.IsFreeNonSlaveColonist)
            {
                if (!pawnHoldsRole && pawnIsEligible)
                {
                    var assignItem = new InspectionTreeItem
                    {
                        Type = InspectionTreeItem.ItemType.Action,
                        Label = "ChooseRole".Translate().CapitalizeFirst() + ": "
                            + role.LabelForPawn(pawn).CapitalizeFirst(),
                        IndentLevel = childIndent,
                        IsExpandable = false,
                        OpensOverlayMenu = true
                    };
                    assignItem.OnActivate = () => SocialTabHelper.OpenRoleChangeRitual(pawn, role);
                    InspectNodeFactory.Attach(roleItem, assignItem);
                }

                if (pawnHoldsRole)
                {
                    var unassignItem = new InspectionTreeItem
                    {
                        Type = InspectionTreeItem.ItemType.Action,
                        Label = "ChooseRole".Translate().CapitalizeFirst() + ": "
                            + "RemoveCurrentRole".Translate().CapitalizeFirst(),
                        IndentLevel = childIndent,
                        IsExpandable = false,
                        OpensOverlayMenu = true
                    };
                    unassignItem.OnActivate = () => SocialTabHelper.OpenRoleChangeRitual(pawn, null);
                    InspectNodeFactory.Attach(roleItem, unassignItem);
                }
            }

            // Show why pawn can't be assigned if not eligible. Priority mirrors
            // vanilla's disabled-option reason exactly (SocialCardUtility.
            // DrawPawnRoleSelection's second cachedRoles loop): already held by
            // someone else, then an unmet requirement, then inactive for lack
            // of believers, then a generic fallback.
            if (!pawnHoldsRole && !pawnIsEligible)
            {
                Pawn otherHolder = role.ChosenPawnSingle();
                var unmetReq = role.GetFirstUnmetRequirement(pawn);
                string reason;
                if (otherHolder != null)
                {
                    reason = "RimWorldAccess.Pawns.Social.Role.CannotAssignReason"
                        .Translate(otherHolder.LabelShort.StripTags());
                }
                else if (unmetReq != null)
                {
                    reason = "RimWorldAccess.Pawns.Social.Role.CannotAssignReason"
                        .Translate(unmetReq.GetLabelCap(role).StripTags());
                }
                else if (!role.Active && role.def.activationBelieverCount > role.ideo.ColonistBelieverCountCached)
                {
                    // Reuses vanilla's own key from SocialCardUtility.DrawPawnRoleSelection —
                    // no separate RimWorldAccess string needed for this reason.
                    reason = "InactiveRoleRequiresMoreBelievers".Translate(
                        role.def.activationBelieverCount, role.ideo.memberName, role.ideo.ColonistBelieverCountCached).CapitalizeFirst();
                }
                else
                {
                    reason = "RimWorldAccess.Pawns.Social.Role.CannotAssignDefault".Translate();
                }

                InspectNodeFactory.Attach(roleItem, new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.DetailText,
                    Label = reason,
                    IndentLevel = childIndent,
                    IsExpandable = false
                });
            }

            // Add role description
            if (!string.IsNullOrEmpty(role.def.description))
            {
                InspectNodeFactory.Attach(roleItem, new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.DetailText,
                    Label = role.def.description.StripTags(),
                    IndentLevel = childIndent,
                    IsExpandable = false
                });
            }

            // Add role requirements
            if (role.def.roleRequirements != null && role.def.roleRequirements.Count > 0)
            {
                var reqsItem = new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.SubCategory,
                    Label = "RimWorldAccess.Pawns.Social.Role.RequirementsHeader".Translate(),
                    IndentLevel = childIndent,
                    IsExpandable = true,
                    IsExpanded = false
                };
                reqsItem.OnActivate = () =>
                {
                    if (reqsItem.Children.Count > 0) return;
                    foreach (var req in role.def.roleRequirements)
                    {
                        string reqLabel = req.GetLabelCap(role).StripTags();
                        if (!string.IsNullOrEmpty(reqLabel))
                        {
                            InspectNodeFactory.Attach(reqsItem, new InspectionTreeItem
                            {
                                Type = InspectionTreeItem.ItemType.DetailText,
                                Label = reqLabel,
                                IndentLevel = reqsItem.IndentLevel + 1,
                                IsExpandable = false
                            });
                        }
                    }
                };
                InspectNodeFactory.Attach(roleItem, reqsItem);
            }

            // Add role effects
            if (role.def.roleEffects != null && role.def.roleEffects.Count > 0)
            {
                var effectsItem = new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.SubCategory,
                    Label = "Effects".Translate().CapitalizeFirst(),
                    IndentLevel = childIndent,
                    IsExpandable = true,
                    IsExpanded = false
                };
                effectsItem.OnActivate = () =>
                {
                    if (effectsItem.Children.Count > 0) return;
                    foreach (var effect in role.def.roleEffects)
                    {
                        string effectLabel = effect.Label(pawn, role).StripTags();
                        if (!string.IsNullOrEmpty(effectLabel))
                        {
                            InspectNodeFactory.Attach(effectsItem, new InspectionTreeItem
                            {
                                Type = InspectionTreeItem.ItemType.DetailText,
                                Label = effectLabel,
                                IndentLevel = effectsItem.IndentLevel + 1,
                                IsExpandable = false
                            });
                        }
                    }
                };
                InspectNodeFactory.Attach(roleItem, effectsItem);
            }
        }
    }
}
