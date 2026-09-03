@echo off
chcp 65001 >nul
echo ================================================
echo    GR 扩展屏幕 - 构建脚本
echo ================================================
echo.

set DOTNET_VERSION=6.0
set ANDROID_API=34

:menu
echo 请选择操作:
echo 1. 构建 Windows 主机端
echo 2. 构建 Android 客户端
echo 3. 全部构建
echo 4. 清理
echo 5. 退出
echo.
set /p choice=请输入选项 (1-5): 

if "%choice%"=="1" goto build_server
if "%choice%"=="2" goto build_client
if "%choice%"=="3" goto build_all
if "%choice%"=="4" goto clean
if "%choice%"=="5" goto end

:build_server
echo.
echo [1/2] 正在构建 Windows 主机端...
cd /d "%~dp0server\ScreenServer"
dotnet restore
dotnet build -c Release
if %ERRORLEVEL% NEQ 0 (
    echo [错误] 构建失败!
    goto end
)
echo.
echo [成功] Windows 主机端构建完成!
echo 可执行文件: server\ScreenServer\bin\Release\net6.0-windows\ScreenServer.exe
goto menu

:build_client
echo.
echo [2/2] 正在构建 Android 客户端...
cd /d "%~dp0client"
call gradlew assembleDebug
if %ERRORLEVEL% NEQ 0 (
    echo [错误] 构建失败!
    goto end
)
echo.
echo [成功] Android 客户端构建完成!
echo APK: client\app\build\outputs\apk\debug\app-debug.apk
goto menu

:build_all
echo.
echo 开始全部构建...
call :build_server_client
call :build_android_client
echo.
echo [成功] 全部构建完成!
goto menu

:build_server_client
echo.
echo 构建 Windows 主机端...
cd /d "%~dp0server\ScreenServer"
dotnet restore
dotnet build -c Release
exit /b

:build_android_client
echo.
echo 构建 Android 客户端...
cd /d "%~dp0client"
call gradlew assembleDebug
exit /b

:clean
echo.
echo 清理构建产物...
cd /d "%~dp0server\ScreenServer"
dotnet clean
cd /d "%~dp0client"
call gradlew clean
echo.
echo [完成] 清理完成!
goto menu

:end
echo.
echo 按任意键退出...
pause >nul
