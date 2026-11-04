param([Parameter(Mandatory=$true)][string]$AssemblyPath)
# Run in Windows PowerShell (the launcher targets .NET Framework).
$ErrorActionPreference = 'Stop'
$assembly = [Reflection.Assembly]::LoadFrom((Resolve-Path $AssemblyPath).Path)
$archive = $assembly.GetType('AzerothLauncher.Archive', $true)
$resolve = $archive.GetMethod('GitHubAssetSha256', [Reflection.BindingFlags]'Static,NonPublic')
$install = $archive.GetMethod('InstallAddonZip', [type[]]@([string],[string],[string],[string]))
$headers = @{ 'User-Agent' = 'AZS-integrity-regression' }
$release = Invoke-RestMethod 'https://api.github.com/repos/Slimmy25/Azeroth-Survivors-Launcher/releases/tags/addons' -Headers $headers
$asset = $release.assets | Where-Object name -eq 'BlackwellUI-by-Blackwell.zip' | Select-Object -First 1
if (!$asset) { throw 'Mirror asset missing' }
$actual = $resolve.Invoke($null, @($asset.browser_download_url))
if (('sha256:' + $actual) -ne $asset.digest) { throw 'Resolved digest differs from the hosting release' }
Write-Output 'PASS: mirror resolves its own GitHub asset digest'
$root = Join-Path ([IO.Path]::GetTempPath()) ('azs-integrity-' + [guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($root) | Out-Null
$assembly.GetType('AzerothLauncher.Util', $true).GetField('dataDir', [Reflection.BindingFlags]'Static,NonPublic').SetValue($null, $root)
try {
    $destination = Join-Path $root 'valid'
    $install.Invoke($null, @([string]$asset.browser_download_url, [string]$destination, $null, $null)) | Out-Null
    if (!(Get-ChildItem $destination -Recurse -Filter '*.toc')) { throw 'Verified ZIP did not extract an addon' }
    Write-Output 'PASS: verified mirror ZIP downloads and installs'
    $source = Join-Path $root 'invalid.zip'
    [IO.File]::WriteAllText($source, 'A deliberately incorrect download')
    $rejected = $false
    $destination = Join-Path $root 'rejected'
    try { $install.Invoke($null, @([string]$source, [string]$destination, $null, [string]('0' * 64))) | Out-Null }
    catch {
        if ($_.Exception.InnerException.Message -notlike '*does not match the SHA-256*') { throw }
        $rejected = $true
    }
    if (!$rejected -or (Get-ChildItem $destination -Recurse)) { throw 'Mismatched download was not rejected before extraction' }
    Write-Output 'PASS: checksum mismatch rejects payload before extraction'
} finally {
    # Only remove this test's explicitly created temporary directory.
    Remove-Item -LiteralPath $root -Recurse -Force
}
