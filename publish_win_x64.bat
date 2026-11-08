@echo off
setlocal
cd /d "%~dp0"

echo =============================================
echo LibraryPrintApp - Windows x64 Release Publish
echo =============================================
where dotnet >nul 2>nul
if errorlevel 1 (
  echo [ERROR] .NET SDK is not installed or dotnet is not in PATH.
  echo Install the .NET 8 SDK, then run this file again.
  pause
  exit /b 1
)

echo.
echo [1/2] Restoring NuGet packages...
dotnet restore "%~dp0LibraryPrintApp.csproj"
if errorlevel 1 goto :failed

echo.
echo [2/2] Publishing self-contained Windows x64 files...
if not exist "%~dp0publish" mkdir "%~dp0publish"
if exist "%~dp0publish\win-x64" rmdir /s /q "%~dp0publish\win-x64"
dotnet publish "%~dp0LibraryPrintApp.csproj" -c Release -r win-x64 --self-contained true -p:UseAppHost=true -p:PublishSingleFile=false -o "%~dp0publish\win-x64"
if errorlevel 1 goto :failed

echo.
echo Publish completed. Output folder:
echo %~dp0publish\win-x64
echo.
echo Copy the complete folder to the target PC, then test startup, printing, backup, and restore.
pause
exit /b 0

:failed
echo.
echo [ERROR] Publish failed. Read the compiler/build messages above.
pause
exit /b 1
