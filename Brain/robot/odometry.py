"""
Odometry tu encoder + gyro Z, loc huong bang Kalman 2 trang thai (yaw, bias gyro).
  du doan : gyro Z (co vung chet GYRO_DEADBAND_DEG)
  cap nhat: yaw tich luy tu encoder (R_ENCODER) va yaw tu ArUco khi camera thay xe (R_ARUCO)
Thong so Q/R lay tu config da test voi phan cung.
"""
import math

import numpy as np

import config as C


def wrap(a):
    return (a + math.pi) % (2 * math.pi) - math.pi


class HeadingKalmanFilter:
    """x = [psi, b_gyro]: du doan bang gyro Z, cap nhat bang yaw tich luy tu encoder."""

    def __init__(self):
        self.x = np.array([0.0, 0.0])
        self.P = np.diag([0.05, 0.01])
        self.Q = np.diag([C.Q_HEADING, C.Q_GYRO_BIAS])
        self.R = C.R_ENCODER
        self.enc_yaw = 0.0

    def predict(self, gz_rad_s, dt):
        F = np.array([[1.0, -dt], [0.0, 1.0]])
        self.x[0] = wrap(self.x[0] + (gz_rad_s - self.x[1]) * dt)
        self.P = F @ self.P @ F.T + self.Q

    def _update(self, z, R):
        H = np.array([[1.0, 0.0]])
        y = wrap(z - self.x[0])
        S = float((H @ self.P @ H.T)[0, 0] + R)
        K = (self.P @ H.T) / S
        self.x = self.x + K.flatten() * y
        self.x[0] = wrap(self.x[0])
        self.P = (np.eye(2) - K @ H) @ self.P

    def update_encoder(self, d_yaw_enc):
        self.enc_yaw = wrap(self.enc_yaw + d_yaw_enc)
        self._update(self.enc_yaw, self.R)

    def update_aruco(self, yaw):
        self._update(yaw, C.R_ARUCO)
        self.enc_yaw = self.x[0]        # neo lai yaw encoder de 2 nguon khong keo nguoc nhau

    def reset_yaw(self):
        self.x[0] = 0.0
        self.enc_yaw = 0.0


class Odometry:
    def __init__(self):
        self.kf = HeadingKalmanFilter()
        self.reset()

    def reset(self):
        self.x = self.y = self.yaw = 0.0          # m, rad (he odometry, goc = luc reset)
        self.dist = 0.0
        self.wl = self.wr = 0.0                   # rad/s banh
        self.prev_l = self.prev_r = None
        self.prev_t = None
        self._acc_l = self._acc_r = 0
        self._acc_t = 0.0
        self.kf.reset_yaw()

    def update(self, enc_l, enc_r, gz_deg, t):
        enc_l *= C.ENC_L_SIGN
        enc_r *= C.ENC_R_SIGN
        if self.prev_t is None:
            self.prev_l, self.prev_r, self.prev_t = enc_l, enc_r, t
            return
        dt = t - self.prev_t
        self.prev_t = t
        if dt <= 0.0 or dt > 0.2:
            dt = 0.033
        dl, dr = enc_l - self.prev_l, enc_r - self.prev_r
        self.prev_l, self.prev_r = enc_l, enc_r

        m_per_tick = 2.0 * math.pi * C.WHEEL_RADIUS_M / C.TICKS_PER_REV
        sl, sr = dl * m_per_tick, dr * m_per_tick
        d = (sl + sr) / 2.0

        gz = gz_deg * C.GYRO_Z_SIGN
        if abs(gz) < C.GYRO_DEADBAND_DEG:
            gz = 0.0
        self.kf.predict(math.radians(gz), dt)
        self.kf.update_encoder((sr - sl) / C.WHEEL_TRACK_M)
        self.yaw = self.kf.x[0]

        self.x += d * math.cos(self.yaw)
        self.y += d * math.sin(self.yaw)
        self.dist += abs(d)

        # toc do banh: cong don xung toi khi du >= 20 ms de tranh gai khi 2 goi den sat nhau
        self._acc_l += dl
        self._acc_r += dr
        self._acc_t += dt
        if self._acc_t >= 0.02:
            rad_per_tick = 2.0 * math.pi / C.TICKS_PER_REV
            self.wl += (self._acc_l * rad_per_tick / self._acc_t - self.wl) * 0.5
            self.wr += (self._acc_r * rad_per_tick / self._acc_t - self.wr) * 0.5
            self._acc_l = self._acc_r = 0
            self._acc_t = 0.0

    def set_pose_mm(self, x_mm, y_mm, yaw, fuse_yaw=True):
        """
        Chinh odometry theo camera (ArUco): vi tri dat thang, yaw qua Kalman (R_ARUCO).
        Mat camera thi odometry chay tiep tu vi tri nay.
        """
        x0, y0 = C.ODOM_START_MM
        a0 = math.radians(C.ODOM_START_YAW_DEG)
        c, s = math.cos(-a0), math.sin(-a0)
        dx, dy = (x_mm - x0) / 1000.0, (y_mm - y0) / 1000.0
        self.x, self.y = dx * c - dy * s, dx * s + dy * c
        if fuse_yaw:
            self.kf.update_aruco(wrap(yaw - a0))
        else:
            self.kf.x[0] = wrap(yaw - a0)
            self.kf.enc_yaw = self.kf.x[0]
        self.yaw = self.kf.x[0]

    def pose_mm(self):
        """Pose tren san (mm, rad): odometry dat tai ODOM_START_MM."""
        x0, y0 = C.ODOM_START_MM
        a0 = math.radians(C.ODOM_START_YAW_DEG)
        c, s = math.cos(a0), math.sin(a0)
        return (x0 + 1000 * (self.x * c - self.y * s),
                y0 + 1000 * (self.x * s + self.y * c),
                wrap(self.yaw + a0))
