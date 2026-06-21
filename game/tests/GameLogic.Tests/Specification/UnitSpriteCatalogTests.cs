using System.Collections.Generic;
using System.IO;
using System.Linq;
using CrownAndColony.GameLogic.Specification;
using Xunit;

namespace CrownAndColony.GameLogic.Tests.Specification;

/// <summary>
/// Verifies the engine-free unit-sprite selection rule (<see cref="UnitSpriteCatalog"/>). The actual <em>rendering</em>
/// isn't headless-testable, but the path-resolution <em>is</em> plain data: this guards that a native brave / convert /
/// native colonist (and a role-equipped brave) resolves to a real FreeCol sprite path rather than the red-disc
/// fallback — the heart of backlog item 86d3bmfcx. It also asserts the priority order (role-specific → generic-role →
/// bare-type) and that the native sprite files actually exist on disk under <c>game/assets/freecol/units/</c>.
/// </summary>
public class UnitSpriteCatalogTests
{
    [Fact]
    public void UnitDir_IsAGodotResourcePath()
    {
        Assert.StartsWith("res://", UnitSpriteCatalog.UnitDir);
        Assert.DoesNotContain(".png", UnitSpriteCatalog.UnitDir);
    }

    [Theory]
    [InlineData("brave", "res://assets/freecol/units/brave.png")]
    [InlineData("indianConvert", "res://assets/freecol/units/indianConvert.png")]
    [InlineData("nativeColonist", "res://assets/freecol/units/nativeColonist.png")]
    public void UnarmedNativeUnit_ResolvesToItsBareTypeSprite(string type, string expected)
    {
        // An unarmed brave/convert carries the "default" role → only the bare-type path, which is a real sprite,
        // so the map shows the FreeCol art, never the red disc.
        IReadOnlyList<string> paths = UnitSpriteCatalog.CandidatePaths(type, UnitSpriteCatalog.DefaultRole);
        Assert.Equal(new[] { expected }, paths);
    }

    [Theory]
    [InlineData("default")]
    [InlineData("")]
    [InlineData(null)]
    public void DefaultEmptyOrNullRole_ContributesOnlyTheBareTypePath(string? role)
    {
        IReadOnlyList<string> paths = UnitSpriteCatalog.CandidatePaths("brave", role);
        Assert.Equal(new[] { "res://assets/freecol/units/brave.png" }, paths);
    }

    [Theory]
    [InlineData("armedBrave")]
    [InlineData("mountedBrave")]
    [InlineData("nativeDragoon")]
    public void RoleEquippedBrave_TriesRoleSpecificThenGenericRoleThenBareType(string role)
    {
        // A brave in a native combat role: most-specific first. FreeCol reuses the single "brave" base type across
        // every native role, so the role-specific path units/{role}/brave.png is the one that exists and wins.
        IReadOnlyList<string> paths = UnitSpriteCatalog.CandidatePaths("brave", role);
        Assert.Equal(
            new[]
            {
                $"res://assets/freecol/units/{role}/brave.png",
                $"res://assets/freecol/units/{role}/{role}.png",
                "res://assets/freecol/units/brave.png",
            },
            paths);
    }

    [Fact]
    public void EuropeanRoleUnit_KeepsTheSamePriorityOrder()
    {
        // Regression guard that natives didn't change European resolution: a veteran soldier still tries its
        // role-specific sprite, then the generic soldier, then the bare type.
        IReadOnlyList<string> paths = UnitSpriteCatalog.CandidatePaths("veteranSoldier", "soldier");
        Assert.Equal(
            new[]
            {
                "res://assets/freecol/units/soldier/veteranSoldier.png",
                "res://assets/freecol/units/soldier/soldier.png",
                "res://assets/freecol/units/veteranSoldier.png",
            },
            paths);
    }

    [Theory]
    [InlineData("brave.png")]
    [InlineData("indianConvert.png")]
    [InlineData("nativeColonist.png")]
    [InlineData("armedBrave/brave.png")]
    [InlineData("mountedBrave/brave.png")]
    [InlineData("nativeDragoon/brave.png")]
    public void NativeSpriteFiles_ExistOnDisk(string relative)
    {
        // The selection rule is worthless if the art it points at is missing. Resolve the repo's assets/freecol/units
        // folder from the test assembly location and assert every native sprite the catalog can pick is really there.
        string unitsDir = RepoUnitsDir();
        string full = Path.Combine(unitsDir, relative.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(full), $"Missing native sprite asset: {full}");
    }

    /// <summary>Walks up from the test assembly to the repo's <c>game/assets/freecol/units</c> folder.</summary>
    private static string RepoUnitsDir()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir is not null)
        {
            string candidate = Path.Combine(dir.FullName, "game", "assets", "freecol", "units");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not locate game/assets/freecol/units above the test working directory.");
    }
}
