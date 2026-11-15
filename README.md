# Ball-Collector Digital Twin

Robot gom bong tu hanh + digital twin thoi gian thuc tren Unity.
Camera tren cao + ArUco dinh vi xe, YOLO (ONNX, GPU) phat hien bong,
Python "Brain" lap lo trinh va dieu khien, ESP32 lai 2 banh + long xoay gom bong.

## Cau truc
- `Assets/`: Unity (scene, script C#, model robot)
- `Brain/`: Python (camera, ArUco, YOLO, tracker, lap lo trinh, dieu khien). Thong so: `Brain/config.py`
- `Firmware/code_esp32_web/`: firmware ESP32 (web cau hinh WiFi "BallBot-XXXX" + long xoay)
- `urdf/`: URDF + mesh goc tu SolidWorks

## Cai dat (Windows)
1. Cai Unity 6000.6.7f1.
2. Bam dup `Brain/setup_venv.bat` (tu cai Python 3.9, thu vien; co NVIDIA thi cai ban GPU).
3. Mo thu muc bang Unity Hub, mo `Assets/Scenes/SampleScene`, bam Play.

## Firmware ESP32
WiFi trong file .ino chi la mac dinh. Sau khi nap: dien thoai noi WiFi `BallBot-XXXX`
(mat khau `ballbot123`), trang cau hinh tu mo (hoac http://192.168.4.1), chon WiFi.

## Luu y
- `Brain/calib/camera_calibration.npz` la calib cua camera goc (1280x720).
- ArUco `DICT_5X5_50`, canh 192 mm: 1 ma tren san (goc), 1 ma tren noc xe.