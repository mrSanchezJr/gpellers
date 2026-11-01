@echo off
echo ============================================================
echo   Building Gpellers Releases (Self-Contained Single-File)
echo ============================================================

echo.
echo [1/2] Publishing Windows x64 (Standalone .exe)...
dotnet publish Gpellers.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o bin\Publish\win-x64
if %ERRORLEVEL% NEQ 0 (
    echo [ERROR] Windows build failed!
    pause
    exit /b %ERRORLEVEL%
)

echo.
echo [2/2] Publishing Linux x64 (Standalone binary)...
dotnet publish Gpellers.csproj -c Release -r linux-x64 --self-contained true -p:PublishSingleFile=true -o bin\Publish\linux-x64
if %ERRORLEVEL% NEQ 0 (
    echo [ERROR] Linux build failed!
    pause
    exit /b %ERRORLEVEL%
)

echo.
echo ============================================================
echo   SUCCESS! Standalone builds are ready:
echo   - Windows x64: bin\Publish\win-x64\Gpellers.exe
echo   - Linux x64:   bin\Publish\linux-x64\Gpellers
echo ============================================================
pause
