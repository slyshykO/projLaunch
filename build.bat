@echo off

setlocal
set saved_dir=%cd%
set old_path=%PATH%

set PROJECT_DIR=%~dp0build
set PROJECT_NAME=build
set PROJECT_FRAMEWORK=net10.0
set PROJECT_RUNTIME=win-x64

if "%1%" == "self-build" (
    dotnet publish ^
        %PROJECT_DIR%\%PROJECT_NAME%.fsproj ^
        --configuration Release -f %PROJECT_FRAMEWORK% --tl:on --verbosity d --sc --runtime %PROJECT_RUNTIME%  ^
        /property:PublishTrimmed=True ^
        /property:IncludeNativeLibrariesForSelfExtract=True ^
        /property:DebugType=None ^
        /property:DebugSymbols=False ^
        /property:PublishSingleFile=False ^
        /property:InvariantGlobalization=True ^
        /property:PublishAot=True ^
        /property:StripSymbols=True  || goto :error

        IF "%COMPUTERNAME%"=="ALEX-TITAN" ( 
            echo.
            eza -laB "%PROJECT_DIR%\bin\Release\%PROJECT_FRAMEWORK%\%PROJECT_RUNTIME%\publish" || goto :error
        )

    goto ok
)

cd %~dp0

"%PROJECT_DIR%\bin\Release\%PROJECT_FRAMEWORK%\%PROJECT_RUNTIME%\publish\%PROJECT_NAME%.exe" %* || goto :error

:ok
PATH=%OLD_PATH%
cd /d %saved_dir%
exit /b 0

:error
PATH=%OLD_PATH%
cd /d %saved_dir%
exit /b %errorlevel%
