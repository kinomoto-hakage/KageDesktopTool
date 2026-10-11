@echo off
rem 从源码启动正式程序，需要 .NET 10 SDK。
cd /d "%~dp0"
dotnet restore src/KageDesktopTool/KageDesktopTool.csproj --configfile NuGet.Config
if errorlevel 1 exit /b %errorlevel%
dotnet run --project src/KageDesktopTool/KageDesktopTool.csproj --no-restore -- %*
