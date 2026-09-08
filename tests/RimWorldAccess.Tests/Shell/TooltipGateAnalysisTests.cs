using System.Collections.Generic;
using System.Linq;
using RimWorldAccess.Shell;

namespace RimWorldAccess.Tests.Shell;

public class TooltipGateAnalysisTests
{
    private const string IsOver = "Verse.Mouse::IsOver";
    private const string TipRegion = "Verse.TooltipHandler::TipRegion";
    private const string TipRegionByKey = "Verse.TooltipHandler::TipRegionByKey";

    // Each census shape is written as the IL the C# lowers to, so the indices
    // in a branch target read as instruction positions in this list.
    private sealed class Body
    {
        private readonly List<GateInstruction> instructions = new();

        public Body Op(string opCode, string member = null, string pushes = null, int slot = -1)
        {
            instructions.Add(new GateInstruction(instructions.Count, opCode, member, -1, slot, pushes));
            return this;
        }

        public Body Call(string member, string returns = "System.Void") => Op("call", member, returns);

        public Body Load(int slot, string localType) => Op("ldloc.s", null, localType, slot);

        public Body Store(int slot) => Op("stloc.s", null, null, slot);

        public Body New(string type) => Op("newobj", type + "::.ctor", type);

        public Body NewArray(string element) => Op("newarr", null, element + "[]");

        public Body Branch(string opCode, int target)
        {
            instructions.Add(new GateInstruction(instructions.Count, opCode, null, target));
            return this;
        }

        public IReadOnlyList<GateSiteResult> Analyze() => TooltipGateAnalysis.Analyze(instructions);

        public GateSiteResult Only() => Assert.Single(Analyze());
    }

    [Fact]
    public void HoverGuardAroundATipRegion_IsEligible()
    {
        // if (Mouse.IsOver(rect)) TooltipHandler.TipRegion(rect, tip);
        GateSiteResult site = new Body()
            .Op("ldloc.0")
            .Call(IsOver)          // 1
            .Branch("brfalse.s", 5)
            .Op("ldloc.0")
            .Call(TipRegion)
            .Op("ret")             // 5
            .Only();

        Assert.Equal(GateVerdict.Eligible, site.Verdict);
        Assert.Equal(1, site.SiteIndex);
    }

    [Fact]
    public void HighlightDrawnAlongsideTheTip_IsStillEligible()
    {
        // Layout paints nothing, so a forced DrawHighlight is visually inert.
        GateSiteResult site = new Body()
            .Call(IsOver)
            .Branch("brfalse.s", 5)
            .Call("Verse.Widgets::DrawHighlight")
            .Op("ldloc.0")
            .Call(TipRegionByKey)
            .Op("ret")             // 5
            .Only();

        Assert.Equal(GateVerdict.Eligible, site.Verdict);
    }

    [Fact]
    public void CompoundCondition_IsEligible()
    {
        // if (flag && Mouse.IsOver(rect)) TooltipHandler.TipRegion(rect, tip);
        GateSiteResult site = new Body()
            .Op("ldloc.0")
            .Branch("brfalse.s", 6)
            .Call(IsOver)          // 2
            .Branch("brfalse.s", 6)
            .Op("ldloc.0")
            .Call(TipRegion)
            .Op("ret")             // 6
            .Only();

        Assert.Equal(GateVerdict.Eligible, site.Verdict);
        Assert.Equal(2, site.SiteIndex);
    }

    [Fact]
    public void ResultStoredInALocal_IsNotAConditional()
    {
        // bool flag = Mouse.IsOver(rect);
        GateSiteResult site = new Body()
            .Call(IsOver)
            .Op("stloc.0")
            .Op("ret")
            .Only();

        Assert.Equal(GateVerdict.NotAConditional, site.Verdict);
        Assert.Equal("stloc.0", site.Reason);
    }

    [Fact]
    public void ResultDuplicatedOnTheStack_IsNotAConditional()
    {
        GateSiteResult site = new Body()
            .Call(IsOver)
            .Op("dup")
            .Op("stloc.0")
            .Branch("brfalse.s", 5)
            .Call(TipRegion)
            .Op("ret")             // 5
            .Only();

        Assert.Equal(GateVerdict.NotAConditional, site.Verdict);
    }

    [Fact]
    public void TernaryIntoGuiColor_IsRefused()
    {
        // GUI.color = Mouse.IsOver(rect) ? hot : cold; — the ternary lowers to a
        // real conditional branch, so it is R4 that refuses it, not R1.
        GateSiteResult site = new Body()
            .Call(IsOver)
            .Branch("brtrue.s", 4)
            .Op("ldsfld", "UnityEngine.Color::white")
            .Branch("br.s", 5)
            .Op("ldsfld", "UnityEngine.Color::yellow")   // 4
            .Call("UnityEngine.GUI::set_color")          // 5
            .Op("ret")
            .Only();

        Assert.Equal(GateVerdict.NoTooltipInBranch, site.Verdict);
    }

    [Fact]
    public void GuardClauseReturn_HasNoTooltipInItsSpan()
    {
        // if (!Mouse.IsOver(rect)) return; — the span is a lone ret, so the
        // unbounded remainder of the method is never forced open.
        GateSiteResult site = new Body()
            .Call(IsOver)
            .Branch("brtrue.s", 3)
            .Op("ret")
            .Call(TipRegion)       // 3
            .Op("ret")
            .Only();

        Assert.Equal(GateVerdict.NoTooltipInBranch, site.Verdict);
    }

    [Fact]
    public void StaticFieldWriteInTheSpan_IsDangerous()
    {
        // The hover-follow writes in IdeoUIUtility.
        GateSiteResult site = new Body()
            .Call(IsOver)
            .Branch("brfalse.s", 5)
            .Op("ldloc.0")
            .Op("stsfld", "RimWorld.IdeoUIUtility::tmpMouseOverMeme")
            .Call(TipRegion)
            .Op("ret")             // 5
            .Only();

        Assert.Equal(GateVerdict.DangerousOperation, site.Verdict);
        Assert.Contains("tmpMouseOverMeme", site.Reason);
    }

    [Theory]
    [InlineData("stsfld", "RimWorld.ITab_Pawn_Visitor+<>c::<>9__7_0")]
    [InlineData("stfld", "RimWorld.CharacterCardUtility+<>c__DisplayClass42_0::<>9__3")]
    public void RoslynDelegateCacheWrite_IsEligible(string opCode, string field)
    {
        // The compiler memoizing the delegate it hands to TipRegion.
        GateSiteResult site = new Body()
            .Call(IsOver)
            .Branch("brfalse.s", 5)
            .Op("ldloc.0")
            .Op(opCode, field)
            .Call(TipRegion)
            .Op("ret")             // 5
            .Only();

        Assert.Equal(GateVerdict.Eligible, site.Verdict);
    }

    [Fact]
    public void DelegateCacheWriteBesideARealStore_IsStillDangerous()
    {
        GateSiteResult site = new Body()
            .Call(IsOver)
            .Branch("brfalse.s", 6)
            .Op("stsfld", "RimWorld.IdeoUIUtility+<>c::<>9__123_0")
            .Op("ldloc.0")
            .Op("stsfld", "RimWorld.IdeoUIUtility::tmpMouseOverMeme")
            .Call(TipRegion)
            .Op("ret")             // 6
            .Only();

        Assert.Equal(GateVerdict.DangerousOperation, site.Verdict);
        Assert.Contains("tmpMouseOverMeme", site.Reason);
    }

    [Fact]
    public void FieldNamedLikeACacheWithoutTheMarker_IsDangerous()
    {
        GateSiteResult site = new Body()
            .Call(IsOver)
            .Branch("brfalse.s", 5)
            .Op("ldloc.0")
            .Op("stsfld", "RimWorld.SomeUtility::x9__cachedHover")
            .Call(TipRegion)
            .Op("ret")             // 5
            .Only();

        Assert.Equal(GateVerdict.DangerousOperation, site.Verdict);
        Assert.Contains("x9__cachedHover", site.Reason);
    }

    [Theory]
    [InlineData("UnityEngine.Input::GetMouseButton")]
    [InlineData("UnityEngine.Event::Use")]
    [InlineData("Verse.Widgets::ButtonInvisible")]
    [InlineData("Verse.Widgets::ButtonImageFitted")]
    [InlineData("Verse.Widgets::ButtonTextSubtle")]
    [InlineData("RimWorld.TargetHighlighter::Highlight")]
    [InlineData("Verse.SoundStarter::PlayOneShotOnCamera")]
    [InlineData("System.Action::Invoke")]
    // Layout state: the gate forces a branch on the Layout pass only, so a
    // layout entry or an unclosed group there desyncs it from Repaint.
    [InlineData("UnityEngine.GUILayout::Label")]
    [InlineData("UnityEngine.GUILayoutUtility::GetRect")]
    [InlineData("UnityEngine.GUI::BeginGroup")]
    [InlineData("UnityEngine.GUI::EndGroup")]
    [InlineData("UnityEngine.GUI::BeginScrollView")]
    [InlineData("UnityEngine.GUI::BeginClip")]
    [InlineData("Verse.Widgets::BeginGroup")]
    [InlineData("Verse.Widgets::EndScrollView")]
    [InlineData("UnityEngine.GUIUtility::GetControlID")]
    public void BlockedCallInTheSpan_IsDangerous(string member)
    {
        GateSiteResult site = new Body()
            .Call(IsOver)
            .Branch("brfalse.s", 4)
            .Call(member)
            .Call(TipRegion)
            .Op("ret")             // 4
            .Only();

        Assert.Equal(GateVerdict.DangerousOperation, site.Verdict);
        Assert.Equal(member, site.Reason);
    }

    [Fact]
    public void BackwardBranch_IsMalformed()
    {
        GateSiteResult site = new Body()
            .Call(TipRegion)
            .Call(IsOver)
            .Branch("brfalse.s", 0)
            .Op("ret")
            .Only();

        Assert.Equal(GateVerdict.MalformedBranch, site.Verdict);
    }

    [Fact]
    public void NestedGuards_RefuseBoth()
    {
        GateSiteResult[] sites = new Body()
            .Call(IsOver)          // 0, outer
            .Branch("brfalse.s", 7)
            .Call(IsOver)          // 2, inner
            .Branch("brfalse.s", 5)
            .Call(TipRegion)
            .Call(TipRegion)       // 5
            .Op("nop")
            .Op("ret")             // 7
            .Analyze().ToArray();

        Assert.Equal(2, sites.Length);
        Assert.All(sites, s => Assert.Equal(GateVerdict.DangerousOperation, s.Verdict));
    }

    [Fact]
    public void OneEligibleSiteAmongRefusals_ReportsItsOwnIndex()
    {
        GateSiteResult[] sites = new Body()
            .Call(IsOver)          // 0, refused: stored in a local
            .Op("stloc.0")
            .Op("ldloc.1")
            .Call(IsOver)          // 3, certified
            .Branch("brfalse.s", 6)
            .Call(TipRegion)
            .Op("ret")             // 6
            .Analyze().ToArray();

        Assert.Equal(2, sites.Length);
        GateSiteResult eligible = Assert.Single(sites, s => s.Verdict == GateVerdict.Eligible);
        Assert.Equal(3, eligible.SiteIndex);
    }

    [Fact]
    public void MethodWithoutAnyHoverTest_HasNoSites()
    {
        Assert.Empty(new Body().Call(TipRegion).Op("ret").Analyze());
    }

    // The freshness rule, shape by shape against the census. Group A is what it
    // recovers; B through E must survive it untouched.
    private const string Display = "RimWorld.StatDrawEntry+<>c__DisplayClass30_0";
    private const string Mass = "RimWorld.TransferableOneWayWidget+<>c__DisplayClass69_1";

    [Fact]
    public void GroupA_FieldWriteIntoAFreshDisplayClass_IsEligible()
    {
        // StatDrawEntry.Draw #92: the receiver is loaded six instructions
        // before the store, which is why adjacency matching cannot decide this.
        GateSiteResult site = new Body()
            .Call(IsOver, "System.Boolean")
            .Branch("brfalse.s", 9)
            .New(Display)                                          // 2
            .Store(6)
            .Load(6, Display)
            .Op("ldarg.0", null, "RimWorld.StatDrawEntry")
            .Op("ldfld", "RimWorld.StatDrawEntry::stat", "RimWorld.StatDef")
            .Op("stfld", Display + "::localStat")
            .Call(TipRegion)
            .Op("ret")                                             // 9
            .Only();

        Assert.Equal(GateVerdict.Eligible, site.Verdict);
    }

    [Fact]
    public void GroupA_ElementWriteIntoAFreshArray_IsEligible()
    {
        // IdeoUIUtility.DoName #236: string[] built by dup, never stored.
        GateSiteResult site = new Body()
            .Call(IsOver, "System.Boolean")
            .Branch("brfalse.s", 11)
            .Op("ldc.i4.2")
            .NewArray("System.String")                             // 3
            .Op("dup")
            .Op("ldc.i4.0")
            .Op("ldstr")
            .Op("stelem.ref")
            .Call("System.String::Concat", "System.String")
            .Call(TipRegion)
            .Op("nop")
            .Op("ret")                                             // 11
            .Only();

        Assert.Equal(GateVerdict.Eligible, site.Verdict);
    }

    [Fact]
    public void GroupA_BranchInsideTheSpan_IsEligible()
    {
        // DoNameAndSymbol #161 threads a ternary through the array fill; the
        // rule quantifies over the whole span, so joins cost nothing.
        GateSiteResult site = new Body()
            .Call(IsOver, "System.Boolean")
            .Branch("brfalse.s", 13)
            .Op("ldc.i4.1")
            .NewArray("System.String")
            .Op("dup")
            .Op("ldc.i4.0")
            .Op("ldarg.1", null, "System.Boolean")
            .Branch("brtrue.s", 10)                                // 7
            .Op("ldstr")
            .Branch("br.s", 11)
            .Op("ldstr")                                           // 10
            .Op("stelem.ref")                                      // 11
            .Call(TipRegion)
            .Op("ret")                                             // 13
            .Only();

        Assert.Equal(GateVerdict.Eligible, site.Verdict);
    }

    [Fact]
    public void GroupA_FreshArrayAndFreshDisplayClassTogether_IsEligible()
    {
        // ModSummaryWindow.<DrawContents>b__4 #128.
        const string outer = "Verse.ModSummaryWindow+<>c__DisplayClass17_2";
        GateSiteResult site = new Body()
            .Call(IsOver, "System.Boolean")
            .Branch("brfalse.s", 15)
            .New(outer)                                            // 2
            .Store(7)
            .Load(7, outer)
            .Op("ldc.i4.1")
            .NewArray("System.String")
            .Op("dup")
            .Op("ldc.i4.0")
            .Op("ldstr")
            .Op("stelem.ref")
            .Call("System.String::Concat", "System.String")
            .Op("stfld", outer + "::description")
            .Call(TipRegion)
            .Op("nop")
            .Op("ret")                                             // 15
            .Only();

        Assert.Equal(GateVerdict.Eligible, site.Verdict);
    }

    [Fact]
    public void GroupA_DelegateCacheBesideAFreshWrite_IsEligible()
    {
        // SocialCardUtility.DrawPawnCertainty #103.
        GateSiteResult site = new Body()
            .Call(IsOver, "System.Boolean")
            .Branch("brfalse.s", 10)
            .New(Display)
            .Store(6)
            .Load(6, Display)
            .Op("ldstr")
            .Op("stfld", Display + "::localStat")
            .Op("stsfld", "RimWorld.SocialCardUtility+<>c::<>9__40_0")
            .Call(TipRegion)
            .Op("nop")
            .Op("ret")                                             // 10
            .Only();

        Assert.Equal(GateVerdict.Eligible, site.Verdict);
    }

    [Fact]
    public void DrawMassCounterexample_IsRefused()
    {
        // TransferableOneWayWidget.DrawMass #47: the display class was
        // allocated by the caller, so the span writes into state that outlives
        // it. Type-name matching alone would have certified this.
        GateSiteResult site = new Body()
            .Call(IsOver, "System.Boolean")
            .Branch("brfalse.s", 8)
            .Load(3, Mass)                                         // 2
            .Op("ldc.r4")
            .Op("stfld", Mass + "::gearMass")
            .Op("ldarg.1", null, "UnityEngine.Rect")
            .Call(TipRegion)
            .Op("nop")
            .Op("ret")                                             // 8
            .Only();

        Assert.Equal(GateVerdict.DangerousOperation, site.Verdict);
        Assert.Contains("gearMass", site.Reason);
    }

    [Fact]
    public void FreshAllocationBesideAForeignInstanceOfTheSameType_IsRefused()
    {
        // The shape DrawMass is one site away from: the span allocates a
        // display class and also holds one it did not.
        GateSiteResult site = new Body()
            .Call(IsOver, "System.Boolean")
            .Branch("brfalse.s", 10)
            .New(Mass)                                             // 2
            .Store(0)
            .Load(3, Mass)                                         // 4, the foreign one
            .Op("ldc.r4")
            .Op("stfld", Mass + "::gearMass")
            .Op("ldloc.s", null, Mass, 0)
            .Call(TipRegion)
            .Op("nop")
            .Op("ret")                                             // 10
            .Only();

        Assert.Equal(GateVerdict.DangerousOperation, site.Verdict);
    }

    [Fact]
    public void SlotWrittenTwiceIsNoLongerFresh()
    {
        GateSiteResult site = new Body()
            .Call(IsOver, "System.Boolean")
            .Branch("brfalse.s", 11)
            .New(Display)                                          // 2
            .Store(0)
            .Op("ldsfld", "RimWorld.Cache::entry", Display)
            .Store(0)                                              // 5
            .Load(0, Display)
            .Op("ldstr")
            .Op("stfld", Display + "::localStat")
            .Call(TipRegion)
            .Op("nop")
            .Op("ret")                                             // 11
            .Only();

        Assert.Equal(GateVerdict.DangerousOperation, site.Verdict);
    }

    [Fact]
    public void BackEdgeOverTheAllocation_IsRefused()
    {
        GateSiteResult site = new Body()
            .Call(IsOver, "System.Boolean")
            .Branch("brfalse.s", 11)
            .New(Display)                                          // 2
            .Store(0)
            .Load(0, Display)
            .Op("ldstr")
            .Op("stfld", Display + "::localStat")
            .Op("ldarg.0", null, "System.Boolean")
            .Branch("brtrue.s", 2)                                 // 8
            .Call(TipRegion)
            .Op("nop")
            .Op("ret")                                             // 11
            .Only();

        Assert.Equal(GateVerdict.DangerousOperation, site.Verdict);
    }

    [Fact]
    public void JumpIntoTheSpanFromOutside_IsRefused()
    {
        GateSiteResult site = new Body()
            .Op("nop")
            .Branch("br.s", 6)                                     // 1, lands mid-span
            .Call(IsOver, "System.Boolean")                        // 2
            .Branch("brfalse.s", 11)
            .New(Display)                                          // 4
            .Store(0)
            .Load(0, Display)                                      // 6
            .Op("ldstr")
            .Op("stfld", Display + "::localStat")
            .Call(TipRegion)
            .Op("nop")
            .Op("ret")                                             // 11
            .Only();

        Assert.Equal(GateVerdict.DangerousOperation, site.Verdict);
    }

    [Fact]
    public void SwitchAnywhereInTheMethod_RefusesASlotBackedSpan()
    {
        // One branch target per instruction, so a jump table's other arms are
        // edges the reachability check cannot see.
        GateSiteResult site = new Body()
            .Call(IsOver, "System.Boolean")
            .Branch("brfalse.s", 9)
            .New(Display)
            .Store(0)
            .Load(0, Display)
            .Op("ldstr")
            .Op("stfld", Display + "::localStat")
            .Call(TipRegion)
            .Op("nop")
            .Op("switch")                                          // 9
            .Op("ret")
            .Only();

        Assert.Equal(GateVerdict.DangerousOperation, site.Verdict);
    }

    [Theory]
    [InlineData("System.Object")]
    [InlineData("System.String[]")]
    [InlineData("System.Collections.Generic.IList`1<System.String>")]
    [InlineData("System.Array")]
    public void ArrayCapableValueBesideAnElementStore_IsRefused(string pushed)
    {
        GateSiteResult site = new Body()
            .Call(IsOver, "System.Boolean")
            .Branch("brfalse.s", 11)
            .Op("ldc.i4.1")
            .NewArray("System.String")
            .Op("dup")
            .Op("ldc.i4.0")
            .Op("ldstr")
            .Op("stelem.ref")
            .Op("ldsfld", "RimWorld.Cache::other", pushed)         // 8
            .Op("pop")
            .Call(TipRegion)
            .Op("ret")                                             // 11
            .Only();

        Assert.Equal(GateVerdict.DangerousOperation, site.Verdict);
    }

    [Theory]
    [InlineData("box", "System.Object", null)]
    [InlineData("stobj", null, null)]
    [InlineData("calli", null, null)]
    [InlineData("ldelem.ref", null, null)]
    [InlineData("call", null, "RimWorld.Util::Build")]
    public void UntypedOrUnmodelledInstruction_RefusesTheSpan(string opCode, string pushes, string member)
    {
        // The last row is the fail-safe: a bridge that cannot name a call's
        // return type costs coverage, never soundness.
        GateSiteResult site = new Body()
            .Call(IsOver, "System.Boolean")
            .Branch("brfalse.s", 10)
            .New(Display)
            .Store(0)
            .Load(0, Display)
            .Op("ldstr")
            .Op("stfld", Display + "::localStat")
            .Op(opCode, member, pushes)                            // 7
            .Call(TipRegion)
            .Op("nop")
            .Op("ret")                                             // 10
            .Only();

        Assert.Equal(GateVerdict.DangerousOperation, site.Verdict);
    }

    [Theory]
    // Group B: state that outlives the span.
    [InlineData("stfld", "RimWorld.Dialog_EditPrecept::selectedStyle")]
    [InlineData("starg.s", null)]
    // Group D: mutating a TipSignal local through its address.
    [InlineData("stfld", "Verse.TipSignal::text")]
    // Group C: writes through a ref or out parameter.
    [InlineData("stind.i1", null)]
    [InlineData("stind.ref", null)]
    [InlineData("stind.r4", null)]
    public void StoreOutsideTheFreshnessRule_IsDangerous(string opCode, string member)
    {
        GateSiteResult site = new Body()
            .Call(IsOver, "System.Boolean")
            .Branch("brfalse.s", 8)
            .New(Display)
            .Store(0)
            .Load(0, Display)
            .Op(opCode, member)                                    // 5
            .Call(TipRegion)
            .Op("nop")
            .Op("ret")                                             // 8
            .Only();

        Assert.Equal(GateVerdict.DangerousOperation, site.Verdict);
    }

    [Fact]
    public void FreshAllocationWithoutAnyStore_IsUnaffected()
    {
        GateSiteResult site = new Body()
            .Call(IsOver, "System.Boolean")
            .Branch("brfalse.s", 7)
            .New(Display)
            .Store(0)
            .Load(0, Display)
            .Call(TipRegion)
            .Op("nop")
            .Op("ret")                                             // 7
            .Only();

        Assert.Equal(GateVerdict.Eligible, site.Verdict);
    }
}
