# Signs files with the code-signing certificate kept in the repository secrets:
# SIGNING_CERT (the .pfx file, base64) and SIGNING_PASSWORD. Called by the CI pipeline.
param([Parameter(Mandatory)][string[]] $Files)
$ErrorActionPreference = 'Stop'

$signtool = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin\*\x64\signtool.exe" |
    Sort-Object { [version]$_.Directory.Parent.Name } |
    Select-Object -Last 1
if (-not $signtool) { throw 'signtool.exe introuvable : le SDK Windows manque.' }

$pfx = Join-Path $env:RUNNER_TEMP 'signing.pfx'
[IO.File]::WriteAllBytes($pfx, [Convert]::FromBase64String($env:SIGNING_CERT))
try {
    # /tr: a timestamp keeps the signature valid after the certificate expires.
    & $signtool.FullName sign /fd SHA256 /tr http://timestamp.digicert.com /td SHA256 /f $pfx /p $env:SIGNING_PASSWORD @Files
    if ($LASTEXITCODE) { throw "signtool a échoué (code $LASTEXITCODE)." }
}
finally {
    Remove-Item $pfx -Force
}
