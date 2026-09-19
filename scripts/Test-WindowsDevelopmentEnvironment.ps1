[CmdletBinding()]
param(
    [string]$RepositoryRoot = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = 'Stop'
$failures = [System.Collections.Generic.List[string]]::new()

function Read-CommandVersion([string]$Name, [scriptblock]$Command) {
    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        $script:failures.Add("Missing command: $Name")
        return $null
    }
    try { return (& $Command).Trim() } catch {
        $script:failures.Add("Cannot run ${Name}: $($_.Exception.Message)")
        return $null
    }
}

if (-not $IsWindows) { $failures.Add('This development environment must run on Windows.') }
if ([System.Environment]::Is64BitOperatingSystem -ne $true) { $failures.Add('A 64-bit Windows installation is required.') }

$gitVersion = Read-CommandVersion 'git' { git --version }
$pwshVersion = $PSVersionTable.PSVersion.ToString()
if ($PSVersionTable.PSVersion.Major -lt 7) { $failures.Add('PowerShell 7 or later is required.') }

$globalJson = Join-Path $RepositoryRoot 'global.json'
if (-not (Test-Path $globalJson)) { $failures.Add("Missing $globalJson") }
else {
    $requiredDotnet = (Get-Content $globalJson -Raw | ConvertFrom-Json).sdk.version
    $dotnetVersion = Read-CommandVersion 'dotnet' { dotnet --version }
    if ($dotnetVersion -and $dotnetVersion -ne $requiredDotnet) {
        $failures.Add(".NET SDK must be $requiredDotnet; found $dotnetVersion")
    }
}

$nodeVersion = Read-CommandVersion 'node' { node --version }
$npmVersion = Read-CommandVersion 'npm' { npm --version }
if ($nodeVersion) {
    $parsedNode = [version]($nodeVersion.TrimStart('v').Split('-')[0])
    if ($parsedNode -lt [version]'24.12.0') { $failures.Add("Node.js must be >=24.12.0; found $nodeVersion") }
}

$pythonCommand = if (Get-Command python -ErrorAction SilentlyContinue) { 'python' }
                 elseif (Get-Command py -ErrorAction SilentlyContinue) { 'py' }
                 else { $null }
$pythonVersion = $null
if (-not $pythonCommand) { $failures.Add('Python 3.12 or later is required.') }
else {
    $pythonVersion = if ($pythonCommand -eq 'py') { (& py -3 --version 2>&1).ToString() }
                     else { (& python --version 2>&1).ToString() }
    if ($pythonVersion -notmatch 'Python\s+(\d+)\.(\d+)') { $failures.Add("Cannot parse Python version: $pythonVersion") }
    elseif ([int]$Matches[1] -lt 3 -or ([int]$Matches[1] -eq 3 -and [int]$Matches[2] -lt 12)) {
        $failures.Add("Python must be >=3.12; found $pythonVersion")
    }
}

$result = [ordered]@{
    observedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
    windows = $IsWindows
    os = [System.Environment]::OSVersion.VersionString
    is64Bit = [System.Environment]::Is64BitOperatingSystem
    powershell = $pwshVersion
    git = $gitVersion
    dotnet = $dotnetVersion
    node = $nodeVersion
    npm = $npmVersion
    python = $pythonVersion
    repository = (Resolve-Path $RepositoryRoot).Path
    failures = @($failures)
}
$result | ConvertTo-Json -Depth 4
if ($failures.Count -gt 0) { exit 1 }
Write-Host 'PASS: Windows development prerequisites are present.'
