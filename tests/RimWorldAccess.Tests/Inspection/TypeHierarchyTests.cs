using RimWorldAccess;

namespace RimWorldAccess.Tests.Inspection;

public class TypeHierarchyTests
{
    private class Animal { }
    private class Dog : Animal { }
    private class Puppy : Dog { }
    private class Cat : Animal { }

    [Fact]
    public void IsSameOrAncestor_SameType_ReturnsTrue()
    {
        Assert.True(TypeHierarchy.IsSameOrAncestor(typeof(Dog), typeof(Dog)));
    }

    [Fact]
    public void IsSameOrAncestor_ActualAncestor_ReturnsTrue()
    {
        Assert.True(TypeHierarchy.IsSameOrAncestor(typeof(Animal), typeof(Puppy)));
        Assert.True(TypeHierarchy.IsSameOrAncestor(typeof(Dog), typeof(Puppy)));
    }

    [Fact]
    public void IsSameOrAncestor_Descendant_ReturnsFalse()
    {
        // The reverse direction: Puppy is BELOW Dog, not an ancestor of it — this is exactly the shape
        // an overriding subtype produces (hotel the FillTab-override decline predicate).
        Assert.False(TypeHierarchy.IsSameOrAncestor(typeof(Puppy), typeof(Dog)));
    }

    [Fact]
    public void IsSameOrAncestor_UnrelatedType_ReturnsFalse()
    {
        Assert.False(TypeHierarchy.IsSameOrAncestor(typeof(Cat), typeof(Dog)));
    }

    [Fact]
    public void IsSameOrAncestor_NullArguments_ReturnsFalse()
    {
        Assert.False(TypeHierarchy.IsSameOrAncestor(null, typeof(Dog)));
        Assert.False(TypeHierarchy.IsSameOrAncestor(typeof(Dog), null));
    }
}
