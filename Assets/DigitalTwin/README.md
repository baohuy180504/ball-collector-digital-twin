# Digital Twin - robot gom bóng

## Cài đặt trong Unity
1. Menu **Digital Twin → 1. Build Robot From URDF**: đọc `Assets/URDF/robot_gom_bong.urdf`, dựng model và lưu prefab vào `Assets/DigitalTwin/Prefabs`.
2. Menu **Digital Twin → 2. Setup Scene**: tạo GameObject `DigitalTwin` (nhận UDP, sân, bóng, path, HUD), đặt robot và camera nhìn từ trên xuống.
3. Chỉnh kích thước sân thật ở component `FieldView` (Length X, Width Y), chuột phải vào component → **Rebuild field**. Chỉnh `Ball Diameter` ở `BallManager`.
4. Bấm Play, rồi chạy `python Tools/fake_twin_sender.py` để thử.

Sửa URDF xong thì chạy lại bước 1.

## Hệ tọa độ sân (Python phải dùng đúng hệ này)
- Gốc tọa độ: 1 góc sân (góc có mã ArUco tham chiếu).
- Trục X dọc chiều dài sân, trục Y dọc chiều rộng. Nhìn từ trên xuống, X quay sang Y là ngược chiều kim đồng hồ (hệ tay phải, Z hướng lên).
- yaw = 0 khi đầu xe (phía 2 bánh chủ động) hướng theo +X; yaw tăng khi xe quay trái.
- Ảnh camera có trục pixel v hướng xuống, nên khi đổi pixel sang mét thường phải đảo trục Y.

## Gói UDP (JSON, cổng 5105, mỗi frame 1 gói)
```json
{
  "t": 12.345,
  "robot": {"valid": true, "x": 512.0, "y": 330.0, "yaw": 90.0,
            "roll": 1.2, "pitch": -0.5, "wl": 3.1, "wr": 3.4, "tilt_alarm": false},
  "balls": [{"id": 3, "x": 800.0, "y": 210.0}],
  "path":  [{"x": 512.0, "y": 330.0}, {"x": 800.0, "y": 210.0}],
  "status": "dang gom bong"
}
```
| Trường | Đơn vị | Ghi chú |
|---|---|---|
| x, y | mm | vị trí tâm xoay (trung điểm trục 2 bánh chủ động) hoặc tâm bóng |
| yaw, roll, pitch | độ | roll/pitch lấy từ IMU |
| wl, wr | rad/s | tốc độ bánh từ encoder; gửi 0 nếu không có, Unity sẽ tự ước lượng |
| valid | bool | false khi camera mất mã ArUco: Unity giữ nguyên pose cũ |
| balls | | chỉ gửi các bóng đang có; bóng không còn trong gói sẽ bị xóa khỏi Unity |
| path | | điểm đầu là vị trí robot, tiếp theo là các điểm theo thứ tự đi |

## Kiểm tra chiều quay
- Đẩy xe thật theo +X: model trên Unity phải đi sang phải màn hình (ở góc nhìn từ trên xuống).
- Nghiêng xe thật sang phải: nếu model nghiêng ngược chiều thì đổi `Roll Sign` của `RobotTwin` thành -1 (tương tự `Pitch Sign`).

## Bộ não Python (thư mục `Brain/`, ngang hàng `Assets`)
- Bấm Play: component `BrainLauncher` tự chạy `python Brain/main.py`, log hiện trong Console Unity. Dừng Play thì Python tự tắt.
- Chọn nguồn dữ liệu trong `Brain/config.py`: `SOURCE = "sim"` (giả lập) hoặc `"camera"`. Bật xe: `ESP32_ENABLED = True` và đặt `ESP32_IP`.
- Thư viện cài trong môi trường ảo riêng `Brain/.venv`, không đụng tới Python chung của máy: bấm đúp `Brain/setup_venv.bat` một lần (chạy lại khi thêm thư viện vào `requirements.txt`). `BrainLauncher` tự dùng `.venv` khi bấm Play.
- Chỉnh màu bóng: bấm đúp `Brain/tools/hsv_tuner.bat`.
- Thư mục: `perception/` (camera, ArUco, bóng, tracking), `tools/` (công cụ phụ), `models/` (model detect bóng .onnx), `calib/` (camera_calib.npz, field_homography.npy).
- Chạy tay trong VS Code: chọn interpreter `Brain/.venv/Scripts/python.exe` (Ctrl+Shift+P → Python: Select Interpreter).

| Cổng UDP | Chiều | Nội dung |
|---|---|---|
| 5005 | ESP32 → Python | telemetry JSON `{"l","r","roll","pitch","gz","v"}` (firmware cố định) |
| 5006 | Python → ESP32 | `cmd:<pwm_l>,<pwm_r>`, PWM −255..255, gửi 20 lần/s |
| 5105 | Python → Unity | JSON trạng thái (bảng ở trên + `mode`, `source`, `esp_connected`, `esp_ip`, `tele_rate`, `batt`, `enc_l`, `enc_r`, `gz`, `dist`, `pwm_l`, `pwm_r`, `fps`, `target_id`, `collected`, `cmd_v`, `cmd_w`) |
| 5106 | Python → Unity | mỗi gói là 1 ảnh JPEG camera đã vẽ overlay |
| 5107 | Unity → Python | `{"mode":"auto|manual|stop","v","w"}` hoặc `{"mode":"manual","pwm_l","pwm_r"}` 20 lần/s; một lần: `{"action":"reset_odom"}`, `{"action":"quit"}`. Python không nhận lệnh quá 1 s thì tự STOP |

Chế độ: `SOURCE` trong `Brain/config.py`, mặc định `camera` (camera thật; chưa có camera hoặc mất mã ArUco thì dùng odometry encoder + gyro). `sim` (giả lập) chỉ còn dùng để thử khi đặt trong config.

Giao diện: MANUAL / AUTO (bật cái này tắt cái kia, bấm lại để tắt), PAUSE (chỉ khi AUTO), 3D VIEW (kéo chuột trái xoay, chuột phải di chuyển, cuộn phóng to). Phím: W A S D lái, M = MANUAL, O = AUTO, P = PAUSE, Space = dừng hết.
