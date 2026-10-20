@echo off
rem Buduje pojedynczy, samodzielny FilipCommander.exe w katalogu .\publish
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish
if %errorlevel% neq 0 (
    echo Budowanie nie powiodlo sie.
    pause
    exit /b 1
)
echo.
echo Gotowe: %~dp0publish\FilipCommander.exe
pause
