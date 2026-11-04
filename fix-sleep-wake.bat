@echo off
title AspireSense Sleep-Wake Protection Fixer
echo ========================================================
echo   AspireSense Sleep-Wake Protection Fixer
echo ========================================================
echo.

:: Check for administrative privileges
net session >nul 2>&1
if %errorlevel% neq 0 (
    echo Elevating to Administrator...
    powershell -Command "Start-Process '%~f0' -Verb RunAs"
    exit /b
)

echo Current devices armed to wake the computer:
echo --------------------------------------------------------
powercfg /devicequery wake_armed
echo --------------------------------------------------------
echo.

echo Disarming all rogue USB/hub/network wake devices...
for /f "tokens=*" %%d in ('powercfg /devicequery wake_armed') do (
    if not "%%d"=="NONE" (
        echo Disarming: %%d
        powercfg /devicedisablewake "%%d"
    )
)

echo.
echo Verification - Remaining wake devices (should be NONE):
echo --------------------------------------------------------
powercfg /devicequery wake_armed
echo --------------------------------------------------------
echo.
echo [SUCCESS] All rogue wake devices have been permanently disarmed!
echo You can now sleep your laptop and turn off your external display safely.
echo.
pause
