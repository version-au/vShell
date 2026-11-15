@echo off
rem Double-click to build. Needs the .NET 10 SDK (https://dotnet.microsoft.com/download/dotnet/10.0).
rem Produces dist\vShell.exe (self-contained: nothing else to install), and the plugins that changed since they were last built
rem ("build.cmd all" builds every plugin again). dist\site\ = the update site's files (vsuite.vrsion.com; "build.cmd pre" = a pre-release).
rem Nothing goes to GitHub any more (the bridge release was 0.38.190).
cd /d "%~dp0"
dotnet --version >nul 2>&1 || (echo .NET SDK not found. Install .NET 10 SDK first. & pause & exit /b 1)
rem the newest vSuite.Core library first (the submodule, committed if it moved) - vSuite.Core\scripts\suite-core.ps1
if not exist vSuite.Core\scripts\suite-core.ps1 git submodule update --init vSuite.Core
powershell -NoProfile -ExecutionPolicy Bypass -File "vSuite.Core\scripts\suite-core.ps1"
if errorlevel 1 (echo. & echo BUILD STOPPED - vSuite.Core, see above. & pause & exit /b 1)
dotnet publish vShell.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:PublishReadyToRun=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none -o dist
if errorlevel 1 (echo. & echo BUILD FAILED - copy the errors above and send them to me. & pause & exit /b 1)
rem for plugin authors: the vShell.dll a plugin compiles against (the exe is a single file) - dist\sdk\, give it out with docs\plugins.md
if not exist dist\sdk mkdir dist\sdk
dotnet build vShell.csproj -c Release -v q -nologo >nul
copy /y "bin\Release\net10.0-windows\vShell.dll" dist\sdk\ >nul
copy /y "docs\plugins.md" dist\sdk\ >nul
rem the plugin API (what's marked [PluginApi]): compared with docs\plugin-api.txt - stops when something plugins use went or changed
del /q dist\plugin-api.txt dist\plugin-api.txt.failed.txt 2>nul
start "" /wait "dist\vShell.exe" --plugin-api "%~dp0dist\plugin-api.txt"
if not exist dist\plugin-api.txt (echo. & echo PLUGIN API LIST FAILED - see dist\plugin-api.txt.failed.txt & pause & exit /b 1)
powershell -NoProfile -ExecutionPolicy Bypass -File "scripts\plugin-api.ps1"
if errorlevel 1 (echo. & echo BUILD STOPPED - the plugin API changed, see above. & pause & exit /b 1)
copy /y dist\plugin-api.txt dist\sdk\ >nul
rem the plugins (plugins\<Name>\, and others' accepted ones in third-party\): only the ones that changed - see scripts\plugins.ps1
set ALL=
if /i "%~1"=="all" set ALL=-All
powershell -NoProfile -ExecutionPolicy Bypass -File "scripts\plugins.ps1" %ALL%
if errorlevel 1 (echo. & echo PLUGINS FAILED - see above. & pause & exit /b 1)
rem dist\plugins.json: the plugins anyone can add from the Plugins page (read from each DLL's manifest) - goes on the site with them
del /q dist\plugins.json dist\plugins.txt 2>nul
start "" /wait "dist\vShell.exe" --plugin-catalog "%~dp0dist"
if not exist dist\plugins.json (echo. & echo PLUGIN LIST FAILED - see dist\plugins.txt & pause & exit /b 1)
rem the installer (Inno Setup 6, installer\vShell.iss) - in every release; build.cmd nosetup skips it
set WANTSETUP=1
for %%a in (%*) do if /i "%%~a"=="nosetup" set WANTSETUP=
set ISCC=%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe
if not exist "%ISCC%" set ISCC=%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe
rem an older installer is never put up with this version: dist\setup is emptied first
del /q dist\setup\*.exe 2>nul
if not defined WANTSETUP (echo No installer this time - nosetup.) else if not exist "%ISCC%" (echo Inno Setup 6 not found - no installer made.) else (
  "%ISCC%" /Q installer\vShell.iss
  if errorlevel 1 (echo. & echo INSTALLER FAILED - see above. & pause & exit /b 1)
  echo Installer: %~dp0dist\setup\
)
echo Plugins listed in dist\plugins.json:
type dist\plugins.txt
echo.
echo Built: %~dp0dist\vShell.exe
rem release notes for this version, taken from CHANGELOG.md
powershell -NoProfile -ExecutionPolicy Bypass -File "scripts\release-notes.ps1"
rem the update site's files in dist\site\ (exe, installer, latest.json, changelog.md, the plugins) - build.cmd pre: a pre-release
set SITEPRE=
for %%a in (%*) do if /i "%%~a"=="pre" set SITEPRE=-Pre
powershell -NoProfile -ExecutionPolicy Bypass -File "scripts\site.ps1" %SITEPRE%
if errorlevel 1 (echo. & echo UPDATE SITE FILES FAILED - see above. & pause & exit /b 1)
rem "build.cmd upload" (with "pre" for a pre-release): dist\site\ straight onto the update site - SFTP with the vsuite-site key
rem (%USERPROFILE%\.ssh\config), latest.json last, then checked over https (vSuite.Core\scripts\site-upload.ps1)
set UPLOAD=
for %%a in (%*) do if /i "%%~a"=="upload" set UPLOAD=1
if defined UPLOAD powershell -NoProfile -ExecutionPolicy Bypass -File "vSuite.Core\scripts\site-upload.ps1" -Site dist\site
if defined UPLOAD if errorlevel 1 (echo. & echo UPLOAD FAILED - see above. & pause & exit /b 1)
echo To update only plugins later: plugins.cmd, then copy dist\site\ up the same way.
pause
