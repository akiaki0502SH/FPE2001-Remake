@echo off
rem FPE2001-Remake 主程序启动器
setlocal
cd /d "%~dp0"

where dotnet >nul 2>nul
if errorlevel 1 (
    echo 未找到 dotnet。请安装 .NET 10 SDK 并将 dotnet 加入 PATH。
    pause
    exit /b 1
)

dotnet run --project "src\Fpe2001Remake.UI\Fpe2001Remake.UI.csproj" -c Release
