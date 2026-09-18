@echo off
setlocal
title EverythingImages
cd /d "%~dp0"
if exist "dist\EverythingImages.exe" (
    start "" "dist\EverythingImages.exe"
    exit /b 0
)
echo No release build yet - building and running the debug version...
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\fetch-ai-engine.ps1 || (pause & exit /b 1)
dotnet run --project src\EverythingImages\EverythingImages.csproj || pause
