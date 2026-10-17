<#
.SYNOPSIS
Copies a public Auric build into this repo as the version.dll Axon ships, after the
same checks CI makes.

.DESCRIPTION
Axon installs Auric\version.dll next to the game's exe; the game loads it at process
start and it is what loads the mods (see docs\memory-mod-loading.md). This script keeps
that file in step with the Auric repo:

  1. takes Kyber.dll from the Auric build (default: the local x64-Release build, which is
     the public variant; the x64-Dev build must never ship),
  2. refuses it if it carries any of the developer-only console command names, the same
     scan the CI workflow runs, or if it does not look like Auric at all,
  3. copies it to BassesModManager\Auric\version.dll and, when a build output exists, to
     bin\Release\Auric\version.dll so a running build picks it up without a rebuild,
  4. prints the SHA-256 and the Auric commit, for the commit message and the docs.

Run from anywhere:
  powershell -ExecutionPolicy Bypass -File BassesModManager\Auric\update-auric.ps1
  powershell -ExecutionPolicy Bypass -File BassesModManager\Auric\update-auric.ps1 -Source C:\Downloads\Kyber.dll

Then rebuild, commit ("chore: bundle Auric <commit>"), and note the hash in
docs\memory-mod-loading.md. Every player's game folder gets the new file on their next
launch (one administrator prompt).
#>
param(
    [string]$Source = "C:\Auric\Auric2\out\build\x64-Release\Kyber.dll",
    [string]$AuricRepo = "C:\Auric\Auric2"
)

$ErrorActionPreference = "Stop"
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$target = Join-Path $here "version.dll"
$binTarget = Join-Path $here "..\bin\Release\Auric\version.dll"

if (-not (Test-Path $Source)) {
    throw "No Auric build at $Source. Build the public variant (x64-Release, AURIC_DEV_BUILD=OFF) first."
}

# Same scan as "Refuse a developer build of Auric" in .github\workflows\build-installer.yml
$bytes = [IO.File]::ReadAllBytes($Source)
$text = [Text.Encoding]::ASCII.GetString($bytes) + "`n" +
        [Text.Encoding]::Unicode.GetString($bytes) + "`n" +
        [Text.Encoding]::Unicode.GetString($bytes, 1, $bytes.Length - 1)
$devMarkers = @('Teleport', 'KillPlayer', 'SetSprintSpeed', 'SetJumpHeight', 'SetPlayerHealth', 'SetPlayerVisibility', 'SetPlayerWeaponById', 'PrintTypeCount', 'DrawAuricDebug')
$found = $devMarkers | Where-Object { $text.Contains($_) }
if ($found) {
    throw "$Source is a developer build of Auric (found: $($found -join ', ')). Only the public build may ship."
}
if (-not $text.Contains('Initialized Auric')) {
    throw "$Source does not look like Auric's DLL at all."
}

$sourceInfo = Get-Item $Source
$hash = (Get-FileHash $Source).Hash.ToLower()

Copy-Item $Source $target -Force
Write-Host "Copied to $target"
if (Test-Path (Split-Path -Parent $binTarget)) {
    Copy-Item $Source $binTarget -Force
    Write-Host "Copied to $binTarget"
}

$commit = ""
if (Test-Path (Join-Path $AuricRepo ".git")) {
    try { $commit = (git -C $AuricRepo log -1 --format="%h %ci %s").Trim() } catch { }
}

Write-Host ""
Write-Host "Auric build: $($sourceInfo.LastWriteTime) ($($sourceInfo.Length) bytes)"
Write-Host "sha256:      $hash"
if ($commit) { Write-Host "Auric HEAD:  $commit (check that the build is from this commit)" }
Write-Host ""
Write-Host "Next: rebuild, commit the DLL, and put the hash in docs\memory-mod-loading.md."
