# Builds and runs the launcher's automated tests (tests\*.cs) with the same C# 5 compiler build.bat uses.
# Used by the Build workflow before the launcher itself is built; runs locally too: powershell -File tests\run-tests.ps1
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path -Parent $PSScriptRoot)
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (!(Test-Path $csc)) { $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
New-Item -ItemType Directory -Path 'build' -Force | Out-Null
$exe = 'build\CloudCopyTests.exe'
& $csc /nologo /target:exe /langversion:5 /warn:3 "/out:$exe" 'tests\CloudCopyTests.cs' 'src\CloudCopy.cs'
if ($LASTEXITCODE -ne 0) { Write-Output '::error::The tests did not compile.'; exit $LASTEXITCODE }
& $exe
if ($LASTEXITCODE -ne 0) { Write-Output '::error::Cloud Save tests failed (see the lines above).'; exit $LASTEXITCODE }
