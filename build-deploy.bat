@echo off
setlocal EnableExtensions

rem ============================================
rem  SelectSpecialCardMod one-click build & deploy script
rem  Double-click to run: build C# -> export PCK -> deploy to game mods folder
rem ============================================

set "PROJ=%~dp0"
if "%PROJ:~-1%"=="\" set "PROJ=%PROJ:~0,-1%"
set "GODOT=D:\myProgram\Godot_v4.5.1-stable_mono_win64\Godot_v4.5.1-stable_mono_win64\Godot_v4.5.1-stable_mono_win64.exe"
set "MODDIR=D:\steam\steamapps\common\Slay the Spire 2\mods\SelectSpecialCardMod"

echo.
echo [1/4] Checking game is not running...
tasklist /fi "imagename eq SlayTheSpire2.exe" 2>nul | find /i "SlayTheSpire2.exe" >nul
if not errorlevel 1 (
    echo ERROR: Slay the Spire 2 is running. Please close it first, then run again.
    pause
    exit /b 1
)

echo [2/4] Building C# (dotnet build)...
dotnet build -c Debug "%PROJ%\SelectSpecialCardMod.csproj"
if errorlevel 1 (
    echo ERROR: Build failed.
    pause
    exit /b 1
)

echo [3/4] Exporting PCK...
if not exist "%PROJ%\build" mkdir "%PROJ%\build"
"%GODOT%" --path "%PROJ%" --headless --export-pack "Windows Desktop" "%PROJ%\build\SelectSpecialCardMod.pck"
if errorlevel 1 (
    echo ERROR: PCK export failed.
    pause
    exit /b 1
)

echo [4/4] Deploying to game mods folder...
if not exist "%MODDIR%" mkdir "%MODDIR%"
copy /y "%PROJ%\.godot\mono\temp\bin\Debug\SelectSpecialCardMod.dll" "%MODDIR%" >nul
copy /y "%PROJ%\SelectSpecialCardMod.json" "%MODDIR%" >nul
copy /y "%PROJ%\build\SelectSpecialCardMod.pck" "%MODDIR%" >nul
del /q "%PROJ%\build\SelectSpecialCardMod.pck" 2>nul

echo.
echo ============================================
echo   Done! Mod deployed to:
echo     %MODDIR%
echo   Restart Slay the Spire 2 to load the new mod.
echo ============================================
pause
