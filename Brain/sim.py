"""Gia lap san + xe + bong khi chua co camera / xe that. Ve anh 'camera' gia de Unity hien thi."""
import math
import random

import cv2
import numpy as np

import config as C
from control import intake_point, wrap
from perception.vision import robot_footprint_mm


class SimWorld:
    SCALE = 0.3        # px / mm
    PAD = 20

    def __init__(self, n_balls=6):
        self.n_balls = n_balls
        self.x, self.y, self.yaw = 500.0, C.FIELD_Y_MM / 2, 0.0
        self.v = self.w = 0.0
        self.balls = []
        self.t = 0.0
        self.respawn_at = 0.0
        self._spawn()

    def _spawn(self):
        m = 250
        self.balls = []
        for i in range(self.n_balls):
            ang = random.uniform(-math.pi, math.pi)
            spd = 120.0 if i == 0 else 0.0     # 1 qua lan (mm/s)
            self.balls.append([random.uniform(m, C.FIELD_X_MM - m), random.uniform(m, C.FIELD_Y_MM - m),
                               spd * math.cos(ang), spd * math.sin(ang)])

    def step(self, dt, v_cmd, w_cmd):
        self.t += dt
        k = min(1.0, dt / 0.15)                # dong co co tre ~150 ms
        self.v += (v_cmd - self.v) * k
        self.w += (w_cmd - self.w) * k
        self.x += self.v * 1000 * math.cos(self.yaw) * dt
        self.y += self.v * 1000 * math.sin(self.yaw) * dt
        self.yaw = wrap(self.yaw + self.w * dt)
        self.x = min(max(self.x, 100), C.FIELD_X_MM - 100)
        self.y = min(max(self.y, 100), C.FIELD_Y_MM - 100)

        for b in self.balls:
            b[0] += b[2] * dt
            b[1] += b[3] * dt
            if not 40 < b[0] < C.FIELD_X_MM - 40:
                b[2] = -b[2]
            if not 40 < b[1] < C.FIELD_Y_MM - 40:
                b[3] = -b[3]

        ix, iy = intake_point(self.x, self.y, self.yaw)
        r = C.PICKUP_RADIUS_M * 1000 * 0.6
        self.balls = [b for b in self.balls if math.hypot(b[0] - ix, b[1] - iy) > r]
        if not self.balls:
            if self.respawn_at == 0.0:
                self.respawn_at = self.t + 2.0
            elif self.t > self.respawn_at:
                self._spawn()
                self.respawn_at = 0.0

    # ----- du lieu "do duoc" -----
    def robot_measurement(self):
        if random.random() < 0.02:             # thinh thoang mat ma ArUco
            return None
        return (self.x + random.gauss(0, 3), self.y + random.gauss(0, 3), self.yaw + random.gauss(0, 0.008))

    def ball_detections(self):
        return [(b[0] + random.gauss(0, 4), b[1] + random.gauss(0, 4)) for b in self.balls if random.random() > 0.05]

    def telemetry(self):
        roll = 0.8 * math.sin(2.3 * self.t) + random.gauss(0, 0.2)
        pitch = 0.6 * math.sin(1.7 * self.t) + random.gauss(0, 0.2)
        R, L = C.WHEEL_RADIUS_M, C.WHEEL_TRACK_M
        return {"wl": (self.v - self.w * L / 2) / R, "wr": (self.v + self.w * L / 2) / R,
                "roll": roll, "pitch": pitch, "batt": 11.8, "tilt": False}

    # ----- anh -----
    def to_px(self, pts_mm):
        p = np.asarray(pts_mm, np.float64).reshape(-1, 2)
        return np.column_stack([self.PAD + p[:, 0] * self.SCALE,
                                self.PAD + (C.FIELD_Y_MM - p[:, 1]) * self.SCALE])

    def render(self):
        w = int(C.FIELD_X_MM * self.SCALE) + 2 * self.PAD
        h = int(C.FIELD_Y_MM * self.SCALE) + 2 * self.PAD
        img = np.full((h, w, 3), (95, 100, 100), np.uint8)
        cv2.rectangle(img, (self.PAD, self.PAD), (w - self.PAD, h - self.PAD), (140, 145, 145), -1)
        c = self.to_px([C.ORIGIN_FIELD_MM])[0].astype(int)          # ma goc tren san
        cv2.rectangle(img, tuple(c - 12), tuple(c + 12), (255, 255, 255), -1)
        cv2.rectangle(img, tuple(c - 8), tuple(c + 8), (0, 0, 0), -1)
        rb = int(C.BALL_DIAMETER_MM / 2 * self.SCALE) + 1
        for b in self.balls:
            c = self.to_px([b[:2]])[0].astype(int)
            cv2.circle(img, tuple(c), rb, (20, 120, 245), -1, cv2.LINE_AA)
        body = self.to_px(robot_footprint_mm(self.x, self.y, self.yaw)).astype(np.int32)
        cv2.fillPoly(img, [body], (60, 60, 60), cv2.LINE_AA)
        mk = self.to_px(robot_footprint_mm(self.x, self.y, self.yaw, 0.35)).astype(np.int32)
        cv2.fillPoly(img, [mk], (235, 235, 235), cv2.LINE_AA)
        return img
