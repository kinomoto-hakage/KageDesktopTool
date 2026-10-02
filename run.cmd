@echo off
rem 从源码启动正式程序；保留原型目录作为行为对照。
cd /d "%~dp0"
rtk proxy dotnet restore src/KageDesktopTool/KageDesktopTool.csproj --configfile NuGet.Config
if errorlevel 1 exit /b %errorlevel%
rtk proxy dotnet run --project src/KageDesktopTool/KageDesktopTool.csproj --no-restore -- %*
