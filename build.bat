@echo off
setlocal
title EverythingImages - release build
cd /d "%~dp0"
echo ========================================================
echo        EverythingImages - release build
echo ========================================================
echo.
where dotnet >nul 2>&1 || (
    echo [ERROR] The .NET SDK is not installed. Get the .NET 10 SDK from https://dot.net
    goto :fail
)
echo [1/3] AI engine (runs the models on the GPU or the CPU)...
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\fetch-ai-engine.ps1 || goto :fail
echo.
echo [2/3] Tests (set EVERYTHINGIMAGES_TEST_IMAGES to a folder of images to include the end-to-end test)...
dotnet test tests\EverythingImages.Tests\EverythingImages.Tests.csproj -c Release || goto :fail
echo.
echo [3/3] Single-file exe...
if exist dist rmdir /s /q dist
dotnet publish src\EverythingImages\EverythingImages.csproj -c Release -o dist || goto :fail

if not exist "dist\ai-engine\llama-mtmd-cli.exe" (
    echo [ERROR] dist\ai-engine is missing - the AI engine was not copied.
    goto :fail
)
echo.
echo ========================================================
echo   SUCCESS
echo   Run:  dist\EverythingImages.exe
echo   Ship: EverythingImages.exe plus the ai-engine folder beside it.
echo   Needs the .NET 10 Desktop Runtime on the target PC.
echo ========================================================
if /i not "%~1"=="nopause" pause
exit /b 0

:fail
echo.
echo [ERROR] Build failed - see the output above.
if /i not "%~1"=="nopause" pause
exit /b 1
