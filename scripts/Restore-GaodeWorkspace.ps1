[CmdletBinding()]
param(
    [string]$PackageRoot = (Split-Path -Parent $PSScriptRoot),
    [string]$Destination = 'C:\Gaode',
    [string]$RemoteUrl = 'https://github.com/NickYoung618/x.git'
)

$ErrorActionPreference = 'Stop'
if (-not $IsWindows) { throw 'Run this restore script on 64-bit Windows.' }
if (-not [System.Environment]::Is64BitOperatingSystem) { throw 'A 64-bit Windows installation is required.' }

$checksumFile = Join-Path $PackageRoot 'checksums.sha256'
$manifestFile = Join-Path $PackageRoot 'manifest.json'
$bundleFile = Join-Path $PackageRoot 'repository\gaode-repository.bundle'
foreach ($required in @($checksumFile, $manifestFile, $bundleFile)) {
    if (-not (Test-Path $required -PathType Leaf)) { throw "Migration package is missing $required" }
}

foreach ($line in Get-Content $checksumFile) {
    if ([string]::IsNullOrWhiteSpace($line)) { continue }
    if ($line -notmatch '^([0-9a-f]{64})  (.+)$') { throw "Invalid checksum line: $line" }
    $expected = $Matches[1]
    $relative = $Matches[2].Replace('/', [IO.Path]::DirectorySeparatorChar)
    $path = Join-Path $PackageRoot $relative
    if (-not (Test-Path $path -PathType Leaf)) { throw "Missing package file: $relative" }
    $actual = (Get-FileHash -Algorithm SHA256 $path).Hash.ToLowerInvariant()
    if ($actual -ne $expected) { throw "Checksum mismatch: $relative" }
}

$manifest = Get-Content $manifestFile -Raw | ConvertFrom-Json
$repoPath = Join-Path $Destination 'repo'
if (Test-Path $repoPath) { throw "Destination already exists: $repoPath" }
New-Item -ItemType Directory -Path $Destination -Force | Out-Null
git clone $bundleFile $repoPath
if ($LASTEXITCODE -ne 0) { throw 'git clone from migration bundle failed.' }
git -C $repoPath remote set-url origin $RemoteUrl
git -C $repoPath checkout main
if ($LASTEXITCODE -ne 0) { throw 'Cannot check out main from migration bundle.' }
$head = (git -C $repoPath rev-parse HEAD).Trim()
if ($head -ne $manifest.mainCommit) { throw "Restored main $head does not match manifest $($manifest.mainCommit)" }
git -C $repoPath fsck --full
if ($LASTEXITCODE -ne 0) { throw 'git fsck failed.' }

Write-Host "PASS: restored repository $head to $repoPath"
Write-Host 'Next: configure GitHub access, run Test-WindowsDevelopmentEnvironment.ps1, then execute locked restore/build/tests.'
