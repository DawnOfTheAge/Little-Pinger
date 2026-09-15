#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Installs the Little Pinger signing certificate into the LocalMachine
    trust stores required by WDAC.

.DESCRIPTION
    Run ONCE from an elevated (Administrator) PowerShell prompt AFTER
    Create-LocalCert.ps1 has been executed:

        .\Scripts\Install-CertToMachineStores.ps1

    Installs cert\LittlePinger.cer into:
        Cert:\LocalMachine\Root              (Trusted Root Certification Authorities)
        Cert:\LocalMachine\TrustedPublisher  (Trusted Publishers)

    Both stores are checked by WDAC before allowing a signed binary to run.
    You only need to do this once per machine; the certificate is valid for 5 years.
#>

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$cerPath = Join-Path $PSScriptRoot '..\cert\LittlePinger.cer'

if (-not (Test-Path $cerPath)) {
    Write-Error "Certificate not found at $cerPath.`nRun Scripts\Create-LocalCert.ps1 first."
    exit 1
}

$certBytes = [System.IO.File]::ReadAllBytes($cerPath)
$x509      = [System.Security.Cryptography.X509Certificates.X509Certificate2]::new($certBytes)

Write-Host "Certificate: $($x509.Subject)"
Write-Host "Thumbprint : $($x509.Thumbprint)"
Write-Host "Valid until: $($x509.NotAfter.ToString('yyyy-MM-dd'))"
Write-Host ""

$storeNames = @(
    [System.Security.Cryptography.X509Certificates.StoreName]::Root,
    [System.Security.Cryptography.X509Certificates.StoreName]::TrustedPublisher
)

foreach ($storeName in $storeNames) {
    $store = [System.Security.Cryptography.X509Certificates.X509Store]::new(
                 $storeName,
                 [System.Security.Cryptography.X509Certificates.StoreLocation]::LocalMachine)
    try {
        $store.Open([System.Security.Cryptography.X509Certificates.OpenFlags]::ReadWrite)

        # Skip if already installed
        $already = $store.Certificates | Where-Object { $_.Thumbprint -eq $x509.Thumbprint }
        if ($already) {
            Write-Host "Already in LocalMachine\$storeName — skipped."
            continue
        }

        $store.Add($x509)
        Write-Host "Installed  → LocalMachine\$storeName"
    }
    finally { $store.Close() }
}

Write-Host ""
Write-Host "Done. LittlePinger.exe is now trusted by WDAC on this machine."
