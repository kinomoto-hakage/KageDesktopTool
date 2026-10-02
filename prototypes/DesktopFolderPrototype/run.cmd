@echo off
cd /d "%~dp0"
dotnet build DesktopFolderPrototype.csproj --nologo --output bin\DragFix
if errorlevel 1 exit /b 1
start "" "%~dp0bin\DragFix\DesktopFolderPrototype.exe" %*
