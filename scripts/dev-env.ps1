# Dot-source this to load the project toolchain into the current session:
#   . .\scripts\dev-env.ps1
# Paths are machine-specific (Chris's PC); CI installs its own toolchain.

$env:DOTNET_ROOT = "C:\Users\Chris\.dotnet"
$env:PATH = "$env:DOTNET_ROOT;$env:PATH"

# Godot 4.6.3 .NET edition; the _console exe attaches to the terminal (use for --headless).
$env:GODOT = "C:\Users\Chris\Tools\Godot_v4.6.3-stable_mono_win64\Godot_v4.6.3-stable_mono_win64_console.exe"

Write-Output "dotnet: $((Get-Command dotnet).Source) ($(dotnet --version))"
Write-Output "godot:  $env:GODOT"
