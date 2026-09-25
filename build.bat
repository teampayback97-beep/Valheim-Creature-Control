@echo off
REM ===========================================================================
REM  CreatureControl - build and deploy
REM  Double-click this file. It compiles the mod and drops the DLL straight
REM  into your Gale profile's plugins folder.
REM ===========================================================================
setlocal
cd /d "%~dp0"

where dotnet >nul 2>nul
if errorlevel 1 (
  echo.
  echo   The .NET SDK isn't installed yet.
  echo.
  echo   Install it once with either of these, then run this file again:
  echo.
  echo     winget install Microsoft.DotNet.SDK.8
  echo.
  echo   ...or download "SDK x64" from  https://dotnet.microsoft.com/download
  echo.
  pause
  exit /b 1
)

echo Building CreatureControl...
echo.
dotnet build -c Release -v minimal

if errorlevel 1 (
  echo.
  echo   BUILD FAILED - see the errors above.
  echo.
  pause
  exit /b 1
)

echo.
echo   Build OK. Launch Valheim through Gale to test.
echo.
pause
