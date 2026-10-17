@echo off

setlocal DisableDelayedExpansion
set "saved_dir=%cd%"
set "old_path=%PATH%"

set "PROJECT_DIR=%~dp0build"
set "PROJECT_NAME=build"
set "PROJECT_FRAMEWORK=net10.0"
set "PROJECT_RUNTIME=win-x64"
set "SELF_BUILD=0"
set "SELF_BUILD_ONLY=0"
set "REBUILT=0"

cd /d "%~dp0" || goto :error

rem Match the build helper to the SDK selected by global.json.
dotnet --version | findstr /b /l /c:"11." >nul
if not errorlevel 1 set PROJECT_FRAMEWORK=net11.0

set "BUILD_EXE=%PROJECT_DIR%\bin\Release\%PROJECT_FRAMEWORK%\%PROJECT_RUNTIME%\publish\%PROJECT_NAME%.exe"

if /I "%~1" == "self-build" (
    set "SELF_BUILD_ONLY=1"
    goto :self-build
)
goto :run

:self-build
set "SELF_BUILD=1"
set "REBUILT=1"
dotnet publish ^
    "%PROJECT_DIR%\%PROJECT_NAME%.fsproj" ^
    --configuration Release -f %PROJECT_FRAMEWORK% --tl:on --verbosity d --sc --runtime %PROJECT_RUNTIME%  ^
    /property:TargetFrameworks=%PROJECT_FRAMEWORK% ^
    /property:PublishTrimmed=True ^
    /property:IncludeNativeLibrariesForSelfExtract=True ^
    /property:DebugType=None ^
    /property:DebugSymbols=False ^
    /property:PublishSingleFile=False ^
    /property:InvariantGlobalization=True ^
    /property:PublishAot=True ^
    /property:StripSymbols=True || goto :error

if "%COMPUTERNAME%"=="ALEX-TITAN" (
    echo.
    eza -laB "%PROJECT_DIR%\bin\Release\%PROJECT_FRAMEWORK%\%PROJECT_RUNTIME%\publish" || goto :error
) else (
    echo.
    dir "%PROJECT_DIR%\bin\Release\%PROJECT_FRAMEWORK%\%PROJECT_RUNTIME%\publish" || goto :error
)
set "SELF_BUILD=0"
if "%SELF_BUILD_ONLY%"=="1" goto :ok
goto :run

:run
if not exist "%BUILD_EXE%" goto :missing-build
"%BUILD_EXE%" %*
set "build_exit_code=%errorlevel%"
if "%build_exit_code%"=="0" goto :ok
if not "%build_exit_code%"=="42" goto :report-error
if "%REBUILT%"=="1" (
    echo Self-check still fails after rebuilding the build tool.
    goto :report-error
)
echo Build tool sources changed. Rebuilding...
goto :self-build

:missing-build
if "%REBUILT%"=="0" goto :self-build
echo Self-build did not produce "%BUILD_EXE%".
set "SELF_BUILD=1"
set "build_exit_code=1"
goto :report-error

:ok
set "PATH=%old_path%"
cd /d "%saved_dir%"
exit /b 0

:error
set "build_exit_code=%errorlevel%"
:report-error
if "%SELF_BUILD%"=="1" (
    echo.
    echo Self-build failed. Please check the output above for details.
) else (
    echo.
    echo Build failed. Please check the output above for details.
)
set "PATH=%old_path%"
cd /d "%saved_dir%"
exit /b %build_exit_code%
