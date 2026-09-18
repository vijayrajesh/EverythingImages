@echo off
rem ===========================================================================
rem  Builds everything EverythingImages ships, and all of it is portable:
rem
rem    release.bat           folder, zip and portable installer
rem    release.bat nosetup   folder and zip only, no Inno Setup needed
rem
rem  Out come three things in release\, all the same copy of the app:
rem
rem    EverythingImages-Portable-<ver>\            a folder to run from anywhere
rem    EverythingImages-Portable-<ver>.zip         the same folder, zipped
rem    EverythingImages-Portable-Setup-<ver>.exe   asks for a folder and copies it there
rem
rem  There is deliberately no installing setup: no Start menu, no uninstaller,
rem  no registry. portable.txt beside the exe makes the app keep its index, its
rem  settings and its AI models in a data folder of its own, so the folder is
rem  the whole app - see installer\EverythingImages.iss and Paths.cs.
rem
rem  The version is read from src\EverythingImages\EverythingImages.csproj, the only place
rem  it changes.
rem ===========================================================================
setlocal
title EverythingImages - portable build
cd /d "%~dp0"

set "MODE=%~1"
set "DO_SETUP=1"
if /i "%MODE%"=="nosetup" set "DO_SETUP="
rem  an argument was given and it was not nosetup
if defined MODE if defined DO_SETUP (
    echo [ERROR] Unknown argument "%MODE%" - the only one is nosetup.
    goto :fail
)

rem  Inno Setup is looked for before the long build, not after it.
if defined DO_SETUP (
    call :find_iscc
    if not defined ISCC goto :no_iscc
)

set "VER="
for /f "usebackq delims=" %%V in (`powershell -NoProfile -Command "([xml](Get-Content 'src\EverythingImages\EverythingImages.csproj')).SelectSingleNode('//Version').InnerText"`) do set "VER=%%V"
if not defined VER (
    echo [ERROR] Could not read Version from src\EverythingImages\EverythingImages.csproj
    goto :fail
)
set "PNAME=EverythingImages-Portable-%VER%"

echo ========================================================
echo        EverythingImages %VER% - portable build
echo ========================================================

echo.
echo ==== Step 1: build, test and publish =======================
call "%~dp0build.bat" nopause || goto :fail
if not exist release mkdir release

echo.
echo ==== Step 2: portable folder and zip =======================
rem  The zip is built from a clean staging copy, so it is exactly what was just
rem  published and nothing else.
set "STAGE=obj\portable"
if exist "%STAGE%" rmdir /s /q "%STAGE%"
if exist "release\%PNAME%.zip" del /q "release\%PNAME%.zip"
xcopy "dist" "%STAGE%\%PNAME%\" /e /i /q /y || goto :fail
copy /y "installer\portable.txt" "%STAGE%\%PNAME%\portable.txt" >nul || goto :fail
"%SystemRoot%\System32\tar.exe" -a -c -f "release\%PNAME%.zip" -C "%STAGE%" "%PNAME%" || goto :fail

rem  The folder in release\ is emptied of the app and filled again, so that a
rem  file the app no longer ships stops being there - an xcopy over the top
rem  would leave it, and it would go out with the next hand-made copy. data\ is
rem  the one thing kept: if that folder has been run, it holds that copy's
rem  index, its settings and the AI models it downloaded.
if exist "release\%PNAME%" (
    for /d %%D in ("release\%PNAME%\*") do if /i not "%%~nxD"=="data" rmdir /s /q "%%D"
    del /q "release\%PNAME%\*" >nul 2>&1
)
xcopy "%STAGE%\%PNAME%" "release\%PNAME%\" /e /i /q /y || goto :copy_failed
rmdir /s /q "%STAGE%"

if not defined DO_SETUP goto :done
echo.
echo ==== Step 3: portable installer ============================
"%ISCC%" /DAppVersion=%VER% "installer\EverythingImages.iss" || goto :fail

:done
rem  An installing setup from an older build would still be sitting here, and
rem  shipping it by mistake is exactly what this script is meant to prevent.
if exist "release\EverythingImages-Setup-*.exe" (
    echo.
    echo Deleting an installing setup left by an older build - only portable builds now.
    del /q "release\EverythingImages-Setup-*.exe"
)
echo.
echo ========================================================
echo   SUCCESS - everything is in %CD%\release
echo ========================================================
echo.
echo   Folder: release\%PNAME%
for %%F in ("release\%PNAME%.zip") do echo   Zip:    %%~nxF  [%%~zF bytes]
if defined DO_SETUP for %%F in ("release\EverythingImages-Portable-Setup-%VER%.exe") do echo   Setup:  %%~nxF  [%%~zF bytes]
if not defined DO_SETUP echo   Setup:  skipped - "release.bat" on its own builds it too.
echo.
echo   None needs the .NET 10 Desktop Runtime installed first: the setup asks
echo   before copying, and the exe offers the download when it is started.
if "%~1"=="" pause
exit /b 0

rem ---------------------------------------------------------------------------
:find_iscc
set "PF86=%ProgramFiles(x86)%"
set "ISCC="
for %%P in (ISCC.exe) do if not "%%~$PATH:P"=="" set "ISCC=%%~$PATH:P"
if not defined ISCC if exist "%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe" set "ISCC=%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe"
if not defined ISCC if exist "%PF86%\Inno Setup 6\ISCC.exe" set "ISCC=%PF86%\Inno Setup 6\ISCC.exe"
if not defined ISCC if exist "%ProgramFiles%\Inno Setup 6\ISCC.exe" set "ISCC=%ProgramFiles%\Inno Setup 6\ISCC.exe"
exit /b 0

:no_iscc
echo.
echo [ERROR] Inno Setup 6 builds the portable installer. Install it with:
echo.
echo     winget install JRSoftware.InnoSetup
echo.
echo Or run "release.bat nosetup" for just the folder and the zip.
goto :fail

:copy_failed
echo.
echo [ERROR] Could not update release\%PNAME%
echo         Close EverythingImages if it is running from that folder, then try again.
goto :fail

:fail
echo.
echo [ERROR] Stopped - see the output above.
if "%~1"=="" pause
exit /b 1
