@echo off
cd /d "%~dp0"
dotnet build DesktopFolderPrototype.csproj --nologo --output bin\LayoutFix
if errorlevel 1 exit /b 1
start "" "%~dp0bin\LayoutFix\DesktopFolderPrototype.exe" %*
