# Keep compiler arguments in a response file: resources exceed cmd.exe's command length limit.
param([Parameter(Mandatory=$true)][string]$Compiler, [Parameter(Mandatory=$true)][string]$Output)
$ErrorActionPreference = 'Stop'
$arguments = New-Object 'System.Collections.Generic.List[string]'
foreach ($value in @('/nologo','/target:winexe','/platform:anycpu','/optimize+','/langversion:5','/warn:3','/nowarn:162,429','/win32icon:assets\AzerothSurvivors.ico','/win32manifest:app.manifest')) { $arguments.Add($value) }
$arguments.Add('/out:"' + $Output + '"')
foreach ($value in @('System.dll','System.Core.dll','System.Drawing.dll','System.Windows.Forms.dll','System.Management.dll','System.IO.Compression.dll','System.IO.Compression.FileSystem.dll','System.Security.dll')) { $arguments.Add('/reference:' + $value) }
foreach ($value in @($env:BUILD_KIND, $env:EXTRA_CSC_FLAGS)) {
    if ($value) { foreach ($match in [regex]::Matches($value, '(?:[^\s"]|"[^"]*")+')) { $arguments.Add($match.Value) } }
}
$resources = @(
    'assets\launcher-announcements.txt,AS.launcher-announcements.txt'
    'assets\slimjim-avatar.png,AS.slimjim-avatar.png'
    'assets\logo.png,AS.logo.png'
    'assets\discord\symbol-blurple.png,AS.discord-blurple.png'
    'assets\discord\symbol-white.png,AS.discord-white.png'
    'assets\portrait.png,AS.portrait.png'
    'assets\gear\dps-up.png,AS.gear.dps-up.png'
    'assets\favorites\codex-aura.png,AS.favorites.codex-aura.png'
    'assets\favorites\heroic-icon.png,AS.favorites.heroic-icon.png'
    'assets\favorites\zones-bosses.jpg,AS.favorites.zones-bosses.jpg'
    'assets\favorites\gear-planner.jpg,AS.favorites.gear-planner.jpg'
    'assets\favorites\jewelry-card.jpg,AS.favorites.jewelry-card.jpg'
    'assets\favorites\talents-button.png,AS.favorites.talents-button.png'
    'assets\native\webp\x64\libwebp.dll,AS.webp.x64.libwebp.dll'
    'assets\native\webp\x64\libsharpyuv.dll,AS.webp.x64.libsharpyuv.dll'
    'assets\native\webp\x86\libwebp.dll,AS.webp.x86.libwebp.dll'
    'assets\native\webp\x86\libsharpyuv.dll,AS.webp.x86.libsharpyuv.dll'
    'assets\AzerothSurvivors.ico,AS.app.ico'
    'assets\changelog.txt,AS.changelog.txt'
    'vendor\SharpCompress\THIRD-PARTY-NOTICES.md,AS.extractor.notices.txt'
    'assets\addons.txt,AS.addons.txt'
    'assets\launcher-about.txt,AS.launcher-about.txt'
    'assets\launcher-changelog.txt,AS.launcher-changelog.txt'
    'assets\guide.txt,AS.guide.txt'
    'assets\game-text.txt,AS.game-text.txt'
    'assets\talents.txt,AS.talents.txt'
    'assets\items.txt,AS.items.txt'
    'videos\big-streamers\videos.txt,AS.big-streamers.txt'
    'assets\gear-stats.txt,AS.gear-stats.txt'
    'assets\gear-sets.txt,AS.gear-sets.txt'
    'assets\transmog.txt,AS.transmog.txt'
    'assets\spell-icons.txt,AS.spell-icons.txt'
    'assets\npc-models.txt,AS.npc-models.txt'
    'assets\item-models.txt,AS.item-models.txt'
    'assets\item-icons.txt,AS.item-icons.txt'
    'addons\AZSKit\AZSKit.lua,AS.addon.kit.AZSKit.lua'
    'addons\AZSHandbook\AZSHandbook.toc,AS.addon.AZSHandbook.AZSHandbook.toc'
    'addons\AZSHandbook\AZSHandbook.lua,AS.addon.AZSHandbook.AZSHandbook.lua'
)
foreach ($value in $resources) { $arguments.Add('"/resource:' + $value + '"') }
# Google Drive sign-in (src\GoogleDrive.cs): the launcher's Google app ID and secret come from the environment (GitHub's
# Actions secrets GOOGLE_CLIENT_ID and GOOGLE_CLIENT_SECRET in the release build), never from the code. Without them the
# launcher is built without Google Drive's sign-in.
if ($env:GOOGLE_CLIENT_ID -and $env:GOOGLE_CLIENT_SECRET) {
    New-Item -ItemType Directory -Path 'build' -Force | Out-Null
    $googleClient = Join-Path (Get-Location) 'build\google-oauth.txt'
    [IO.File]::WriteAllText($googleClient, $env:GOOGLE_CLIENT_ID.Trim() + "`n" + $env:GOOGLE_CLIENT_SECRET.Trim(), (New-Object Text.UTF8Encoding($false)))
    $arguments.Add('"/resource:' + $googleClient + ',AS.google-oauth.txt"')
    Write-Output 'Google Drive sign-in: included.'
} else { Write-Output 'Google Drive sign-in: not included (no GOOGLE_CLIENT_ID / GOOGLE_CLIENT_SECRET).' }
foreach ($file in Get-ChildItem 'vendor\SharpCompress\*.dll' -File) {
    $arguments.Add('"/reference:' + $file.FullName + '"')
    $arguments.Add('"/resource:' + $file.FullName + ',AS.extractor.' + $file.Name + '"')
}
$groups = @(
    @('assets\lang', '*.txt', 'AS.lang.'),
    @('assets\retro', '*.png', 'AS.retro.'),
    @('assets\fonts', '*.ttf', 'AS.font.'),
    @('assets\favorites', 'class-*.jpg', 'AS.favorites.'),
    @('assets\addon-gallery', '*', 'AS.addon-gallery.')
)
foreach ($group in $groups) {
    foreach ($file in Get-ChildItem -LiteralPath $group[0] -File | Where-Object { $_.Name -like $group[1] }) {
        $arguments.Add('"/resource:' + $file.FullName + ',' + $group[2] + $file.Name + '"')
    }
}
foreach ($file in Get-ChildItem 'src\*.cs' -File) { $arguments.Add('"' + $file.FullName + '"') }
New-Item -ItemType Directory -Path 'build' -Force | Out-Null
$responseFile = Join-Path (Get-Location) 'build\launcher-compiler.rsp'
[IO.File]::WriteAllLines($responseFile, $arguments, (New-Object Text.UTF8Encoding($false)))
& $Compiler ('@' + $responseFile)
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
if (!(Test-Path -LiteralPath $Output -PathType Leaf)) { throw 'The compiler did not produce the launcher executable.' }
