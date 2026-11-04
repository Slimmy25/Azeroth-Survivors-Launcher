param([string]$Executable='dist\Azeroth Survivors Launcher - Display Preview.exe',[string]$Output='build\addon-updates-checks')
$ErrorActionPreference='Stop'
Set-Location (Split-Path -Parent $PSScriptRoot)
$exe=(Resolve-Path -LiteralPath $Executable).Path
$root=Join-Path (Get-Location) ($Output+'-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $root | Out-Null
foreach($case in @(@(1920,1040,144),@(1366,728,96))) {
    $destination=Join-Path $root ($case -join '-')
    $process=Start-Process -FilePath $exe -ArgumentList @('--preview-scaling',('"'+$destination+'"'),'--addon-updates','--display',$case[0],$case[1],$case[2]) -WindowStyle Hidden -PassThru
    if(!$process.WaitForExit(25000)) {$process.Kill();throw 'Addon update preview timed out'}
    if(!(Test-Path (Join-Path $destination 'success.txt'))) {
        foreach($file in @('error.txt','native-error.txt')) {if(Test-Path (Join-Path $destination $file)){Get-Content (Join-Path $destination $file)}}
        throw 'Addon update preview failed'
    }
    Write-Output (($case -join '-')+': '+@(Get-Content (Join-Path $destination 'checks.txt')).Count+' checks passed')
}
$root | Set-Content build\addon-updates-final-qa.txt
Write-Output $root
