"""
Cau noi voi ESP32 theo dung giao thuc firmware hien tai (giong web.py):
  ESP32 -> PC : UDP toi cong ESP32_TELEMETRY_PORT, JSON {"l","r","roll","pitch","gz","v","step"}
  PC -> ESP32 : UDP toi ESP32_IP:ESP32_CMD_PORT, chu "cmd:<pwm_l>,<pwm_r>"
                va "step:RUN" / "step:STOP" (motor buoc co cau gom bong)
"""
import json
import math
import socket
import threading
import time

import config as C
from robot.odometry import Odometry


def clamp(x, lo, hi):
    return lo if x < lo else hi if x > hi else x


class Esp32Bridge(threading.Thread):
    def __init__(self):
        super().__init__(daemon=True)
        self.rx = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        self.rx.bind(("0.0.0.0", C.ESP32_TELEMETRY_PORT))
        self.rx.settimeout(0.5)
        self.tx = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        self.ip = None if str(C.ESP32_IP).lower() == "auto" else C.ESP32_IP
        self.odom = Odometry()
        self.lock = threading.Lock()
        self.tel = {}
        self.last_time = 0.0
        self.rate = 0.0
        self.last_pwm = (0, 0)
        self.stepper_want = False        # Brain muon co cau gom quay hay khong
        self._step_sent = None           # lenh step gan nhat da gui
        self._step_sent_t = 0.0
        self.running = True
        self._reset_req = False

    # ---------- nhan telemetry ----------
    def run(self):
        print(f"Cho telemetry ESP32 tren UDP {C.ESP32_TELEMETRY_PORT} ...")
        count, t0 = 0, time.time()
        while self.running:
            try:
                data, addr = self.rx.recvfrom(2048)
            except socket.timeout:
                continue
            except OSError:
                if not self.running:
                    break
                continue
            try:
                p = json.loads(data.decode("utf-8"))
            except ValueError:
                continue
            now = time.time()
            if str(C.ESP32_IP).lower() == "auto" and addr[0] != self.ip:
                print(f"Nhan telemetry tu ESP32 {addr[0]}")
                self.ip = addr[0]

            with self.lock:
                if self._reset_req:
                    self.odom.reset()
                    self._reset_req = False
                self.odom.update(int(p.get("l", 0)), int(p.get("r", 0)), float(p.get("gz", 0.0)), now)
                roll, pitch = float(p.get("roll", 0.0)), float(p.get("pitch", 0.0))
                self.tel = {
                    "enc_l": int(p.get("l", 0)), "enc_r": int(p.get("r", 0)),
                    "wl": self.odom.wl, "wr": self.odom.wr,
                    "roll": roll, "pitch": pitch, "gz": float(p.get("gz", 0.0)),
                    "batt": float(p.get("v", 0.0)),
                    "tilt": abs(roll) > C.TILT_LIMIT_DEG or abs(pitch) > C.TILT_LIMIT_DEG,
                    "dist": self.odom.dist,
                    "step": int(p.get("step", 0)),   # trang thai that cua motor buoc (firmware moi)
                }
                self.last_time = now

            count += 1
            if now - t0 >= 1.0:
                self.rate = count / (now - t0)
                count, t0 = 0, now

    @property
    def connected(self):
        return time.time() - self.last_time < C.ESP32_TIMEOUT_S

    def snapshot(self):
        with self.lock:
            return dict(self.tel) if self.connected else {}

    def pose_mm(self):
        with self.lock:
            return self.odom.pose_mm()

    def set_pose_mm(self, x_mm, y_mm, yaw):
        with self.lock:
            self.odom.set_pose_mm(x_mm, y_mm, yaw)

    def reset_odom(self):
        with self.lock:
            self._reset_req = True

    # ---------- gui lenh ----------
    def send_pwm(self, pwm_l, pwm_r):
        pwm_l = int(clamp(round(pwm_l), -C.PWM_MAX, C.PWM_MAX))
        pwm_r = int(clamp(round(pwm_r), -C.PWM_MAX, C.PWM_MAX))
        self.last_pwm = (pwm_l, pwm_r)
        if self.ip is None:
            return
        try:
            self.tx.sendto(f"cmd:{pwm_l},{pwm_r}".encode("utf-8"), (self.ip, C.ESP32_CMD_PORT))
        except OSError as e:
            print(f"Loi gui lenh ESP32: {e}")

    def _send_text(self, text):
        if self.ip is None:
            return False
        try:
            self.tx.sendto(text.encode("utf-8"), (self.ip, C.ESP32_CMD_PORT))
            return True
        except OSError as e:
            print(f"Loi gui lenh ESP32: {e}")
            return False

    def set_stepper(self, run):
        """Bat/tat co cau gom bong. Gui ngay khi doi trang thai, va gui lai dinh ky (UDP co the mat goi)."""
        run = bool(run)
        now = time.time()
        if run != self._step_sent or now - self._step_sent_t >= C.STEPPER_RESEND_S:
            if self._send_text("step:RUN" if run else "step:STOP"):
                if run != self._step_sent:
                    print(f"Co cau gom bong: {'RUN' if run else 'STOP'}")
                self._step_sent, self._step_sent_t = run, now
        self.stepper_want = run

    def close(self):
        for _ in range(3):                       # dam bao xe dung va co cau gom dung
            self.send_pwm(0, 0)
            self._send_text("step:STOP")
            time.sleep(0.02)
        self.running = False
        try:
            self.rx.close()
        except OSError:
            pass


class WheelSpeedController:
    """(v m/s, w rad/s) -> PWM 2 banh: feedforward + PI theo toc do banh do bang encoder."""

    def __init__(self):
        self.i = [0.0, 0.0]

    def reset(self):
        self.i = [0.0, 0.0]

    def update(self, v, w, wl_meas, wr_meas, dt):
        R, L = C.WHEEL_RADIUS_M, C.WHEEL_TRACK_M
        target = ((v - w * L / 2) / R, (v + w * L / 2) / R)
        meas = (wl_meas, wr_meas)
        out = []
        for k in range(2):
            t = target[k]
            if abs(t) < 0.05:
                self.i[k] = 0.0
                out.append(0)
                continue
            ff = math.copysign(C.PWM_MIN_MOVE + C.PWM_PER_RAD_S * abs(t), t)
            e = t - meas[k]
            self.i[k] = clamp(self.i[k] + e * dt, -C.SPEED_I_MAX, C.SPEED_I_MAX)
            out.append(int(clamp(ff + C.SPEED_KP * e + C.SPEED_KI * self.i[k], -C.PWM_MAX, C.PWM_MAX)))
        return out[0], out[1]


def pwm_to_vw(pwm_l, pwm_r):
    """Uoc luong nguoc (v, w) tu PWM - dung cho che do gia lap."""
    def rad_s(p):
        return math.copysign(max(0.0, abs(p) - C.PWM_MIN_MOVE) / C.PWM_PER_RAD_S, p)
    wl, wr = rad_s(pwm_l), rad_s(pwm_r)
    R, L = C.WHEEL_RADIUS_M, C.WHEEL_TRACK_M
    return R * (wl + wr) / 2, R * (wr - wl) / L
