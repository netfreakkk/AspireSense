@echo off
setlocal
cd /d "%~dp0"

echo ========================================================
echo  AspireSense Production Release Build System
echo ========================================================
echo.

echo [1/3] Compiling AspireSense Application (AspireSense.exe)...
C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /target:winexe /platform:x64 /win32manifest:"%~dp0app.manifest" /win32icon:"%~dp0app.ico" /out:"%~dp0AspireSense.exe" /r:System.dll,System.Xaml.dll,"C:\Windows\Microsoft.NET\Framework64\v4.0.30319\System.Management.dll","C:\Windows\Microsoft.NET\Framework64\v4.0.30319\WPF\PresentationCore.dll","C:\Windows\Microsoft.NET\Framework64\v4.0.30319\WPF\PresentationFramework.dll","C:\Windows\Microsoft.NET\Framework64\v4.0.30319\WPF\WindowsBase.dll" "%~dp0AspireSense.cs"
if %errorlevel% neq 0 (
    echo [FAILED] Application compilation failed.
    pause
    exit /b %errorlevel%
)
echo [OK] AspireSense.exe generated.
echo.

echo [2/3] Compiling Standalone Installer (AspireSense-Setup.exe)...
C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe "@%~dp0build_installer.rsp"
if %errorlevel% neq 0 (
    echo [FAILED] Installer compilation failed.
    pause
    exit /b %errorlevel%
)
echo [OK] AspireSense-Setup.exe generated.
echo.

echo [3/3] Generating Portable Release Zip...
powershell -NoProfile -ExecutionPolicy Bypass -Command "& { Add-Type -AssemblyName System.IO.Compression.FileSystem; $stage = Join-Path '%~dp0' 'AspireSense-v1.1.0'; if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }; New-Item -ItemType Directory -Path $stage | Out-Null; Copy-Item '%~dp0AspireSense.exe' $stage; Copy-Item '%~dp0InsydeDCHU.dll' $stage; Copy-Item '%~dp0logo.png' $stage; Copy-Item '%~dp0app.ico' $stage; Copy-Item '%~dp0README.md' $stage; Copy-Item '%~dp0LICENSE' $stage; $zip = Join-Path '%~dp0' 'AspireSense-Portable-v1.1.0.zip'; if (Test-Path $zip) { Remove-Item $zip -Force }; [System.IO.Compression.ZipFile]::CreateFromDirectory($stage, $zip); Remove-Item $stage -Recurse -Force; Write-Host '[OK] Portable zip created.' }"
echo.

echo ========================================================
echo  BUILD COMPLETE! Release Artifacts:
echo   - Installer: AspireSense-Setup.exe
echo   - Portable:  AspireSense-Portable-v1.1.0.zip
echo   - App:       AspireSense.exe
echo ========================================================
echo.
if "%~1"=="" pause
