param(
    [string]$Executable = "dist\scaling-prototype.exe",
    [string]$Output = "build\scaling-matrix"
)
$ErrorActionPreference = "Stop"
Set-Location (Split-Path -Parent $PSScriptRoot)
$exe = (Resolve-Path -LiteralPath $Executable).Path
New-Item -ItemType Directory -Force -Path $Output | Out-Null
$report = @()
# These are simulated working areas, not changes to the user's Windows display settings.
foreach ($display in @(@(1366,728), @(1920,1040), @(2560,1400), @(3840,2080), @(3440,1400))) {
    foreach ($dpi in @(96,120,144,168,192)) {
        $name = "$($display[0])x$($display[1])-$dpi"
        $destination = Join-Path $Output $name
        $arguments = "--preview-scaling `"$destination`" --display $($display[0]) $($display[1]) $dpi"
        $timer = [System.Diagnostics.Stopwatch]::StartNew()
        $process = Start-Process -FilePath $exe -ArgumentList $arguments -WindowStyle Hidden -PassThru
        if (-not $process.WaitForExit(90000)) { $process.Kill(); throw "Preview timed out: $name" }
        $errorFile = Join-Path $destination "error.txt"
        $success = $process.ExitCode -eq 0 -and -not (Test-Path -LiteralPath $errorFile)
        $count = @(Get-ChildItem -LiteralPath $destination -Filter *.png).Count
        $checks = @(Get-Content -LiteralPath (Join-Path $destination "checks.txt") -ErrorAction SilentlyContinue).Count
        $report += [pscustomobject]@{Case=$name; Passed=$success; Screenshots=$count; Checks=$checks; Seconds=[math]::Round($timer.Elapsed.TotalSeconds,1)}
        $report | Export-Csv -NoTypeInformation -LiteralPath (Join-Path $Output "results.csv")
        Write-Output "$name : $success ($count screenshots, $checks checks)"
        if (-not $success -and (Test-Path -LiteralPath $errorFile)) {Get-Content -LiteralPath $errorFile}
    }
}

if(@($report | Where-Object {-not $_.Passed}).Count -gt 0) {throw "Display matrix contains failures; inspect results.csv"}
