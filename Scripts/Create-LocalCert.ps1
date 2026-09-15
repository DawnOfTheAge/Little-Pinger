<#
.SYNOPSIS
    Creates a self-signed code-signing certificate for Little Pinger.

.DESCRIPTION
    Step 1 - run as normal user:
        .\Scripts\Create-LocalCert.ps1

    Step 2 - run as Administrator for WDAC machine-wide trust:
        .\Scripts\Install-CertToMachineStores.ps1
#>

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$certDir  = Join-Path $PSScriptRoot '..\cert'
$cerPath  = Join-Path $certDir 'LittlePinger.cer'
$pfxPath  = Join-Path $certDir 'LittlePinger-signing.pfx'
$tpFile   = Join-Path $certDir 'thumbprint.txt'
$pwdFile  = Join-Path $certDir 'pfx-password.txt'
$subject  = 'CN=LittlePinger Local Signing, O=LittlePinger, L=Local'
$pfxPwd   = 'LittlePingerLocal'

# Remove any stale certificate with the same subject
$existing = Get-ChildItem Cert:\CurrentUser\My |
    Where-Object { $_.Subject -eq $subject }
foreach ($old in $existing) {
    Remove-Item -Path "Cert:\CurrentUser\My\$($old.Thumbprint)" -Force
    Write-Host "Removed stale cert: $($old.Thumbprint)"
}

# Create the new self-signed code-signing certificate
Write-Host 'Creating self-signed code-signing certificate...'
$cert = New-SelfSignedCertificate `
    -Subject           $subject `
    -Type              CodeSigningCert `
    -KeyAlgorithm      RSA `
    -KeyLength         2048 `
    -HashAlgorithm     SHA256 `
    -NotAfter          (Get-Date).AddYears(5) `
    -CertStoreLocation 'Cert:\CurrentUser\My'

Write-Host "Created : $($cert.Thumbprint)"
Write-Host "Subject : $($cert.Subject)"
Write-Host "Expires : $($cert.NotAfter.ToString('yyyy-MM-dd'))"

# Ensure output directory exists
New-Item -ItemType Directory -Path $certDir -Force | Out-Null

# Export the public key (.cer)
Export-Certificate -Cert $cert -FilePath $cerPath -Type CERT | Out-Null
Write-Host "Exported: $cerPath"

# Export the private key (.pfx) for direct signing by the build process
$secPwd = ConvertTo-SecureString -String $pfxPwd -Force -AsPlainText
Export-PfxCertificate -Cert $cert -FilePath $pfxPath -Password $secPwd | Out-Null
Write-Host "Exported: $pfxPath  (password stored in $pwdFile)"

# Save thumbprint and password
$cert.Thumbprint | Set-Content -Path $tpFile -Encoding ASCII
$pfxPwd           | Set-Content -Path $pwdFile -Encoding ASCII

Write-Host ''
Write-Host 'SUCCESS - builds will now be signed automatically.'
Write-Host ''
Write-Host 'For WDAC trust, run from an elevated prompt:'
Write-Host '  .\Scripts\Install-CertToMachineStores.ps1'
