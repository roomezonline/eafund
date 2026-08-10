@echo off
echo Starting MiniBank PWA...

REM Change to project directory
cd /d "D:\DotNet\Web\Emam_Ali_Sandogh_New\src\MiniBank.Web"

REM Kill any existing processes first
call "..\..\kill-minibank.bat"

REM Wait a moment for ports to be released
timeout /t 3 >nul

REM Start the application
echo Starting application...
dotnet run

pause
