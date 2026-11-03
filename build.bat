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
if defined DISPLAY_PREVIEW set "BUILD_KIND=/define:AZS_LOCAL_BUILD,AZS_DISPLAY_PREVIEW"

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
if defined DISPLAY_PREVIEW if not defined OUT set "OUT=dist\Azeroth Survivors Launcher - Display Preview.exe"
if not defined OUT set "OUT=dist\Azeroth Survivors Launcher.exe"
if not defined HASHOUT set "HASHOUT=dist\Azeroth Survivors Launcher.sha256.txt"
if exist "%OUT%" del "%OUT%"

rem every language file in assets\lang is built in (see assets\lang\README.md)
for %%f in (SharpCompress.dll Microsoft.Bcl.AsyncInterfaces.dll System.Text.Encoding.CodePages.dll System.Threading.Tasks.Extensions.dll System.Memory.dll System.Runtime.CompilerServices.Unsafe.dll System.Buffers.dll System.Numerics.Vectors.dll) do (
    if not exist "vendor\SharpCompress\%%f" (
        echo Missing bundled extractor dependency: vendor\SharpCompress\%%f
        echo Restore the pinned, licensed assemblies listed in vendor\SharpCompress\THIRD-PARTY-NOTICES.md.
        %PAUSE%
        exit /b 1
    )
)
echo Building %OUT% ...
powershell -NoProfile -ExecutionPolicy Bypass -File tools\Compile-Launcher.ps1 -Compiler "%CSC%" -Output "%OUT%"
if errorlevel 1 goto fail

if exist "%~dp0sign_build.bat" call "%~dp0sign_build.bat" "%OUT%"
if errorlevel 1 goto fail

set "HASH="
for /f "skip=1 tokens=*" %%H in ('certutil -hashfile "%OUT%" SHA256') do if not defined HASH set "HASH=%%H"
set "HASH=!HASH: =!"
if not defined HASH goto fail
if "!HASH:~63,1!"=="" goto fail
if not "!HASH:~64!"=="" goto fail
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
