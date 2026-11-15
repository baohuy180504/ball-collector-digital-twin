@echo off
rem Chay cong cu thu model detect bong ONNX bang Python trong .venv
cd /d "%~dp0.."
if not exist ".venv\Scripts\python.exe" (
    echo Chua co .venv. Chay setup_venv.bat truoc.
    pause
    exit /b 1
)
".venv\Scripts\python.exe" tools\test_ball_onnx.py
pause
