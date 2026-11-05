<#
  Builds KerbalNews.dll against your own KSP install and optionally installs it.
  Needs no .NET SDK: it uses the C# compiler that ships with Visual Studio (or Build Tools)
  and links against the DLLs in KSP_x64_Data\Managed.

  .\build.ps1                       # build only
  .\build.ps1 -Install              # build and copy into <KSP>\GameData\KerbalNews
  .\build.ps1 -KspDir "D:\Games\KSP" -Install
#>
param(
    [string]$KspDir = $env:KSP_DIR,
    [switch]$Install
)
$ErrorActionPreference = "Stop"
if (-not $KspDir) { $KspDir = "C:\Program Files (x86)\Steam\steamapps\common\Kerbal Space Program" }
$managed = Join-Path $KspDir "KSP_x64_Data\Managed"
if (-not (Test-Path (Join-Path $managed "Assembly-CSharp.dll"))) { throw "KSP not found at '$KspDir'. Pass -KspDir or set KSP_DIR." }

# Find a modern (Roslyn) csc.exe from Visual Studio or Build Tools.
$csc = $null
$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
if (Test-Path $vswhere) {
    $vs = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -property installationPath
    if ($vs) { $cand = Join-Path $vs "MSBuild\Current\Bin\Roslyn\csc.exe"; if (Test-Path $cand) { $csc = $cand } }
}
if (-not $csc) {
    $cand = Get-ChildItem "${env:ProgramFiles(x86)}\Microsoft Visual Studio","$env:ProgramFiles\Microsoft Visual Studio" -Recurse -Filter csc.exe -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -like "*Roslyn*" } | Select-Object -First 1
    if ($cand) { $csc = $cand.FullName }
}
if (-not $csc) { throw "No Roslyn csc.exe found. Install Visual Studio Build Tools (C# compiler)." }

# Reference the core libraries plus every Unity module, so new Unity type dependencies never break the build.
$names = @("mscorlib","System","System.Core","netstandard","Assembly-CSharp","Assembly-CSharp-firstpass")
$refFiles = ($names | ForEach-Object { Join-Path $managed "$_.dll" } | Where-Object { Test-Path $_ }) +
            (Get-ChildItem $managed -Filter "UnityEngine*.dll" | ForEach-Object { $_.FullName })
$refs = $refFiles | Select-Object -Unique | ForEach-Object { "-r:`"$_`"" }

$root = $PSScriptRoot
$out = Join-Path $root "GameData\KerbalNews\Plugins\KerbalNews.dll"
New-Item -ItemType Directory -Force -Path (Split-Path $out) | Out-Null
$cscArgs = @("-nologo","-target:library","-optimize+","-debug-","-nostdlib+","-langversion:7.3","-warn:4","-out:`"$out`"") + $refs +
        @("`"$root\src\Core\*.cs`"","`"$root\src\KSP\*.cs`"")
Write-Host "Compiling with $csc"
$p = Start-Process -FilePath $csc -ArgumentList $cscArgs -NoNewWindow -Wait -PassThru
if ($p.ExitCode -ne 0) { throw "Build failed." }
Write-Host "Built $out"

if ($Install) {
    $dest = Join-Path $KspDir "GameData\KerbalNews"
    foreach ($sub in "Plugins","Icons") {
        New-Item -ItemType Directory -Force -Path (Join-Path $dest $sub) | Out-Null
        Copy-Item -Force -Recurse (Join-Path $root "GameData\KerbalNews\$sub\*") (Join-Path $dest $sub)
    }
    New-Item -ItemType Directory -Force -Path (Join-Path $dest "PluginData") | Out-Null  # settings and keys stay untouched
    Write-Host "Installed to $dest"
}
