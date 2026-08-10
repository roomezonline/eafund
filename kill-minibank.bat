@echo off
echo Stopping MiniBank processes...

REM Kill by process name
taskkill /F /IM MiniBank.Web.exe 2>nul

REM Kill processes using port 58154
for /f "tokens=5" %%a in ('netstat -ano ^| findstr :58154') do (
    if not "%%a"=="0" (
        echo Killing process %%a using port 58154
        taskkill /F /PID %%a 2>nul
    )
)

REM Kill processes using port 58155
for /f "tokens=5" %%a in ('netstat -ano ^| findstr :58155') do (
    if not "%%a"=="0" (
        echo Killing process %%a using port 58155
        taskkill /F /PID %%a 2>nul
    )
)

echo All MiniBank processes stopped.
timeout /t 2 >nul
