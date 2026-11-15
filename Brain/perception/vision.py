"""
Camera, ArUco (1 ma goc tren san + 1 ma tren xe), phat hien bong.
Viet lai tu camera_tracker.py da test voi phan cung:
  - solvePnP (IPPE_SQUARE) cho tung ma voi file calib (camera_matrix, dist_coeffs)
  - ma goc va ma xe phan biet bang "neo" vi tri ma goc (anchor), loc thong thap chong rung
  - pose xe = pose ma xe trong he toa do ma goc (x, y, yaw) -> doi sang he toa do san
Bong: tia tu pixel giao voi mat phang san (trong he ma goc) -> khong can homography.
"""
import math
import os
import threading
import time

os.environ.setdefault("OPENCV_VIDEOIO_MSMF_ENABLE_HW_TRANSFORMS", "0")   # MSMF mo camera nhanh hon, it treo
import cv2
import numpy as np

import config as C


CAMERA_OPEN_LOCK = threading.Lock()   # khong mo 2 camera cung luc (OpenCV/DirectShow co the crash)


class Camera:
    """
    Doc camera bang 1 luong rieng, luon giu khung hinh MOI NHAT.
    - 1 vai khung hinh loi (USB rot goi) KHONG lam dong camera nua (truoc day loi 1 lan la mo lai -> mat anh 3 s).
    - Chi bao 'dead' khi khong doc duoc anh lien tuc CAMERA_STALL_S giay -> main.py moi mo lai.
    """

    def __init__(self, index=None):
        self.index = C.CAMERA_INDEX if index is None else int(index)
        self.cap = self._open()
        self.lock = threading.Condition()
        self.frame, self.seq, self.last_ok = None, 0, time.time()
        self.fps = 0.0                   # toc do camera THAT SU cho anh (do trong luong doc)
        self.dead = False
        self._stop = False
        self.th = threading.Thread(target=self._loop, daemon=True)
        self.th.start()

    @staticmethod
    def _backends():
        b = getattr(C, "CAMERA_BACKEND", "auto")
        if os.name != "nt" or b == "default":
            return [("default", cv2.CAP_ANY)]
        if b == "dshow":
            return [("dshow", cv2.CAP_DSHOW)]
        if b == "msmf":
            return [("msmf", cv2.CAP_MSMF)]
        return [("dshow", cv2.CAP_DSHOW), ("msmf", cv2.CAP_MSMF)]      # auto (Windows)

    def _open(self):
        for name, api in self._backends():
            with CAMERA_OPEN_LOCK:
                cap = cv2.VideoCapture(self.index, api)
            if not cap.isOpened():
                cap.release()
                continue
            fourcc = getattr(C, "CAMERA_FOURCC", "MJPG")
            if fourcc:
                cap.set(cv2.CAP_PROP_FOURCC, cv2.VideoWriter_fourcc(*fourcc))   # MJPG: 1280x720 @30 qua USB 2.0
            cap.set(cv2.CAP_PROP_FRAME_WIDTH, C.CAMERA_WIDTH)
            cap.set(cv2.CAP_PROP_FRAME_HEIGHT, C.CAMERA_HEIGHT)
            cap.set(cv2.CAP_PROP_FPS, getattr(C, "CAMERA_FPS", 30))
            if fourcc:
                cap.set(cv2.CAP_PROP_FOURCC, cv2.VideoWriter_fourcc(*fourcc))   # DirectShow: dat lai sau khi doi kich thuoc
            cap.set(cv2.CAP_PROP_BUFFERSIZE, 1)
            ok = False
            for _ in range(10):                      # vai khung dau thuong rong
                ok, f = cap.read()
                if ok:
                    break
                time.sleep(0.05)
            if not ok:
                cap.release()
                continue
            h, w = f.shape[:2]
            fcc = int(cap.get(cv2.CAP_PROP_FOURCC))
            fcc_s = "".join(chr((fcc >> 8 * k) & 0xFF) for k in range(4)) if fcc > 0 else "?"
            print(f"Camera index {self.index}: backend {name}, {w}x{h}, format {fcc_s}, fps khai bao {cap.get(cv2.CAP_PROP_FPS):.0f}")
            if (w, h) != (C.CAMERA_WIDTH, C.CAMERA_HEIGHT):
                print(f"CANH BAO: camera cho {w}x{h}, calib la {C.CAMERA_WIDTH}x{C.CAMERA_HEIGHT} -> toa do se sai")
            return cap
        raise RuntimeError(f"Khong mo duoc camera index {self.index} (dang bi app khac dung, hoac sai index)")

    def _loop(self):
        stall = getattr(C, "CAMERA_STALL_S", 2.0)
        while not self._stop:
            ok, f = self.cap.read()
            now = time.time()
            if ok and f is not None:
                dtc = now - self.last_ok
                if 1e-3 < dtc < 1.0:
                    self.fps += (1.0 / dtc - self.fps) * 0.1
                with self.lock:
                    self.frame, self.seq, self.last_ok = f, self.seq + 1, now
                    self.lock.notify_all()
            else:
                if now - self.last_ok > stall:
                    self.dead = True
                    with self.lock:
                        self.lock.notify_all()
                    return
                time.sleep(0.01)

    def read(self, timeout=0.1):
        """Khung hinh moi (chua doc lan nao) hoac None neu chua co trong 'timeout' giay."""
        with self.lock:
            seq0 = getattr(self, "_read_seq", 0)
            if self.seq == seq0 and not self.dead:
                self.lock.wait(timeout)
            if self.seq == seq0:
                return None
            self._read_seq = self.seq
            return self.frame

    def release(self):
        self._stop = True
        if self.th.is_alive() and threading.current_thread() is not self.th:
            self.th.join(timeout=1.0)
        self.cap.release()


class CameraScanner:
    """
    Do tim cac camera THAT SU cho anh (chay nen, khong lam dung vong lap chinh).
    Chi tinh camera mo duoc VA doc duoc anh khong phai toan mau den (loai bo camera ao / IR rong).
    """

    def __init__(self):
        self.result = []
        self.busy = False
        self.done_once = False
        self.open_index = -1             # main.py cap nhat: camera dang mo -> khong mo lai khi do

    def start(self, open_index=None):
        if open_index is not None:
            self.open_index = open_index
        if self.busy:
            return
        self.busy = True
        threading.Thread(target=self._run, daemon=True).start()

    @staticmethod
    def _probe(i):
        for _, api in Camera._backends():
            with CAMERA_OPEN_LOCK:
                cap = cv2.VideoCapture(i, api)
                try:
                    if not cap.isOpened():
                        continue
                    for _ in range(8):
                        ok, f = cap.read()
                        if ok and f is not None and f.size > 0 and float(f.std()) > 2.0:
                            return True
                        time.sleep(0.05)
                finally:
                    cap.release()
        return False

    def _run(self):
        found = []
        try:
            for i in range(getattr(C, "CAMERA_SCAN_MAX", 6)):
                if i == self.open_index:                  # camera dang mo va dang cho anh: khong mo lai lan 2
                    found.append(i)
                elif self._probe(i):
                    found.append(i)
        except Exception as e:
            print(f"Loi khi do camera: {e}")
        self.result = found
        self.done_once = True
        self.busy = False
        print(f"Tim thay camera index: {found}")


def load_calibration(path):
    """Tra ve (K, dist). Ho tro ten khoa camera_matrix/dist_coeffs (ban calib) va K/mtx/dist."""
    if os.path.exists(path):
        d = np.load(path)
        K = d["camera_matrix"] if "camera_matrix" in d.files else (d["K"] if "K" in d.files else d["mtx"])
        dist = d["dist_coeffs"] if "dist_coeffs" in d.files else d["dist"]
        print(f"Da nap calib camera: {path}")
        return K.astype(np.float64), dist.astype(np.float64).reshape(-1, 1)
    print(f"CANH BAO: khong thay {path} -> pose ArUco se sai")
    return np.eye(3), np.zeros((5, 1))


def _rot_z(deg):
    a = math.radians(deg)
    return np.array([[math.cos(a), -math.sin(a), 0.0], [math.sin(a), math.cos(a), 0.0], [0.0, 0.0, 1.0]])


class ArucoTracker:
    """
    Tim ma goc (san) va ma xe. Moi khung hinh tra ve dict:
      origin_ok, robot (x_mm, y_mm, yaw_rad) trong he SAN hoac None, markers (de ve), ...
    """

    def __init__(self):
        self.K, self.dist = load_calibration(C.CALIB_FILE)
        h = C.ARUCO_MARKER_SIZE_M / 2.0
        self.obj = np.array([[-h, h, 0.0], [h, h, 0.0], [h, -h, 0.0], [-h, -h, 0.0]], np.float32)
        d = cv2.aruco.getPredefinedDictionary(getattr(cv2.aruco, C.ARUCO_DICT))
        try:
            self.det = cv2.aruco.ArucoDetector(d, cv2.aruco.DetectorParameters())
            self.new_api = True
        except AttributeError:
            self.dict, self.params = d, cv2.aruco.DetectorParameters_create()
            self.new_api = False
        self.anchor = None              # tvec ma goc da neo
        self.R_o = None                 # pose ma goc (camera <- ma goc)
        self.t_o = None
        self.R_yaw = _rot_z(C.ARUCO_YAW_OFFSET_DEG)

    def reset_anchor(self):
        self.anchor = None
        self.R_o = self.t_o = None

    @property
    def origin_ok(self):
        return self.R_o is not None

    def _detect(self, gray):
        if self.new_api:
            corners, ids, _ = self.det.detectMarkers(gray)
        else:
            corners, ids, _ = cv2.aruco.detectMarkers(gray, self.dict, parameters=self.params)
        out = []
        if ids is None:
            return out
        for c, i in zip(corners, ids.flatten()):
            ok, rvec, tvec = cv2.solvePnP(self.obj, c[0], self.K, self.dist, flags=cv2.SOLVEPNP_IPPE_SQUARE)
            if ok:
                out.append({"id": int(i), "corners": c[0], "rvec": rvec, "tvec": tvec.flatten()})
        return out

    def _split(self, ms):
        """
        Chon ma goc va ma xe.
          1. Neu config dat ID rieng -> theo ID.
          2. Thay >= 2 ma -> theo DO CAO: ma xe cao hon ma san ~ROBOT_MARKER_HEIGHT_MM theo phap tuyen
             mat ma. Dung ca khi camera bi di chuyen.
          3. Chi thay 1 ma -> theo neo vi tri ma goc lan gan nhat (camera dung yen).
        """
        if C.ORIGIN_MARKER_ID is not None and C.ROBOT_MARKER_ID is not None:
            o = next((m for m in ms if m["id"] == C.ORIGIN_MARKER_ID), None)
            r = next((m for m in ms if m["id"] == C.ROBOT_MARKER_ID), None)
            return o, r
        if not ms:
            return None, None
        if len(ms) >= 2:
            H = C.ROBOT_MARKER_HEIGHT_MM / 1000.0
            best = None
            for a in ms:
                Ra, _ = cv2.Rodrigues(a["rvec"])
                n = Ra[:, 2]                       # truc z ma: vuong goc mat ma, huong len (ve phia camera)
                for b in ms:
                    if b is a:
                        continue
                    err = abs(float(n @ (b["tvec"] - a["tvec"])) - H)
                    if best is None or err < best[0]:
                        best = (err, a, b)
            if best[0] < C.HEIGHT_MATCH_TOL_M:
                self.anchor = best[1]["tvec"].copy()          # cap nhat neo theo vi tri camera moi
                return best[1], best[2]
        if self.anchor is None:
            if len(ms) == 1:
                self.anchor = ms[0]["tvec"].copy()          # chi thay 1 ma: do la ma goc
            else:
                # thay nhieu ma luc chua neo: ma o xa camera nhat la ma tren san (ma xe cao hon ~30 cm)
                self.anchor = max(ms, key=lambda m: m["tvec"][2])["tvec"].copy()
        ms = sorted(ms, key=lambda m: np.linalg.norm(m["tvec"] - self.anchor))
        o = ms[0]
        if np.linalg.norm(o["tvec"] - self.anchor) > C.ANCHOR_MAX_JUMP_M:
            o = None                                        # ma goc bi che, ma gan nhat la xe
            r = ms[0]
        else:
            r = ms[1] if len(ms) > 1 else None
        return o, r

    def process(self, frame):
        gray = cv2.cvtColor(frame, cv2.COLOR_BGR2GRAY)
        ms = self._detect(gray)
        o, r = self._split(ms)
        if o is not None:
            if self.anchor is not None and (C.ORIGIN_MARKER_ID is None):
                self.anchor = 0.95 * self.anchor + 0.05 * o["tvec"]   # chong rung camera
            self.R_o, _ = cv2.Rodrigues(o["rvec"])
            self.t_o = o["tvec"].reshape(3, 1)

        robot = None
        if r is not None and self.origin_ok:
            R_r, _ = cv2.Rodrigues(r["rvec"])
            R_r = R_r @ self.R_yaw
            t_rel = (self.R_o.T @ (r["tvec"].reshape(3, 1) - self.t_o)).flatten()
            R_rel = self.R_o.T @ R_r
            if C.ARUCO_FORWARD_OPPOSITE_Z:
                R_rel = R_rel @ np.array([[0, 0, 1], [0, 1, 0], [-1, 0, 0]], np.float64)
            yaw = math.atan2(R_rel[1, 0], R_rel[0, 0])
            # tam ma xe -> tam xoay (MARKER_OFFSET_MM trong he xe)
            ox, oy = C.MARKER_OFFSET_MM
            mx, my = t_rel[0] * 1000.0, t_rel[1] * 1000.0
            mx -= ox * math.cos(yaw) - oy * math.sin(yaw)
            my -= ox * math.sin(yaw) + oy * math.cos(yaw)
            fx, fy, fyaw = marker_to_field(mx, my, yaw)
            robot = (fx, fy, fyaw)
            r["rvec_draw"], _ = cv2.Rodrigues(R_r)
        return {"origin": o, "robot_marker": r, "robot": robot, "markers": ms}

    # ---------- chuyen doi pixel <-> san ----------
    def pixel_to_field(self, px, z_m=0.0):
        """Pixel (u, v) -> diem tren mat phang cao z_m (he ma goc) -> mm he san."""
        if not self.origin_ok:
            return None
        n = cv2.undistortPoints(np.array([[px]], np.float64), self.K, self.dist).reshape(2)
        d = self.R_o.T @ np.array([[n[0]], [n[1]], [1.0]])          # huong tia trong he ma goc
        c = -self.R_o.T @ self.t_o                                  # tam camera trong he ma goc
        if abs(d[2, 0]) < 1e-9:
            return None
        s = (z_m - c[2, 0]) / d[2, 0]
        if s <= 0:
            return None
        p = c + s * d
        return marker_to_field(p[0, 0] * 1000.0, p[1, 0] * 1000.0, 0.0)[:2]

    def field_to_pixel(self, pts_mm, z_m=0.0):
        """Mang diem mm he san -> pixel (de ve overlay)."""
        pts = np.asarray(pts_mm, np.float64).reshape(-1, 2)
        if not self.origin_ok or len(pts) == 0:
            return np.zeros((len(pts), 2))
        mk = np.array([field_to_marker(x, y) for x, y in pts]) / 1000.0
        obj = np.column_stack([mk, np.full(len(mk), z_m)])
        rvec, _ = cv2.Rodrigues(self.R_o)
        img, _ = cv2.projectPoints(obj, rvec, self.t_o, self.K, self.dist)
        return img.reshape(-1, 2)

    def draw(self, frame, res):
        if self.origin_ok:
            rvec, _ = cv2.Rodrigues(self.R_o)
            cv2.drawFrameAxes(frame, self.K, self.dist, rvec, self.t_o, 0.15)
            p = self.field_to_pixel([marker_to_field(0, 0, 0)[:2]])[0].astype(int)
            cv2.putText(frame, "ORIGIN", (p[0], p[1] - 20), cv2.FONT_HERSHEY_SIMPLEX, 0.6, (0, 255, 0), 2)
        r = res["robot_marker"]
        if r is not None and "rvec_draw" in r:
            cv2.drawFrameAxes(frame, self.K, self.dist, r["rvec_draw"], r["tvec"], 0.15)
        for m in res["markers"]:
            cv2.polylines(frame, [np.int32(m["corners"])], True, (0, 255, 255), 2)
        if not self.origin_ok:
            pass  # bo dong chu "WAITING FOR ORIGIN MARKER" (trang thai da hien tren Unity)


# ---------- he ma goc (m) <-> he san (mm) ----------
def marker_to_field(x_mm, y_mm, yaw):
    a = math.radians(C.ORIGIN_FIELD_YAW_DEG)
    ox, oy = C.ORIGIN_FIELD_MM
    return (ox + x_mm * math.cos(a) - y_mm * math.sin(a),
            oy + x_mm * math.sin(a) + y_mm * math.cos(a),
            math.atan2(math.sin(yaw + a), math.cos(yaw + a)))


def field_to_marker(x_mm, y_mm):
    a = math.radians(C.ORIGIN_FIELD_YAW_DEG)
    ox, oy = C.ORIGIN_FIELD_MM
    dx, dy = x_mm - ox, y_mm - oy
    return (dx * math.cos(a) + dy * math.sin(a), -dx * math.sin(a) + dy * math.cos(a))


def make_occlusion_test(tracker, robot, frame_shape):
    """
    Tra ve ham f(x_mm, y_mm) -> True neu camera KHONG THE thay bong tai vi tri do luc nay:
      - chua co anh / chua thay ma goc (khong biet camera nhin dau),
      - bong nam ngoai khung hinh camera,
      - bong nam trong vung anh bi THAN XE che (chieu hop 3D than xe len anh).
    Tracker dung ham nay: bong bi khuat thi GIU LAI, chi xoa khi cho do camera thay ro ma khong co bong.
    """
    if tracker is None or frame_shape is None or not tracker.origin_ok:
        return lambda x, y: True
    h, w = frame_shape[:2]
    m = float(getattr(C, "OCCLUSION_PIXEL_MARGIN", 12))
    hull = None
    if robot is not None:
        fp = robot_footprint_mm(*robot, scale=1.1)                       # noi rong 10% cho chac
        H = C.ROBOT_MARKER_HEIGHT_MM / 1000.0                             # chieu cao than xe ~ noc xe
        pts = np.vstack([tracker.field_to_pixel(fp, 0.0), tracker.field_to_pixel(fp, H)])
        hull = cv2.convexHull(pts.astype(np.float32))
    r_m = C.BALL_DIAMETER_MM / 2000.0

    def occluded(x, y):
        u, v = tracker.field_to_pixel([(x, y)], r_m)[0]
        if not (m <= u < w - m and m <= v < h - m):
            return True                                                    # ngoai khung hinh
        if hull is not None and cv2.pointPolygonTest(hull, (float(u), float(v)), True) >= -m:
            return True                                                    # sau / duoi than xe
        return False

    return occluded


def robot_footprint_mm(x, y, yaw, scale=1.0):
    f, r, hw = C.BODY_FRONT_M * 1000, C.BODY_REAR_M * 1000, C.BODY_HALF_WIDTH_M * 1000
    local = np.array([[f, hw], [f, -hw], [-r, -hw], [-r, hw]]) * scale
    c, s = math.cos(yaw), math.sin(yaw)
    R = np.array([[c, -s], [s, c]])
    return local @ R.T + np.array([x, y])


_onnx_detector = None


def _get_onnx_detector():
    """Tao model 1 lan (lan dau goi), dung lai cho cac frame sau."""
    global _onnx_detector
    if _onnx_detector is None:
        from perception.ball_detector_onnx import OnnxBallDetector
        _onnx_detector = OnnxBallDetector(C.BALL_MODEL_PATH, C.BALL_CONF, C.BALL_IOU, C.BALL_USE_GPU)
    return _onnx_detector


def _exclusion_polys(tracker, robot, markers):
    """Vung khong tinh bong: than xe (tu mat san len noc) + cac ma ArUco. Tra ve list polygon pixel."""
    polys = []
    if robot is not None and tracker.origin_ok:
        ground = robot_footprint_mm(*robot, scale=1.1)
        top_z = C.ROBOT_MARKER_HEIGHT_MM / 1000.0
        poly = np.vstack([tracker.field_to_pixel(ground, 0.0), tracker.field_to_pixel(ground, top_z)])
        polys.append(cv2.convexHull(poly.astype(np.int32)))
    for m in markers:
        polys.append(np.int32(m["corners"]).reshape(-1, 1, 2))
    return polys


def detect_balls(frame_bgr, tracker, robot=None, markers=()):
    """Tra ve list (x_mm, y_mm, cx_px, cy_px, r_px). Bo vung than xe va cac ma ArUco."""
    if C.BALL_DETECTOR == "onnx":
        return _detect_balls_onnx(frame_bgr, tracker, robot, markers)
    return _detect_balls_hsv(frame_bgr, tracker, robot, markers)


def _detect_balls_onnx(frame_bgr, tracker, robot, markers):
    dets = _get_onnx_detector().detect(frame_bgr)
    from perception.tracker import in_robot_body
    polys = [np.int32(m["corners"]).reshape(-1, 1, 2) for m in markers]     # cac ma ArUco
    if robot is not None and tracker.origin_ok:                            # mat NOC xe tren anh
        top = tracker.field_to_pixel(robot_footprint_mm(*robot), C.ROBOT_MARKER_HEIGHT_MM / 1000.0)
        polys.append(top.astype(np.int32).reshape(-1, 1, 2))
    r_m = C.BALL_DIAMETER_MM / 2000.0
    out = []
    for cx, cy, r, conf in dets:
        if any(cv2.pointPolygonTest(pl, (cx, cy), False) >= 0 for pl in polys):
            continue                                   # nam tren noc xe / ma ArUco
        mm = tracker.pixel_to_field((cx, cy), r_m)     # tam bong o do cao = ban kinh
        if mm is not None and in_robot_body(robot, mm[0], mm[1]):
            continue                                   # nam trong THAN xe (sau mep long) - pheu van giu
        if mm is None or not (0 <= mm[0] <= C.FIELD_X_MM and 0 <= mm[1] <= C.FIELD_Y_MM):
            continue                                   # ngoai san
        if C.BALL_SIZE_GATE:
            # ban kinh pixel du kien cua bong that tai vi tri nay
            p = tracker.field_to_pixel([mm, (mm[0] + C.BALL_DIAMETER_MM / 2.0, mm[1])], r_m)
            r_exp = float(np.linalg.norm(p[1] - p[0]))
            lo, hi = C.BALL_SIZE_GATE
            if r_exp > 1e-3 and not (lo * r_exp <= r <= hi * r_exp):
                continue                               # to / nho bat thuong -> khong phai bong
        out.append((float(mm[0]), float(mm[1]), cx, cy, r))
    return out


def _detect_balls_hsv(frame_bgr, tracker, robot=None, markers=()):
    """Cach cu: loc mau HSV (giu lai de so sanh)."""
    hsv = cv2.cvtColor(frame_bgr, cv2.COLOR_BGR2HSV)
    mask = cv2.inRange(hsv, np.array(C.BALL_HSV_LOW), np.array(C.BALL_HSV_HIGH))
    mask = cv2.morphologyEx(mask, cv2.MORPH_OPEN, np.ones((3, 3), np.uint8))
    mask = cv2.morphologyEx(mask, cv2.MORPH_CLOSE, np.ones((5, 5), np.uint8))
    for pl in _exclusion_polys(tracker, robot, markers):
        cv2.fillPoly(mask, [pl], 0)

    contours, _ = cv2.findContours(mask, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_SIMPLE)
    out = []
    r_m = C.BALL_DIAMETER_MM / 2000.0
    for cnt in contours:
        area = cv2.contourArea(cnt)
        if not C.BALL_MIN_AREA_PX <= area <= C.BALL_MAX_AREA_PX:
            continue
        per = cv2.arcLength(cnt, True)
        if per <= 0 or 4 * math.pi * area / (per * per) < C.BALL_MIN_CIRCULARITY:
            continue
        (cx, cy), r = cv2.minEnclosingCircle(cnt)
        mm = tracker.pixel_to_field((cx, cy), r_m)      # tam bong o do cao = ban kinh
        if mm is not None and 0 <= mm[0] <= C.FIELD_X_MM and 0 <= mm[1] <= C.FIELD_Y_MM:
            out.append((float(mm[0]), float(mm[1]), cx, cy, r))
    return out
