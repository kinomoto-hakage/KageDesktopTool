@echo off
cd /d "%~dp0"
dotnet run --project DesktopFolderPrototype.csproj -- %*
