using System;
using System.Collections.Generic;
using System.Linq;
using RimWorldAccess;

namespace RimWorldAccess.Tests;

/// <summary>
/// Tests for <see cref="SafeTypeSweep"/>'s pure per-type guard (poisoned-mod-assembly
/// hardening, 2026-08-02): a live-observed defect where a Type object that survives
/// enumeration can still throw when a later predicate call (Type.IsAssignableFrom,
/// Type.IsSubclassOf, etc.) forces the CLR to resolve it further. These tests fake the
/// "poisoned type" by handing WhereSafe/TryEvaluate a predicate/selector that throws for
/// one specific Type — standing in for a real poisoned type without needing an actual
/// unresolvable CLR type in the test host.
/// </summary>
public class SafeTypeSweepTests
{
    private static readonly Type Poisoned = typeof(DateTime);

    [Fact]
    public void WhereSafe_Null_YieldsNothing()
    {
        Assert.Empty(SafeTypeSweep.WhereSafe(null!, _ => true));
    }

    [Fact]
    public void WhereSafe_SkipsNullEntriesWithoutInvokingPredicate()
    {
        var predicateCalls = new List<Type>();
        var types = new Type[] { typeof(string), null!, typeof(int) };

        List<Type> result = SafeTypeSweep.WhereSafe(types, t =>
        {
            predicateCalls.Add(t);
            return true;
        }).ToList();

        Assert.Equal(new[] { typeof(string), typeof(int) }, result);
        Assert.Equal(new[] { typeof(string), typeof(int) }, predicateCalls);
    }

    [Fact]
    public void WhereSafe_FiltersByPredicate()
    {
        var types = new[] { typeof(string), typeof(int), typeof(double) };

        List<Type> result = SafeTypeSweep.WhereSafe(types, t => t == typeof(int)).ToList();

        Assert.Equal(new[] { typeof(int) }, result);
    }

    [Fact]
    public void WhereSafe_PredicateThrows_SkipsThatTypeButContinuesTheSweep()
    {
        var types = new[] { typeof(string), Poisoned, typeof(int) };

        List<Type> result = SafeTypeSweep.WhereSafe(types, t =>
        {
            if (t == Poisoned) throw new TypeLoadException("simulated poisoned mod type");
            return true;
        }).ToList();

        Assert.Equal(new[] { typeof(string), typeof(int) }, result);
    }

    [Fact]
    public void WhereSafe_PredicateThrows_ReportsTheFailure()
    {
        var types = new[] { typeof(string), Poisoned };
        var reported = new List<(Type, Exception)>();

        SafeTypeSweep.WhereSafe(types,
            t =>
            {
                if (t == Poisoned) throw new TypeLoadException("simulated poisoned mod type");
                return true;
            },
            (type, ex) => reported.Add((type, ex))).ToList();

        Assert.Single(reported);
        Assert.Equal(Poisoned, reported[0].Item1);
        Assert.IsType<TypeLoadException>(reported[0].Item2);
    }

    [Fact]
    public void WhereSafe_NoOnErrorHandler_StillSkipsThrowingTypeWithoutThrowing()
    {
        var types = new[] { Poisoned, typeof(int) };

        List<Type> result = SafeTypeSweep.WhereSafe(types, t =>
        {
            if (t == Poisoned) throw new TypeLoadException("simulated poisoned mod type");
            return true;
        }).ToList();

        Assert.Equal(new[] { typeof(int) }, result);
    }

    [Fact]
    public void TryEvaluate_Succeeds_ReturnsTrueAndResult()
    {
        bool ok = SafeTypeSweep.TryEvaluate(typeof(string), t => t.Name, out string result);

        Assert.True(ok);
        Assert.Equal("String", result);
    }

    [Fact]
    public void TryEvaluate_Throws_ReturnsFalseAndDefault()
    {
        bool ok = SafeTypeSweep.TryEvaluate(Poisoned, (Func<Type, string>)(_ =>
            throw new TypeLoadException("simulated poisoned mod type")), out string result);

        Assert.False(ok);
        Assert.Null(result);
    }

    [Fact]
    public void TryEvaluate_Throws_ReportsTheFailure()
    {
        Exception reported = null!;

        SafeTypeSweep.TryEvaluate(Poisoned, (Func<Type, string>)(_ =>
                throw new TypeLoadException("simulated poisoned mod type")),
            out _,
            (_, ex) => reported = ex);

        Assert.IsType<TypeLoadException>(reported);
    }
}
