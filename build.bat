@echo off
setlocal EnableDelayedExpansion
cd /d "%~dp0"

rem Builds "Azeroth Survivors Launcher.exe" with the C# compiler that is already part of Windows
rem (.NET Framework 4.8, on every Windows 10 and 11 PC). Nothing has to be installed and nothing is downloaded.
rem The result is one small exe in the "dist" folder. Players need nothing else either.
rem
rem After the build the exe is signed if you made a sign_config.bat (see sign_build.bat), and the SHA-256 of the
rem exe is written to dist\Azeroth Survivors Launcher.sha256.txt.
rem
rem To change the patch notes or the addon list, edit assets\changelog.txt or assets\addons.txt and run this again.

rem On GitHub (CI is set there) the window does not wait for a key press.
set "PAUSE=pause"
if defined CI set "PAUSE=rem"
rem Local builds do not silently replace themselves with the published GitHub exe while being tested.
rem The GitHub Build workflow sets RELEASE_BUILD=true for its public artifact.
set "BUILD_KIND=/define:AZS_LOCAL_BUILD"
if defined RELEASE_BUILD set "BUILD_KIND=/define:AZS_RELEASE_BUILD"

set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
    echo The .NET Framework 4 compiler was not found on this PC:
    echo   %CSC%
    echo Turn on ".NET Framework 4.8 Advanced Services" in "Turn Windows features on or off" and try again.
    %PAUSE%
    exit /b 1
)

for %%f in (src\Program.cs assets\changelog.txt assets\addons.txt assets\launcher-about.txt assets\launcher-changelog.txt assets\logo.png assets\portrait.png assets\AzerothSurvivors.ico app.manifest) do (
    if not exist "%%f" (
        echo Missing file: %%f
        echo Keep the whole folder together: build.bat, app.manifest, the src and the assets folders.
        %PAUSE%
        exit /b 1
    )
)

if not exist dist mkdir dist
if not defined OUT set "OUT=dist\Azeroth Survivors Launcher.exe"
if not defined HASHOUT set "HASHOUT=dist\Azeroth Survivors Launcher.sha256.txt"
if exist "%OUT%" del "%OUT%"

rem every language file in assets\lang is built in (see assets\lang\README.md)
set "LANGRES="
set "CLASSFAVRES="
for %%f in (assets\lang\*.txt) do set "LANGRES=!LANGRES! /resource:assets\lang\%%~nxf,AS.lang.%%~nf.txt"
for %%f in (assets\retro\*.png) do set "LANGRES=!LANGRES! /resource:assets\retro\%%~nxf,AS.retro.%%~nxf"
for %%f in (assets\fonts\*.ttf) do set "LANGRES=!LANGRES! /resource:assets\fonts\%%~nxf,AS.font.%%~nxf"
for %%f in (assets\favorites\class-*.jpg) do set "CLASSFAVRES=!CLASSFAVRES! /resource:assets\favorites\%%~nxf,AS.favorites.%%~nxf"

echo Building %OUT% ...
"%CSC%" /nologo /target:winexe /platform:anycpu /optimize+ /langversion:5 /warn:3 /nowarn:162,429 !BUILD_KIND! ^
  /out:"%OUT%" /win32icon:assets\AzerothSurvivors.ico /win32manifest:app.manifest ^
  /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll ^
  /r:System.Management.dll /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll ^
  /resource:assets\logo.png,AS.logo.png /resource:assets\discord\symbol-blurple.png,AS.discord-blurple.png /resource:assets\discord\symbol-white.png,AS.discord-white.png /resource:assets\portrait.png,AS.portrait.png /resource:assets\gear\dps-up.png,AS.gear.dps-up.png /resource:assets\favorites\codex-aura.png,AS.favorites.codex-aura.png /resource:assets\favorites\heroic-icon.png,AS.favorites.heroic-icon.png /resource:assets\favorites\zones-bosses.jpg,AS.favorites.zones-bosses.jpg /resource:assets\favorites\gear-planner.jpg,AS.favorites.gear-planner.jpg /resource:assets\favorites\jewelry-card.jpg,AS.favorites.jewelry-card.jpg /resource:assets\favorites\talents-button.png,AS.favorites.talents-button.png !CLASSFAVRES! ^
  /resource:assets\AzerothSurvivors.ico,AS.app.ico /resource:assets\changelog.txt,AS.changelog.txt ^
  /resource:assets\addons.txt,AS.addons.txt /resource:assets\launcher-about.txt,AS.launcher-about.txt /resource:assets\guide.txt,AS.guide.txt /resource:assets\game-text.txt,AS.game-text.txt /resource:assets\talents.txt,AS.talents.txt /resource:assets\items.txt,AS.items.txt /resource:videos\big-streamers\videos.txt,AS.big-streamers.txt /resource:assets\gear-stats.txt,AS.gear-stats.txt /resource:assets\gear-sets.txt,AS.gear-sets.txt /resource:assets\transmog.txt,AS.transmog.txt /resource:assets\spell-icons.txt,AS.spell-icons.txt /resource:assets\npc-models.txt,AS.npc-models.txt /resource:assets\item-models.txt,AS.item-models.txt /resource:assets\item-icons.txt,AS.item-icons.txt !LANGRES! ^
  /resource:addons\AZSKit\AZSKit.lua,AS.addon.kit.AZSKit.lua /resource:addons\AZSHandbook\AZSHandbook.toc,AS.addon.AZSHandbook.AZSHandbook.toc /resource:addons\AZSHandbook\AZSHandbook.lua,AS.addon.AZSHandbook.AZSHandbook.lua /resource:addons\AZSMinorMods\AZSMinorMods.toc,AS.addon.AZSMinorMods.AZSMinorMods.toc /resource:addons\AZSMinorMods\AZSMinorMods.lua,AS.addon.AZSMinorMods.AZSMinorMods.lua ^
  src\*.cs
if errorlevel 1 goto fail

if exist "%~dp0sign_build.bat" call "%~dp0sign_build.bat" "%OUT%"
if errorlevel 1 goto fail

set "HASH="
for /f "skip=1 tokens=*" %%H in ('certutil -hashfile "%OUT%" SHA256') do if not defined HASH set "HASH=%%H"
set "HASH=!HASH: =!"
if not defined HASH goto fail
> "%HASHOUT%" echo !HASH!  %OUT%

echo.
echo Done.  %~dp0%OUT%
echo It is a single file: share just the exe (or zip it). Players put it in the folder where the game is (or should be installed).
if not defined CI explorer "%~dp0dist"
%PAUSE%
exit /b 0

:fail
echo.
echo Build failed. See the messages above (copy them and send them over if you want them fixed).
%PAUSE%
exit /b 1
