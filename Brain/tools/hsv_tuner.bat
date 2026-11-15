@echo off
rem Chay cong cu chinh mau bong bang Python trong .venv
cd /d "%~dp0.."
if not exist ".venv\Scripts\python.exe" (
    echo Chua co .venv. Chay setup_venv.bat truoc.
    pause
    exit /b 1
)
".venv\Scripts\python.exe" tools\hsv_tuner.py
pause
