@echo off
setlocal EnableExtensions DisableDelayedExpansion
rem Double-click: build, package and deploy. Command line: deploy, package or help.
rem Keep ASCII output so this script works with any Windows console code page.
set "PauseAtEnd=0"
set "Result=0"
set "Target=DeployMod"
set "EnteredProject=0"
if "%~1"=="" set "PauseAtEnd=1"
if not "%~2"=="" goto BadArguments
if "%~1"=="" goto Start
if /I "%~1"=="deploy" goto Start
if /I "%~1"=="package" (
    set "Target=PackageMod"
    goto Start
)
if /I "%~1"=="help" goto Help
if /I "%~1"=="/help" goto Help
if /I "%~1"=="/?" goto Help
goto BadArguments

:Start
pushd "%~dp0"
if errorlevel 1 (
    echo [ERROR] Cannot enter the project directory.
    set "Result=1"
    goto Finish
)
set "EnteredProject=1"
if not exist "SelectSpecialCardMod.csproj" (
    echo [ERROR] SelectSpecialCardMod.csproj is missing beside this script.
    set "Result=1"
    goto Finish
)
where dotnet >nul 2>&1
if errorlevel 1 (
    echo [ERROR] dotnet is not available. Install the .NET 9 SDK and reopen the terminal.
    set "Result=1"
    goto Finish
)
if /I "%Target%"=="PackageMod" goto Build
powershell.exe -NoProfile -NonInteractive -Command "if (Get-Process -Name SlayTheSpire2,sts2 -ErrorAction SilentlyContinue) { exit 10 }; exit 0"
if errorlevel 10 (
    echo [ERROR] Slay the Spire 2 is running. Close the game before deploying.
    set "Result=10"
    goto Finish
)
if errorlevel 1 (
    echo [ERROR] Cannot check whether the game is running. Deployment stopped.
    set "Result=1"
    goto Finish
)

:Build
echo Project: %CD%
echo Target: %Target% / Release
echo Machine paths come from the project and optional Local.Build.props.
echo.
call dotnet msbuild "SelectSpecialCardMod.csproj" -restore -t:%Target% -p:Configuration=Release -nologo
set "Result=%ERRORLEVEL%"
if not "%Result%"=="0" (
    echo.
    echo [ERROR] Build, packaging or deployment failed. See the errors above.
    goto Finish
)
echo.
if /I "%Target%"=="DeployMod" (
    echo [OK] DLL, PCK and JSON deployed to Sts2Dir\mods\SelectSpecialCardMod.
    echo You can now start the game.
) else (
    echo [OK] Package created in PackageDir. Default: artifacts\package.
    echo Game files were not updated.
)
goto Finish

:BadArguments
echo [ERROR] Invalid arguments.
set "Result=2"
:Help
echo Usage: DeployMod.cmd [deploy ^| package ^| help]
echo   No arguments: build, package, deploy and pause before closing.
echo   deploy:       build, package and deploy; return the exit code.
echo   package:      build and package only; do not update game files.
echo   help:         show this message.
echo Close the game before deploying. RitsuLib 0.6.7 or later is required.

:Finish
if "%EnteredProject%"=="1" popd
if "%PauseAtEnd%"=="1" pause
endlocal & exit /b %Result%
