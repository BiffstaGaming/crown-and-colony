<#
.SYNOPSIS
  Launch Crown & Colony locally - loads the toolchain, then runs the Godot project.

.DESCRIPTION
  One command to play the game. Dot-sources scripts/dev-env.ps1 (puts the user-local
  .NET SDK on PATH + DOTNET_ROOT and locates Godot), then launches the game with
  --path game. Godot's .NET build compiles the C# on launch, so no separate build is
  needed just to play; pass -Build to force a clean dotnet build first.

  By default it uses the *console* Godot build so engine/C# logs (GD.Print, errors,
  stack traces) appear in this terminal - most useful while developing. Pass -NoConsole
  to use the windowed build with no attached log console.

  Any extra arguments are forwarded straight to Godot, e.g. for a quick headless
  smoke test:  .\scripts\run-game.ps1 --headless --quit-after 120

.PARAMETER Build
  Run 'dotnet build' of the solution before launching (catches C# errors up front).

.PARAMETER NoConsole
  Use the windowed Godot build (no attached log console window).

.EXAMPLE
  .\scripts\run-game.ps1
  Launch the game with logs visible in the terminal.

.EXAMPLE
  .\scripts\run-game.ps1 -Build
  Clean-build the C# first, then launch.
#>
[CmdletBinding()]
param(
    [switch]$Build,
    [switch]$NoConsole,
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$GodotArgs
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot   # repo root (scripts/..)

# Load DOTNET_ROOT / PATH / Godot paths into this session.
. (Join-Path $PSScriptRoot 'dev-env.ps1')

if ($Build) {
    dotnet build (Join-Path $root 'game\CrownAndColony.slnx')
    if ($LASTEXITCODE -ne 0) { throw "Build failed (exit $LASTEXITCODE)." }
}

$exe = if ($NoConsole) { $env:GODOT_GUI } else { $env:GODOT }
if (-not (Test-Path $exe)) { throw "Godot not found at '$exe'. Check scripts/dev-env.ps1." }

Write-Output "Launching: $exe --path game $GodotArgs"
& $exe --path (Join-Path $root 'game') @GodotArgs
exit $LASTEXITCODE
