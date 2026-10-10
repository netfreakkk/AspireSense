@echo off
echo ========================================================
echo Compiling AspireSense Production Release...
echo ========================================================

C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /target:winexe /platform:x64 /win32icon:"%~dp0app.ico" /out:"%~dp0AspireSense.exe" /r:System.dll,System.Xaml.dll,"C:\Windows\Microsoft.NET\Framework64\v4.0.30319\WPF\PresentationCore.dll","C:\Windows\Microsoft.NET\Framework64\v4.0.30319\WPF\PresentationFramework.dll","C:\Windows\Microsoft.NET\Framework64\v4.0.30319\WPF\WindowsBase.dll" "%~dp0AspireSense.cs"

if %errorlevel% equ 0 (
    echo.
    echo [SUCCESS] Production build generated: AspireSense.exe
) else (
    echo.
    echo [FAILED] Compilation failed.
)
pause
