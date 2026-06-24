using System.Diagnostics;
using System.Reflection;

namespace CrownAndColony.GameLogic.App;

/// <summary>
/// Read-only application/release metadata for the running build — currently the single source of truth for the app
/// <see cref="Version"/> shown on the About screen and reported in logs. The version comes from the assembly's
/// informational version, which is set from <c>&lt;InformationalVersion&gt;</c>/<c>&lt;Version&gt;</c> in
/// <c>GameLogic.csproj</c> (e.g. <c>0.1.0</c>) at build time — so the displayed number is whatever was compiled in,
/// never a hand-maintained string that can drift.
/// </summary>
/// <remarks>
/// Engine-free (ADR-006) so the version is unit-testable (L1) without the Godot runtime. This is the <b>application</b>
/// release version (FreeCol equivalent: <c>FreeCol.getVersion()</c>) and is deliberately separate from the
/// <b>save-format</b> version in <c>SaveGame.cs</c>, which tracks on-disk compatibility and changes on its own cadence.
/// </remarks>
public static class AppInfo
{
    /// <summary>The product name shown on the title screen and the About screen.</summary>
    public const string Name = "Crown & Colony";

    /// <summary>The public project / source repository URL (shown on the About screen).</summary>
    public const string RepositoryUrl = "https://github.com/BiffstaGaming/crown-and-colony";

    /// <summary>
    /// The application/release version as a semver-shaped string (e.g. <c>0.1.0</c>), read from this assembly's
    /// informational version (falling back to the assembly version, then <c>0.0.0</c> if neither is present). Never
    /// empty.
    /// </summary>
    public static string Version { get; } = ResolveVersion();

    private static string ResolveVersion()
    {
        Assembly assembly = typeof(AppInfo).Assembly;

        // AssemblyInformationalVersion carries the <Version>/<InformationalVersion> string verbatim (it may include a
        // build-metadata suffix appended by the SDK, e.g. "0.1.0+<sha>"; strip that so callers see clean semver).
        string? informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informational))
        {
            int plus = informational.IndexOf('+');
            return plus >= 0 ? informational[..plus] : informational;
        }

        // Fallbacks for assemblies built without the informational attribute.
        string? fileVersion = FileVersionInfo.GetVersionInfo(assembly.Location).ProductVersion;
        if (!string.IsNullOrWhiteSpace(fileVersion))
        {
            return fileVersion;
        }

        return assembly.GetName().Version?.ToString() ?? "0.0.0";
    }
}
