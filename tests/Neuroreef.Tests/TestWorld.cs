using System;
using System.Runtime.CompilerServices;

namespace Neuroreef.Tests;

// Tests run on a small world (a 256-unit map) unless they need a big one: building a 1024 map with its geology, water
// cycle and thousands of animals takes a minute and gigabytes, and most laws are the same at any size. A test that
// does depend on size sets Map.Set itself and puts the default back after (Map.Set(TestWorld.Side)).
static class TestWorld
{
    public const int Side = 256;

    [ModuleInitializer]
    internal static void Init() => Map.Set(Side);
}

/// <summary>Runs a test (or every test of a class) on the full 1024 map: rivers, ponds and floods need real basins.</summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
sealed class BigWorldAttribute : Xunit.Sdk.BeforeAfterTestAttribute
{
    public override void Before(System.Reflection.MethodInfo m) => Map.Set(1024);
    public override void After(System.Reflection.MethodInfo m) => Map.Set(TestWorld.Side);
}
