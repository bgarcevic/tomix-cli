# mdl installer — usage:
#   powershell -ExecutionPolicy ByPass -c "irm https://raw.githubusercontent.com/bgarcevic/mdl-cli/main/install/install.ps1 | iex"
# Pin a version:    $env:MDL_VERSION = '0.2.0'; irm ... | iex
# Custom location:  $env:MDL_INSTALL = 'D:\tools\mdl'; irm ... | iex

$ErrorActionPreference = 'Stop'

$Repo = 'bgarcevic/mdl-cli'
$Version = if ($env:MDL_VERSION) { $env:MDL_VERSION } else { 'latest' }
$InstallDir = if ($env:MDL_INSTALL) { $env:MDL_INSTALL } else { Join-Path $env:LOCALAPPDATA 'Programs\mdl' }

# --- detect platform --------------------------------------------------------
$arch = if ([System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture -eq 'Arm64') { 'arm64' } else { 'x64' }
$rid = "win-$arch"
$asset = "mdl-$rid.zip"

# --- resolve URLs -----------------------------------------------------------
$base = if ($Version -eq 'latest') {
    "https://github.com/$Repo/releases/latest/download"
} else {
    "https://github.com/$Repo/releases/download/v$($Version.TrimStart('v'))"
}

$tmp = Join-Path ([System.IO.Path]::GetTempPath()) "mdl-install-$([System.Guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Path $tmp | Out-Null

try {
    Write-Host "downloading " -ForegroundColor Cyan -NoNewline
    Write-Host "$base/$asset"
    Invoke-WebRequest -Uri "$base/$asset" -OutFile (Join-Path $tmp $asset) -UseBasicParsing
    Invoke-WebRequest -Uri "$base/checksums.txt" -OutFile (Join-Path $tmp 'checksums.txt') -UseBasicParsing

    # --- verify checksum -----------------------------------------------------
    $line = Get-Content (Join-Path $tmp 'checksums.txt') | Where-Object { $_ -match [regex]::Escape($asset) + '$' }
    if (-not $line) { throw "no checksum entry for $asset" }
    $expected = ($line -split '\s+')[0].ToLowerInvariant()
    $actual = (Get-FileHash (Join-Path $tmp $asset) -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($expected -ne $actual) { throw "checksum mismatch for $asset" }
    Write-Host "verified " -ForegroundColor Cyan -NoNewline
    Write-Host "sha256 OK"

    # --- install -------------------------------------------------------------
    Expand-Archive -Path (Join-Path $tmp $asset) -DestinationPath $tmp -Force
    New-Item -ItemType Directory -Path $InstallDir -Force | Out-Null
    Copy-Item -Path (Join-Path $tmp "mdl-$rid\*") -Destination $InstallDir -Recurse -Force
    Write-Host "installed " -ForegroundColor Cyan -NoNewline
    Write-Host (Join-Path $InstallDir 'mdl.exe')

    # --- PATH (user scope) ---------------------------------------------------
    $userPath = [Environment]::GetEnvironmentVariable('Path', 'User')
    if (($userPath -split ';') -notcontains $InstallDir) {
        [Environment]::SetEnvironmentVariable('Path', "$userPath;$InstallDir", 'User')
        $env:Path = "$env:Path;$InstallDir"
        Write-Host "added to PATH " -ForegroundColor Cyan -NoNewline
        Write-Host "(restart other terminals to pick it up)"
    }

    & (Join-Path $InstallDir 'mdl.exe') --version
    Write-Host "`nRun ``mdl doctor`` to verify your setup."
}
finally {
    Remove-Item -Path $tmp -Recurse -Force -ErrorAction SilentlyContinue
}
