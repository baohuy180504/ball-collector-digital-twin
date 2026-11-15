@echo off
rem ==========================================================================
rem  Cai moi truong Python cho Brain - chay 1 lan tren may moi (bam dup file nay).
rem    1. Tim Python 3.9 (chua co -> tu cai Python 3.9 bang winget)
rem    2. Tao Brain\.venv rieng (khong dung Python chung cua may)
rem    3. Co card NVIDIA -> cai ban GPU (CUDA qua pip, khong can CUDA Toolkit)
rem       Khong co        -> cai ban CPU
rem    4. Kiem tra thu vien + chay thu model detect bong
rem  Ep chon: setup_venv.bat gpu   hoac   setup_venv.bat cpu
rem  Chay lai bao nhieu lan cung duoc (.venv hong / sai phien ban -> tu tao lai).
rem ==========================================================================
setlocal EnableExtensions
cd /d "%~dp0"
title Brain - cai dat moi truong Python
echo ================== CAI DAT MOI TRUONG CHO BRAIN ==================
echo.

rem ---------------------------------------------------------------- 1. Python 3.9
set "PY39="
for /f "delims=" %%P in ('py -3.9 -c "import sys;print(sys.executable)" 2^>nul') do set "PY39=%%P"
if not defined PY39 if exist "%LOCALAPPDATA%\Programs\Python\Python39\python.exe" set "PY39=%LOCALAPPDATA%\Programs\Python\Python39\python.exe"
if not defined PY39 if exist "C:\Python39\python.exe" set "PY39=C:\Python39\python.exe"
if not defined PY39 (
    echo [1/4] May chua co Python 3.9 - dang tu cai bang winget, cho vai phut ...
    winget install -e --id Python.Python.3.9 --scope user --accept-package-agreements --accept-source-agreements
    if exist "%LOCALAPPDATA%\Programs\Python\Python39\python.exe" set "PY39=%LOCALAPPDATA%\Programs\Python\Python39\python.exe"
)
if not defined PY39 (
    echo.
    echo KHONG cai duoc Python 3.9 tu dong.
    echo Tai va cai thu cong Python 3.9.13 64-bit: https://www.python.org/downloads/release/python-3913/
    echo Cai xong chay lai file nay.
    pause
    exit /b 1
)
"%PY39%" -c "import sys,struct;sys.exit(0 if sys.version_info[:2]==(3,9) and struct.calcsize('P')==8 else 1)" >nul 2>&1
if errorlevel 1 (
    echo Python tim thay khong phai 3.9 64-bit: %PY39%
    pause
    exit /b 1
)
echo [1/4] Python 3.9: %PY39%

rem ---------------------------------------------------------------- 2. .venv
set "VPY=.venv\Scripts\python.exe"
if exist ".venv" (
    "%VPY%" -c "import sys;sys.exit(0 if sys.version_info[:2]==(3,9) else 1)" >nul 2>&1
    if errorlevel 1 (
        echo .venv cu khong dung duoc - vd chep tu may khac - xoa de tao lai ...
        rmdir /s /q .venv
    )
)
if exist "%VPY%" goto venv_ok
if exist ".venv" (
    echo Khong xoa duoc .venv cu. Dung Play trong Unity, dong VS Code roi chay lai.
    pause
    exit /b 1
)
echo [2/4] Dang tao .venv ...
"%PY39%" -m venv --without-pip .venv
rem Python 3.9 tren Windows loi khi duong dan Python co dau cach (vd C:\Users\Ten Nguoi\...):
rem ghi duong dan ngan khong dau cach (vd C:\Users\TENNGU~1\...) vao .venv\pyvenv.cfg
"%PY39%" -c "import ctypes,os,sys;b=os.path.dirname(sys.executable);buf=ctypes.create_unicode_buffer(1024);ctypes.windll.kernel32.GetShortPathNameW(b,buf,1024);s=buf.value or b;p=os.path.join('.venv','pyvenv.cfg');L=open(p).read().splitlines();open(p,'w').write(chr(10).join(('home = '+s) if l.startswith('home') else l for l in L)+chr(10));sys.exit(1 if ' ' in s else 0)"
if errorlevel 1 (
    echo Duong dan Python co dau cach va Windows khong tao duoc ten ngan.
    echo Cai lai Python 3.9 vao C:\Python39 roi chay lai file nay.
    pause
    exit /b 1
)
"%VPY%" -c "import sys" >nul 2>&1
if errorlevel 1 (
    echo Tao .venv that bai.
    pause
    exit /b 1
)
"%VPY%" -m ensurepip --upgrade --default-pip >nul
:venv_ok
echo [2/4] .venv san sang.

rem ---------------------------------------------------------------- 3. GPU hay CPU
set "MODE=cpu"
where nvidia-smi >nul 2>&1 && set "MODE=gpu"
if /i "%~1"=="gpu" set "MODE=gpu"
if /i "%~1"=="cpu" set "MODE=cpu"
if /i "%MODE%"=="gpu" (
    echo [3/4] Tim thay card NVIDIA:
    nvidia-smi --query-gpu=name,driver_version --format=csv,noheader 2>nul
    echo       Cai ban GPU - tai khoang 2 GB, lan dau hoi lau ...
    "%VPY%" -m pip uninstall -y onnxruntime >nul 2>&1
) else (
    echo [3/4] Khong thay card NVIDIA - cai ban CPU, model detect bong se cham hon.
    "%VPY%" -m pip uninstall -y onnxruntime-gpu >nul 2>&1
)
"%VPY%" -m pip install --upgrade pip --disable-pip-version-check -q
"%VPY%" -m pip install --disable-pip-version-check -r "requirements-%MODE%.txt"
if errorlevel 1 (
    echo.
    echo Cai thu vien LOI - kiem tra mang Internet roi chay lai file nay.
    pause
    exit /b 1
)

rem ---------------------------------------------------------------- 4. Kiem tra
echo.
echo [4/4] Kiem tra:
"%VPY%" -c "import sys,cv2,numpy,onnxruntime as o;print('  Python',sys.version.split()[0],'| OpenCV',cv2.__version__,'| numpy',numpy.__version__,'| onnxruntime',o.__version__)"
if errorlevel 1 (
    echo Import thu vien loi.
    pause
    exit /b 1
)
"%VPY%" -c "import sys,os;sys.path.insert(0,'.');import config as C;from perception.ball_detector_onnx import OnnxBallDetector as D;D(C.BALL_MODEL_PATH,use_gpu=C.BALL_USE_GPU) if os.path.exists(C.BALL_MODEL_PATH) else print('  Chua co model:',C.BALL_MODEL_PATH)"
echo.
echo XONG. Mo Unity, bam Play: Unity tu dung Brain\.venv de chay Brain.
echo Neu dong tren ghi "chay tren: CPUExecutionProvider" o may co NVIDIA: cap nhat driver NVIDIA roi chay lai.
pause
