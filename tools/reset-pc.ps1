<#
.SYNOPSIS
Removes every trace of Axon, Auric and Frosty Mod Manager from this PC, so the next
start of Axon behaves like a first install.

.DESCRIPTION
Without -Delete the script only lists what it would remove. With -Delete it removes:

  - Axon itself: runs its uninstaller silently if installed, then deletes what is left
    of C:\Program Files\BassesModManager
  - %ProgramData%\BassesModManager (mods, update downloads, Frosty's old asset cache)
  - %LocalAppData%\BassesModManager (user settings, launch marker, Frosty's old config)
  - %TEMP%\BassesModManager
  - In the game folder: version.dll (Auric), CryptBase.dll (Frosty's shim), ModData
  - %AppData%\Auric (Auric's log, config with the website token, saved matches);
    -KeepAuricConfig keeps config.json
  - %LocalAppData%\Frosty (Frosty Mod Manager's config)

The game folder is read from Axon's saved settings, else from the EA app's registry key,
else -GamePath. Nothing of EA's own installation is touched. Needs an elevated shell for
the game folder and Program Files; it relaunches itself elevated if needed.

.EXAMPLE
powershell -ExecutionPolicy Bypass -File tools\reset-pc.ps1
powershell -ExecutionPolicy Bypass -File tools\reset-pc.ps1 -Delete
powershell -ExecutionPolicy Bypass -File tools\reset-pc.ps1 -Delete -KeepAuricConfig
#>
param(
    [switch]$Delete,
    [switch]$KeepAuricConfig,
    [string]$GamePath
)

$ErrorActionPreference = "Stop"

# Elevation: Program Files and the game folder need it
$principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    $args = @("-ExecutionPolicy", "Bypass", "-NoExit", "-File", "`"$($MyInvocation.MyCommand.Path)`"")
    if ($Delete) { $args += "-Delete" }
    if ($KeepAuricConfig) { $args += "-KeepAuricConfig" }
    if ($GamePath) { $args += @("-GamePath", "`"$GamePath`"") }
    Start-Process -Verb RunAs powershell.exe -ArgumentList $args
    return
}

# Nothing may be running
$running = Get-Process -Name BassesModManager, starwarsbattlefront, FrostyModManager -ErrorAction SilentlyContinue
if ($running) {
    Write-Host "Close these first: $(($running.Name | Sort-Object -Unique) -join ', ')" -ForegroundColor Yellow
    return
}

# Where the game is
function Find-GamePath {
    if ($GamePath) { return $GamePath }
    $cfgs = Get-ChildItem "$env:LOCALAPPDATA\BassesModManager" -Recurse -Filter user.config -ErrorAction SilentlyContinue
    foreach ($cfg in $cfgs) {
        $m = [regex]::Match((Get-Content $cfg.FullName -Raw), '<setting name="GamePath"[^>]*>\s*<value>([^<]+)</value>')
        if ($m.Success -and (Test-Path (Join-Path $m.Groups[1].Value "starwarsbattlefront.exe"))) { return $m.Groups[1].Value }
    }
    foreach ($key in "HKLM:\SOFTWARE\WOW6432Node\EA Games\STAR WARS Battlefront", "HKLM:\SOFTWARE\EA Games\STAR WARS Battlefront") {
        $dir = (Get-ItemProperty $key -ErrorAction SilentlyContinue)."Install Dir"
        if ($dir -and (Test-Path (Join-Path $dir "starwarsbattlefront.exe"))) { return $dir.TrimEnd('\') }
    }
    return $null
}

$game = Find-GamePath
$programData = [Environment]::GetFolderPath("CommonApplicationData")
$appFolder = Join-Path ${env:ProgramFiles} "BassesModManager"

$targets = New-Object System.Collections.Generic.List[object]
function Add-Target($path, $what) {
    if ($path -and (Test-Path -LiteralPath $path)) { $targets.Add([pscustomobject]@{ Path = $path; What = $what }) }
}

Add-Target $appFolder "Axon program folder"
Add-Target "$programData\BassesModManager" "mods, update downloads, old Frosty cache"
Add-Target "$env:LOCALAPPDATA\BassesModManager" "user settings, launch marker, old Frosty config"
Add-Target "$env:TEMP\BassesModManager" "update downloads (fallback location)"
if ($game) {
    Add-Target (Join-Path $game "version.dll") "Auric in the game folder"
    Add-Target (Join-Path $game "CryptBase.dll") "Frosty's shim in the game folder"
    Add-Target (Join-Path $game "ModData") "Frosty's ModData packs"
} else {
    Write-Host "Game folder not found (no Axon settings, no EA registry key); pass -GamePath to clean it." -ForegroundColor Yellow
}
if ($KeepAuricConfig) {
    Add-Target "$env:APPDATA\Auric\auric.log" "Auric's log"
    Add-Target "$env:APPDATA\Auric\matches" "Auric's saved matches"
} else {
    Add-Target "$env:APPDATA\Auric" "Auric's log, config (website token) and matches"
}
Add-Target "$env:LOCALAPPDATA\Frosty" "Frosty Mod Manager's config"

$uninstaller = Join-Path $appFolder "unins000.exe"
$shortcuts = @("$([Environment]::GetFolderPath('Desktop'))\Axon.lnk",
               "$([Environment]::GetFolderPath('CommonDesktopDirectory'))\Axon.lnk",
               "$([Environment]::GetFolderPath('Programs'))\Axon",
               "$([Environment]::GetFolderPath('CommonPrograms'))\Axon")

Write-Host ""
Write-Host ("Game folder: " + $(if ($game) { $game } else { "(unknown)" }))
Write-Host "Found:"
foreach ($t in $targets) { Write-Host ("  " + $t.Path.PadRight(70) + " " + $t.What) }
if (Test-Path $uninstaller) { Write-Host "  Axon's uninstaller will run first (silent)" }
foreach ($s in $shortcuts) { if (Test-Path $s) { Write-Host "  $s" } }
if ($targets.Count -eq 0) { Write-Host "  nothing, this PC is already clean" }

if (-not $Delete) {
    Write-Host ""
    Write-Host "Dry run. Add -Delete to remove the above." -ForegroundColor Cyan
    return
}

Write-Host ""
if (Test-Path $uninstaller) {
    Write-Host "Uninstalling Axon..."
    Start-Process -FilePath $uninstaller -ArgumentList "/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART" -Wait
}
foreach ($t in $targets) {
    if (Test-Path -LiteralPath $t.Path) {
        Write-Host "Removing $($t.Path)"
        Remove-Item -LiteralPath $t.Path -Recurse -Force
    }
}
foreach ($s in $shortcuts) {
    if (Test-Path $s) { Write-Host "Removing $s"; Remove-Item -LiteralPath $s -Recurse -Force }
}
Write-Host ""
Write-Host "Done. The next Axon install and start are first-run." -ForegroundColor Green
