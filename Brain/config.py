"""
Cau hinh bo nao robot gom bong. Chi can sua file nay.
Don vi: mm cho vi tri tren san, m va m/s cho dong hoc robot.
"""
import os

BASE_DIR = os.path.dirname(os.path.abspath(__file__))
CALIB_DIR = os.path.join(BASE_DIR, "calib")     # file calib camera + homography san
MODEL_DIR = os.path.join(BASE_DIR, "models")    # model detect bong (.onnx)

# ================= NGUON DU LIEU =================
# "camera" : che do chinh. Vi tri tu ArUco; khi chua co camera / mat ma thi dung odometry (encoder + gyro)
# "sim"    : gia lap hoan toan (chua can camera, chua can xe) -> de thu Unity va dieu khien
# "odom"   : chi odometry, khong mo camera
SOURCE = "camera"
# check cam:  .\.venv\Scripts\python.exe -c "import cv2; [print(i, 'CO CAMERA' if cv2.VideoCapture(i).read()[0] else '-') for i in range(4)]"
CAMERA_INDEX = 1                 # theo camera_tracker.py da test (0 = webcam laptop, 1 = camera USB)
CAMERA_WIDTH = 1280              # phai trung do phan giai luc calib (1280 x 720)
CAMERA_HEIGHT = 720
CAMERA_BACKEND = "auto"          # "auto" (Windows: thu dshow roi msmf) | "dshow" | "msmf" | "default"
CAMERA_FOURCC = "MJPG"           # nen MJPG de USB 2.0 chay 1280x720 @30 on dinh; "" = de mac dinh
CAMERA_SCAN_MAX = 6              # nut CAMERA tren Unity: do tim camera index 0..(N-1)
CAMERA_STALL_S = 2.0             # khong co anh lien tuc bao lau thi coi la mat camera va mo lai
CAMERA_RETRY_S = 3.0             # chua mo duoc camera -> thu lai sau moi khoang nay
CALIB_FILE = os.path.join(CALIB_DIR, "camera_calibration.npz")   # camera_matrix + dist_coeffs (ban calib)

# ================= SAN & ArUco (1 ma goc tren san + 1 ma tren xe) =================
FIELD_X_MM = 3000                # chieu dai san theo truc X (phai khop FieldView trong Unity)
FIELD_Y_MM = 2000                # chieu rong san theo truc Y

ARUCO_DICT = "DICT_5X5_50"
ARUCO_MARKER_SIZE_M = 0.192      # canh ma ArUco (m), ca ma goc va ma xe
# ID ma goc / ma xe. De None: phan biet bang "neo" vi tri ma goc nhu camera_tracker.py
# (luc bat dau, ma duy nhat thay duoc hoac ma xa camera nhat = ma goc tren san).
ORIGIN_MARKER_ID = None
ROBOT_MARKER_ID = None
ANCHOR_MAX_JUMP_M = 0.15         # (chi thay 1 ma) ma lech neo qua khoang nay -> do la ma xe
HEIGHT_MATCH_TOL_M = 0.10        # (thay 2 ma) chenh cao do duoc lech ROBOT_MARKER_HEIGHT_MM toi da bao nhieu
ARUCO_YAW_OFFSET_DEG = -90.0     # xoay he truc ma xe de truc X chi ve dau xe (theo ban test)
ARUCO_FORWARD_OPPOSITE_Z = False

# Vi tri ma goc tren san: tam ma goc o toa do nao cua san (mm) va truc X ma lech truc X san bao nhieu do.
# Mac dinh ma goc o GIUA san -> xe o phia nao quanh ma goc cung hien tren Unity.
ORIGIN_FIELD_MM = (FIELD_X_MM / 2, FIELD_Y_MM / 2)
ORIGIN_FIELD_YAW_DEG = 0.0

ROBOT_MARKER_HEIGHT_MM = 301     # do cao mat ma tren xe so voi ma san: dung de phan biet 2 ma va che than xe khi tim bong
# Tam ma ArUco tren noc xe so voi TAM XOAY (trung diem truc 2 banh chu dong), trong he xe:
# x duong = phia dau xe, y duong = ben trai. Ma dan giua noc than xe -> x = -(BODY_REAR - BODY_FRONT)/2.
# Camera do duoc tam ma; bo nao tru di doan nay de ra tam xoay (diem dong hoc dung de dieu khien).
MARKER_OFFSET_MM = (-170.0, 0.0)  # da do: tam ma cach truc 2 banh 17 cm ve phia sau

# ================= BONG =================
BALL_DETECTION = True            # BAT nhan dien bong that bang camera (False = chi dung bong ao tu Unity)
BALL_DETECTOR = "onnx"           # "onnx" = model YOLO (.onnx) | "hsv" = loc mau cu
BALL_MODEL_PATH = os.path.join(MODEL_DIR, "model2.onnx")
BALL_CONF = 0.40                 # nguong tin cay (tang neu bao nham, giam neu sot bong)
BALL_IOU = 0.50                  # nguong NMS (gop box trung)
BALL_USE_GPU = True              # thu GPU NVIDIA truoc, khong duoc thi tu chay CPU
BALL_SIZE_GATE = (0.5, 2.0)      # ban kinh box / ban kinh du kien tai vi tri do phai nam trong khoang nay (None = tat)
BALL_DIAMETER_MM = 74             # duong kinh bong that (Unity lay so nay de ve bong)
# Nguong mau HSV cua bong (OpenCV: H 0-179). Chinh bang tools/hsv_tuner.py
BALL_HSV_LOW = (5, 120, 120)
BALL_HSV_HIGH = (25, 255, 255)
BALL_MIN_AREA_PX = 60
BALL_MAX_AREA_PX = 8000
BALL_MIN_CIRCULARITY = 0.6

# ================= ROBOT =================
WHEEL_RADIUS_M = 0.0325
WHEEL_TRACK_M = 0.45             # khoang cach tam 2 banh (web.py cu de 0.165 - kiem tra lai!)
BODY_FRONT_M = 0.05              # than xe nho ra truoc truc banh
BODY_REAR_M = 0.335              # than xe keo ve sau truc banh
BODY_HALF_WIDTH_M = 0.2375       # nua be ngang, tinh ca banh
INTAKE_AHEAD_M = 0.10            # diem gom bong phia truoc truc banh

# ---- Co cau gom: long xoay + pheu (he xe, mm; goc = tam truc banh, x ve dau xe) ----
CAGE_X_MM = 0.0                  # truc long xoay so voi truc banh (long nam ngay tren truc dong co)
CAGE_RADIUS_MM = 40.0            # ban kinh long: 20 cm toi truc long - 16 cm toi mep long
CAGE_HALF_WIDTH_MM = 150.0       # nua be ngang LONG XOAY (theo y) -> DO LAI
HOLD_FRONT_X_MM = 200.0          # mieng pheu (mep truoc vung giu bong) cach truc banh 20 cm
HOLD_FRONT_HALF_W_MM = 190.0     # mieng pheu rong 38 cm
HOLD_BACK_HALF_W_MM = 185.0      # cho sat long = be rong than xe 37 cm -> DO LAI neu khac
CONTACT_TOL_MM = 30.0            # tam bong cach vi tri "ap long" toi da bay nhieu thi coi la dang ap long
PICK_CONFIRM_S = 0.8             # bong da vao pheu roi bien mat lien tuc bay lau -> tinh DA GOM
FINISH_PUSH_S = 1.5              # het bong de toi ma pheu con bong / vua gom: tien them bay lau cho bong vao han
PUSH_TIMEOUT_S = 8.0             # bong nam trong pheu (van thay) qua lau ma khong vao -> tha ra, xe lam lai
PUSH_SPEED = 0.3                # m/s: toc do tien khi day bong cuoi vao long
PUSH_W_MAX = 0.6                 # rad/s: lai nhe giu bong o giua long khi dang day
OCCLUSION_MARGIN_MM = 150.0        # bong mat gan than xe (ngoai cua thu) -> coi la bi che, giu lai
BALL_HOLD_FRAMES = 1800            # giu bong BI KHUAT toi da bay nhieu khung (~60 s o 30 FPS)
OCCLUSION_PIXEL_MARGIN = 12        # px: noi rong vung than xe che + bo mep anh khi xet bong bi khuat

V_MAX = 0.40                     # m/s
W_MAX = 1.2                      # rad/s  (toc do quay toi da khi AUTO)
K_W = 1.8                        # he so lai: w = K_W * goc lech (lon -> quay gat)
SLOW_DIST_M = 0.25               # diem gom con cach bong bao nhieu thi bat dau giam toc
APPROACH_MIN_RATIO = 0.8         # toc do toi thieu khi sap toi bong = ty le cua V_MAX (0.6 -> 0.18 m/s)
TURN_IN_PLACE_DEG = 50           # lech qua goc nay thi dung lai quay tai cho
MAX_ACCEL = 0.6                  # m/s^2   gioi han tang/giam toc thang
MAX_ANG_ACCEL = 4.0              # rad/s^2 gioi han tang/giam toc quay (nho -> quay em hon)
PLAN_SWITCH_MM = 150             # chi doi bong muc tieu khi thu tu moi ngan hon it nhat bay nhieu
PICKUP_RADIUS_M = 0.08           # (che do sim) bong gia lap bien mat quanh diem gom           # bong bien mat trong ban kinh nay quanh diem gom -> tinh la da nhat
VIRTUAL_BALL_ID_START = 10001    # ID bong ao (dat tu Unity) de khong trung bong that
FIELD_MARGIN_M = 0.15            # khong cho diem gom ra sat mep san hon khoang nay
ROBOT_LOST_TIMEOUT_S = 0.4       # mat ca camera lan odometry lau hon -> dung xe (che do AUTO)

# ================= ESP32 (theo firmware hien tai, giong web.py) =================
# ESP32 -> PC : UDP JSON {"l": xung trai, "r": xung phai, "roll", "pitch", "gz": do/s, "v": volt}
# PC -> ESP32 : UDP "cmd:<pwm_l>,<pwm_r>"  (PWM -255..255)
ESP32_ENABLED = True             # che do "camera": co gui lenh xuong xe khong ("odom" luon bat)
ESP32_IP = "auto"                # "auto" = lay IP tu goi telemetry dau tien; hoac dien vd "172.18.40.200"
ESP32_CMD_PORT = 5006            # cong ESP32 nghe lenh
ESP32_TELEMETRY_PORT = 5005      # cong PC nghe telemetry (firmware gui toi cong nay)
ESP32_SEND_HZ = 20               # gui lai lenh lien tuc (firmware giu lenh cuoi cung)
ESP32_TIMEOUT_S = 0.5            # khong co telemetry lau hon -> coi nhu mat ket noi
STEPPER_AUTO = True              # co cau gom bong (motor buoc): tu quay khi AUTO, dung khi STOP/PAUSE/MANUAL
STEPPER_RESEND_S = 1.0           # gui lai lenh step:RUN/STOP moi khoang nay (phong mat goi UDP)

TICKS_PER_REV = 330.0            # xung encoder / 1 vong banh
# Bo loc Kalman huong (theo config da test)
GYRO_DEADBAND_DEG = 1.5          # |gz| nho hon -> coi nhu 0 (chong troi)
Q_HEADING = 0.003
Q_GYRO_BIAS = 0.0001
R_ENCODER = 0.025
R_ARUCO = 0.004                  # yaw tu ArUco tin cay hon encoder

ENC_L_SIGN = 1                   # doi -1 neu xe tien ma so xung banh trai giam
ENC_R_SIGN = 1
GYRO_Z_SIGN = 1                  # doi -1 neu xe quay trai ma yaw giam
TILT_LIMIT_DEG = 12.0            # |roll| hoac |pitch| lon hon -> bao nghieng, dung AUTO
BATT_MIN_V, BATT_MAX_V = 10.0, 12.6   # pin 3S

# Doi (v, w) -> PWM cho tung banh: PWM = PWM_MIN_MOVE + PWM_PER_RAD_S * toc_do + PI(sai so)
# Do bang che do test: lai PWM co dinh, doc toc do banh (rad/s) tren Unity.
PWM_MAX = 255
PWM_MIN_MOVE = 70                # PWM nho nhat de banh bat dau quay
PWM_PER_RAD_S = 12.0             # PWM tang them cho moi rad/s
SPEED_KP = 6.0
SPEED_KI = 15.0
SPEED_I_MAX = 5.0

# Vi tri bat dau cua odometry tren san (che do "odom")
ODOM_START_MM = (FIELD_X_MM / 2, FIELD_Y_MM / 2)
ODOM_START_YAW_DEG = 0.0

# ================= UNITY =================
# (5005/5006 da danh cho ESP32 nen Unity dung 51xx)
UNITY_IP = "127.0.0.1"
STATE_PORT = 5105                # JSON trang thai -> Unity
VIDEO_PORT = 5106                # anh camera JPEG -> Unity
CMD_PORT = 5107                  # lenh tu Unity -> bo nao
VIDEO_WIDTH = 640
VIDEO_JPEG_QUALITY = 70
VIDEO_HZ = 15
MANUAL_TIMEOUT_S = 1.0           # che do tay: khong nhan lenh tu Unity lau hon -> dung
