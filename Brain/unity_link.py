"""Ket noi UDP voi Unity: gui trang thai + anh camera, nhan lenh."""
import json
import socket
import time

import cv2

import config as C


class UnityLink:
    def __init__(self):
        self.out = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        self.cmd = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        self.cmd.bind(("0.0.0.0", C.CMD_PORT))
        self.cmd.setblocking(False)
        self.last_cmd = None
        self.last_cmd_time = 0.0

    def poll(self):
        """
        Doc het goi tu Unity. Tra ve (lenh_moi_nhat, danh_sach_action).
          lenh  : {"mode": "auto|manual|stop", "v", "w"}  hoac  {"mode": "manual", "pwm_l", "pwm_r"}
          action: {"action": "reset_odom" | "quit" | "clear_balls"} | {"action": "add_ball", "x": mm, "y": mm}  (goi 1 lan)
        """
        actions = []
        while True:
            try:
                data, _ = self.cmd.recvfrom(4096)
            except (BlockingIOError, OSError):
                break
            try:
                msg = json.loads(data.decode())
            except ValueError:
                continue
            if "action" in msg:
                actions.append(msg)
            else:
                self.last_cmd = msg
                self.last_cmd_time = time.time()
        return self.last_cmd, actions

    def send_state(self, msg):
        self.out.sendto(json.dumps(msg).encode(), (C.UNITY_IP, C.STATE_PORT))

    def send_frame(self, frame, text_lines=()):
        h, w = frame.shape[:2]
        if w != C.VIDEO_WIDTH:
            frame = cv2.resize(frame, (C.VIDEO_WIDTH, int(h * C.VIDEO_WIDTH / w)), interpolation=cv2.INTER_AREA)
        if text_lines:
            frame = frame.copy()
            # Ve chu tren anh DA thu nho: net khong bi mat khi resize; chu trang vien den chiu duoc nen JPEG
            line = "   |   ".join(text_lines)          # tat ca tren 1 hang
            y0 = 44                                  # chua cho nhan "CAMERA FEED" cua Unity
            cv2.putText(frame, line, (8, y0), cv2.FONT_HERSHEY_SIMPLEX, 0.40, (0, 0, 0), 4, cv2.LINE_AA)
            cv2.putText(frame, line, (8, y0), cv2.FONT_HERSHEY_SIMPLEX, 0.40, (255, 255, 255), 1, cv2.LINE_AA)
        q = C.VIDEO_JPEG_QUALITY
        while q >= 20:
            ok, jpg = cv2.imencode(".jpg", frame, [cv2.IMWRITE_JPEG_QUALITY, q])
            if ok and len(jpg) < 60000:          # 1 goi UDP toi da ~64 KB
                self.out.sendto(jpg.tobytes(), (C.UNITY_IP, C.VIDEO_PORT))
                return
            q -= 15
