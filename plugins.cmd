@echo off
rem Builds only the plugins (not vShell) - for updating plugins in a release without a new vShell. Only the ones that changed since
rem they were last built ("plugins.cmd all" builds every one again). Needs dist\vShell.exe from build.cmd (0.38.88 or later).
rem Then copy dist\site\ over the update site's root (vsuite.vrsion.com) - vShells with the same plugin API pick the updates up.
rem "plugins.cmd pre": into the pre-release's plugins (vshell\pre\plugins\).
cd /d "%~dp0"
if not exist dist\vShell.exe (echo dist\vShell.exe isn't there - run build.cmd first. & pause & exit /b 1)
set ALL=
if /i "%~1"=="all" set ALL=-All
rem code signing (the Certum certificate, through SimplySign Desktop - connect it first): each plugin built is signed (scripts\plugins.ps1)
rem "nosign" skips it; an upload needs it
set SIGN=1
for %%a in (%*) do if /i "%%~a"=="nosign" set SIGN=
set SIGNREQ=
for %%a in (%*) do if /i "%%~a"=="upload" set SIGNREQ=-Require
powershell -NoProfile -ExecutionPolicy Bypass -File "scripts\plugins.ps1" %ALL%
if errorlevel 1 (echo. & echo PLUGINS FAILED - see above. & pause & exit /b 1)
del /q dist\plugins.json dist\plugins.txt 2>nul
start "" /wait "dist\vShell.exe" --plugin-catalog "%~dp0dist"
if not exist dist\plugins.json (echo. & echo PLUGIN LIST FAILED - see dist\plugins.txt & pause & exit /b 1)
set SITEPRE=
for %%a in (%*) do if /i "%%~a"=="pre" set SITEPRE=-Pre
powershell -NoProfile -ExecutionPolicy Bypass -File "scripts\site.ps1" -PluginsOnly %SITEPRE%
if errorlevel 1 (echo. & echo UPDATE SITE FILES FAILED - see above. & pause & exit /b 1)
rem "plugins.cmd upload": straight onto the update site (vSuite.Core\scripts\site-upload.ps1)
set UPLOAD=
for %%a in (%*) do if /i "%%~a"=="upload" set UPLOAD=1
if defined UPLOAD if not defined SIGN (echo. & echo UPLOAD STOPPED - this build isn't signed. Connect SimplySign Desktop and build again. & pause & exit /b 1)
if defined UPLOAD powershell -NoProfile -ExecutionPolicy Bypass -File "vSuite.Core\scripts\site-upload.ps1" -Site dist\site
if defined UPLOAD if errorlevel 1 (echo. & echo UPLOAD FAILED - see above. & pause & exit /b 1)
echo.
echo Plugins listed in dist\plugins.json:
type dist\plugins.txt
echo.
echo Copy everything in %~dp0dist\site\ over the update site's root (the files there are replaced). Plugins update by
echo themselves (or Settings ^> Plugins ^> Check for updates).
pause
