@echo off
rem FPE2001-Remake 测试启动器（合成靶子 + 主程序）
setlocal
cd /d "%~dp0"

where dotnet >nul 2>nul
if errorlevel 1 (
    echo 未找到 dotnet。请安装 .NET 10 SDK 并将 dotnet 加入 PATH。
    pause
    exit /b 1
)

echo [1/2] 启动合成靶子进程（SyntheticTarget.x64，固定内存布局）...
start "SyntheticTarget" dotnet run --project "tests\SyntheticTarget.x64\SyntheticTarget.x64.csproj" -c Release -- -noinput -nocounter
timeout /t 2 /nobreak >nul

echo [2/2] 启动 FPE2001-Remake...
dotnet run --project "src\Fpe2001Remake.UI\Fpe2001Remake.UI.csproj" -c Release
echo.
echo 靶子进程仍在运行，测试完可关闭其窗口或任务管理器结束。
pause
