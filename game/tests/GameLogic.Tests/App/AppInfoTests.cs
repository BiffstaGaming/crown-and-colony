using System.Text.RegularExpressions;
using CrownAndColony.GameLogic.App;
using Xunit;

namespace CrownAndColony.GameLogic.Tests.App;

/// <summary>
/// L1 unit tests (docs/TESTING.md) for the app/release metadata accessor: the version is a non-empty, semver-shaped
/// string compiled in from the project file, and the product name / repository URL are present.
/// </summary>
public class AppInfoTests
{
    [Fact]
    public void Version_IsANonEmptySemverShapedString()
    {
        string version = AppInfo.Version;

        Assert.False(string.IsNullOrWhiteSpace(version));
        // major.minor.patch, optionally a pre-release/build suffix (e.g. "0.1.0" or "0.1.0-rc.1").
        Assert.Matches(new Regex(@"^\d+\.\d+\.\d+(?:[-.+][0-9A-Za-z.\-+]+)?$"), version);
    }

    [Fact]
    public void Name_AndRepositoryUrl_ArePresent()
    {
        Assert.Equal("Crown & Colony", AppInfo.Name);
        Assert.StartsWith("https://github.com/", AppInfo.RepositoryUrl);
    }
}
