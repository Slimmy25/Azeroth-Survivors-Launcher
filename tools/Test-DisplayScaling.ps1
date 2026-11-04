param(
    [string]$Executable = "dist\scaling-prototype.exe",
    [string]$Output = "build\scaling-matrix"
)
$ErrorActionPreference = "Stop"
Set-Location (Split-Path -Parent $PSScriptRoot)
$exe = (Resolve-Path -LiteralPath $Executable).Path
New-Item -ItemType Directory -Force -Path $Output | Out-Null
$runOutput = Join-Path $Output ("run-" + (Get-Date -Format "yyyyMMdd-HHmmss") + "-" + [guid]::NewGuid().ToString("N").Substring(0,8))
if (Test-Path -LiteralPath $runOutput) { throw "Validation output already exists: $runOutput" }
New-Item -ItemType Directory -Path $runOutput | Out-Null
$requiredCaptures = @("gear-warrior.png", "talents-warrior.png", "gear-priest.png", "talents-priest.png",
    "guide.png", "class-paladin.png", "ten-favorites.png", "export-overlay.png", "import-overlay.png",
    "tooltip-owned.png", "message-dialog.png", "message-dialog-dpi-change.png", "setup-0.png", "setup-1.png", "setup-4.png")
$report = @()
# These are simulated working areas, not changes to the user's Windows display settings.
foreach ($display in @(@(1366,728), @(1920,1040), @(2560,1400), @(3840,2080), @(3440,1400))) {
    foreach ($dpi in @(96,120,144,168,192)) {
        $name = "$($display[0])x$($display[1])-$dpi"
        $destination = Join-Path $runOutput $name
        if (Test-Path -LiteralPath $destination) { throw "Case output already exists: $destination" }
        $arguments = "--preview-scaling `"$destination`" --display $($display[0]) $($display[1]) $dpi"
        $timer = [System.Diagnostics.Stopwatch]::StartNew()
        $process = Start-Process -FilePath $exe -ArgumentList $arguments -WindowStyle Hidden -PassThru
        if (-not $process.WaitForExit(90000)) { $process.Kill(); throw "Preview timed out: $name" }
        $errorFile = Join-Path $destination "error.txt"
        $nativeError = Join-Path $destination "native-error.txt"
        $marker = Join-Path $destination "completed.txt"
        $checkLines = @(Get-Content -LiteralPath (Join-Path $destination "checks.txt") -ErrorAction SilentlyContinue)
        $missingCaptures = @($requiredCaptures | Where-Object {
            $capture = Join-Path $destination $_
            -not (Test-Path -LiteralPath $capture) -or (Get-Item -LiteralPath $capture -ErrorAction SilentlyContinue).Length -eq 0
        })
        $allChecksPass = $checkLines.Count -gt 0 -and @($checkLines | Where-Object { $_ -notmatch '^PASS ' }).Count -eq 0
        $success = $process.ExitCode -eq 0 -and (Test-Path -LiteralPath $marker) -and
            -not (Test-Path -LiteralPath $errorFile) -and -not (Test-Path -LiteralPath $nativeError) -and
            $allChecksPass -and $missingCaptures.Count -eq 0
        $count = @(Get-ChildItem -LiteralPath $destination -Filter *.png).Count
        $checks = $checkLines.Count
        $report += [pscustomobject]@{Case=$name; Passed=$success; Screenshots=$count; Checks=$checks; Seconds=[math]::Round($timer.Elapsed.TotalSeconds,1); OutputFolder=$destination}
        $report | Export-Csv -NoTypeInformation -LiteralPath (Join-Path $Output "results.csv")
        $report | Export-Csv -NoTypeInformation -LiteralPath (Join-Path $runOutput "results.csv")
        Write-Output "$name : $success ($count screenshots, $checks checks)"
        if (-not $success -and (Test-Path -LiteralPath $errorFile)) {Get-Content -LiteralPath $errorFile}
        if (-not $success -and (Test-Path -LiteralPath $nativeError)) {Get-Content -LiteralPath $nativeError}
        if (-not $success -and $missingCaptures.Count -gt 0) {Write-Output ("Missing captures: " + ($missingCaptures -join ", "))}
    }
}

if(@($report | Where-Object {-not $_.Passed}).Count -gt 0) {throw "Display matrix contains failures; inspect results.csv"}
