using RimWorldAccess;

namespace RimWorldAccess.Tests.Inspection;

public class TypeChainResolverTests
{
    private class Animal { }
    private class Dog : Animal { }
    private class Puppy : Dog { }
    private class Cat : Animal { }
    private class Unrelated { }

    [Fact]
    public void TryResolve_ExactTypeRegistered_ReturnsItsValue()
    {
        var resolver = new TypeChainResolver<string>();
        resolver.Register(typeof(Dog), "dog-handler");

        bool found = resolver.TryResolve(typeof(Dog), out string value);

        Assert.True(found);
        Assert.Equal("dog-handler", value);
    }

    [Fact]
    public void TryResolve_SubtypeOfRegisteredType_WalksUpToBaseRegistration()
    {
        // Reproduces `is Dog` matching a Puppy instance.
        var resolver = new TypeChainResolver<string>();
        resolver.Register(typeof(Dog), "dog-handler");

        bool found = resolver.TryResolve(typeof(Puppy), out string value);

        Assert.True(found);
        Assert.Equal("dog-handler", value);
    }

    [Fact]
    public void TryResolve_MostDerivedRegistrationWins_OverBaseRegistration()
    {
        var resolver = new TypeChainResolver<string>();
        resolver.Register(typeof(Animal), "animal-handler");
        resolver.Register(typeof(Dog), "dog-handler");

        bool found = resolver.TryResolve(typeof(Puppy), out string value);

        Assert.True(found);
        Assert.Equal("dog-handler", value);
    }

    [Fact]
    public void ResolveChain_YieldsEveryRegistrationMostDerivedFirst()
    {
        var resolver = new TypeChainResolver<string>();
        resolver.Register(typeof(Animal), "animal-handler");
        resolver.Register(typeof(Dog), "dog-handler");

        var chain = resolver.ResolveChain(typeof(Puppy)).ToList();

        Assert.Equal(new[] { "dog-handler", "animal-handler" }, chain);
    }

    [Fact]
    public void ResolveChain_NoRegistrations_YieldsNothing()
    {
        var resolver = new TypeChainResolver<string>();
        resolver.Register(typeof(Cat), "cat-handler");

        Assert.Empty(resolver.ResolveChain(typeof(Puppy)));
    }

    [Fact]
    public void TryResolve_SiblingType_DoesNotMatch()
    {
        var resolver = new TypeChainResolver<string>();
        resolver.Register(typeof(Dog), "dog-handler");

        bool found = resolver.TryResolve(typeof(Cat), out string value);

        Assert.False(found);
        Assert.Null(value);
    }

    [Fact]
    public void TryResolve_NoRegistrationAnywhereInChain_ReturnsFalse()
    {
        var resolver = new TypeChainResolver<string>();
        resolver.Register(typeof(Dog), "dog-handler");

        bool found = resolver.TryResolve(typeof(Unrelated), out string value);

        Assert.False(found);
        Assert.Null(value);
    }

    [Fact]
    public void Register_SameTypeTwice_ReplacesPreviousValue()
    {
        var resolver = new TypeChainResolver<string>();
        resolver.Register(typeof(Dog), "first");
        resolver.Register(typeof(Dog), "second");

        resolver.TryResolve(typeof(Dog), out string value);

        Assert.Equal("second", value);
    }

    [Fact]
    public void TryResolveWithMatchedType_ExactRegistration_ReportsRuntimeTypeItself()
    {
        var resolver = new TypeChainResolver<string>();
        resolver.Register(typeof(Dog), "dog-handler");

        bool found = resolver.TryResolve(typeof(Dog), out string value, out Type matchedType);

        Assert.True(found);
        Assert.Equal("dog-handler", value);
        Assert.Equal(typeof(Dog), matchedType);
    }

    [Fact]
    public void TryResolveWithMatchedType_InheritedRegistration_ReportsTheAncestorType()
    {
        var resolver = new TypeChainResolver<string>();
        resolver.Register(typeof(Dog), "dog-handler");

        bool found = resolver.TryResolve(typeof(Puppy), out string value, out Type matchedType);

        Assert.True(found);
        Assert.Equal("dog-handler", value);
        Assert.Equal(typeof(Dog), matchedType);
    }

    [Fact]
    public void TryResolveWithMatchedType_NoMatch_ReportsNullMatchedType()
    {
        var resolver = new TypeChainResolver<string>();
        resolver.Register(typeof(Dog), "dog-handler");

        bool found = resolver.TryResolve(typeof(Cat), out string value, out Type matchedType);

        Assert.False(found);
        Assert.Null(matchedType);
    }
}
