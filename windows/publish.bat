@echo off
setlocal enabledelayedexpansion

echo ============================================
echo  lil agents - Build and Package
echo ============================================
echo.

:: Resolve paths relative to this script
set "SCRIPT_DIR=%~dp0"
set "PROJECT_DIR=%SCRIPT_DIR%LilAgents"
set "INSTALLER_DIR=%SCRIPT_DIR%installer"

:: Step 1: dotnet publish
echo [1/2] Publishing .NET app (Release, win-x64, self-contained)...
echo.
dotnet publish "%PROJECT_DIR%\LilAgents.csproj" ^
    -c Release ^
    -r win-x64 ^
    -p:Platform=x64 ^
    --self-contained ^
    -p:PublishTrimmed=false

if %ERRORLEVEL% neq 0 (
    echo.
    echo ERROR: dotnet publish failed with exit code %ERRORLEVEL%
    exit /b %ERRORLEVEL%
)

echo.
echo    Publish succeeded.
echo.

:: Step 2: Build installer with Inno Setup
echo [2/2] Building installer with Inno Setup...
echo.

:: Find iscc.exe - check common locations
set "ISCC="
if exist "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" (
    set "ISCC=C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
) else if exist "C:\Program Files\Inno Setup 6\ISCC.exe" (
    set "ISCC=C:\Program Files\Inno Setup 6\ISCC.exe"
) else (
    :: Try PATH
    where iscc.exe >nul 2>&1
    if !ERRORLEVEL! equ 0 (
        set "ISCC=iscc.exe"
    )
)

if "!ISCC!"=="" (
    echo.
    echo ERROR: Inno Setup 6 not found.
    echo        Install from https://jrsoftware.org/isdl.php
    echo        or add iscc.exe to your PATH.
    exit /b 1
)

"!ISCC!" "%INSTALLER_DIR%\lilagents.iss"

if %ERRORLEVEL% neq 0 (
    echo.
    echo ERROR: Inno Setup compiler failed with exit code %ERRORLEVEL%
    exit /b %ERRORLEVEL%
)

echo.
echo ============================================
echo  Build complete!
echo  Installer: %INSTALLER_DIR%\Output\
echo ============================================

:: List output
dir /b "%INSTALLER_DIR%\Output\*.exe" 2>nul
