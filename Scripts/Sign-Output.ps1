<#
.SYNOPSIS
    Signs the Little Pinger build output using signtool.exe.
    Called automatically by MSBuild after each build.
    Uses the PFX file directly so no cert-store lookup is needed.
#>

param([string]$OutputDir = '')

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$projRoot = Split-Path $PSScriptRoot -Parent
$certDir  = Join-Path $projRoot 'cert'
$pfxPath  = Join-Path $certDir 'LittlePinger-signing.pfx'
$pwdFile  = Join-Path $certDir 'pfx-password.txt'

if (-not (Test-Path $pfxPath)) {
    Write-Warning "PFX not found at $pfxPath - skipping signing."
    Write-Warning "Run Scripts\Create-LocalCert.ps1 to generate it."
    exit 0
}

if (-not (Test-Path $pwdFile)) {
    Write-Warning "PFX password file not found at $pwdFile - skipping signing."
    exit 0
}

# Find signtool.exe
$signtool = Get-ChildItem 'C:\Program Files (x86)\Windows Kits\10\bin' -Recurse -Filter 'signtool.exe' -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -like '*x64*' } |
    Sort-Object FullName -Descending |
    Select-Object -First 1 -ExpandProperty FullName

if (-not $signtool) {
    Write-Warning "signtool.exe not found - skipping signing."
    Write-Warning "Install Windows SDK to enable code signing."
    exit 0
}

Write-Host "Using: $signtool"

$pfxPwd = (Get-Content $pwdFile -Raw).Trim()

# Determine the output directory
if ($OutputDir) { $OutputDir = $OutputDir.Trim().Trim('"').TrimEnd('\').TrimEnd('/') }
if (-not $OutputDir -and $env:TargetDir) { $OutputDir = $env:TargetDir.TrimEnd('\').TrimEnd('/') }
if (-not $OutputDir) { $OutputDir = Join-Path $projRoot 'bin\Debug\net8.0-windows' }

$targets = @('LittlePinger.exe', 'LittlePinger.dll') |
    ForEach-Object { Join-Path $OutputDir $_ } |
    Where-Object    { Test-Path $_ }

if (-not $targets) {
    Write-Warning "No signable files found in $OutputDir"
    exit 0
}

foreach ($t in $targets) {
    Write-Host "Signing $t ..."
    $args = @(
        'sign'
        '/fd', 'SHA256'
        '/tr', 'http://timestamp.digicert.com'
        '/td', 'SHA256'
        '/f', $pfxPath
        '/p', $pfxPwd
        $t
    )
    $result = & $signtool @args 2>&1
    Write-Host $result
}

Write-Host "Signing complete."
