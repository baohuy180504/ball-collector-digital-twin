r"""
Bo nao robot gom bong - file chay chinh.
  camera -> ArUco + bong -> tracking -> lap path -> (v, w) -> PWM -> ESP32 (UDP "cmd:l,r")
  ESP32 -> telemetry JSON (encoder, IMU, pin) -> odometry Kalman
  -> trang thai JSON + anh camera -> Unity (UDP);  Unity -> lenh che do / lai tay (UDP)

Che do (config.SOURCE hoac --source):
  odom   : test xe khong camera, vi tri tu encoder + gyro, lai tay bang PWM
  sim    : gia lap hoan toan
  camera : camera that

Chay:  python main.py  |  python main.py --source sim  |  python main.py --source camera
Unity tu chay file nay khi bam Play (component BrainLauncher).
"""
import argparse
import collections
import math
import os
import time

import cv2
import numpy as np

import config as C
from control import Planner, intake_point, keep_in_field, push_in, pursue, rate_limit
from perception.tracker import hold_zone_polygon_mm, in_hold_zone
from perception.tracker import BallTracker
from robot.esp32_bridge import Esp32Bridge, WheelSpeedController, pwm_to_vw
from unity_link import UnityLink


def draw_overlay(img, to_px, robot, balls, order, mode, info, virtual_ids=()):
    from perception.vision import robot_footprint_mm
    green, yellow, orange, red, white = (80, 220, 80), (0, 220, 255), (0, 140, 255), (60, 60, 255), (255, 255, 255)
    if robot is not None and order:
        pts = [robot[:2]] + [balls[i] for i in order]
        cv2.polylines(img, [to_px(pts).astype(np.int32)], False, yellow, 2, cv2.LINE_AA)
    # Moi bong: hinh vuong xanh co gian theo kich thuoc bong (bong dang toi: vien dam hon)
    rank = {bid: k + 1 for k, bid in enumerate(order or [])}
    r_mm = C.BALL_DIAMETER_MM / 2.0
    for i, (x, y) in balls.items():
        try:
            p = to_px([(x, y), (x + r_mm, y)], r_mm / 1000.0)      # tam bong o do cao = ban kinh
        except TypeError:
            p = to_px([(x, y), (x + r_mm, y)])                     # che do sim: khong co tham so do cao
        c = p[0]
        h = max(8.0, 1.35 * float(np.linalg.norm(p[1] - p[0])))  # nua canh hinh vuong (px)
        is_target = rank.get(i) == 1
        col = (0, 255, 0) if is_target else green
        p1 = (int(c[0] - h), int(c[1] - h))
        p2 = (int(c[0] + h), int(c[1] + h))
        cv2.rectangle(img, p1, p2, col, 3 if is_target else 2, cv2.LINE_AA)
    if robot is not None:
        body = to_px(robot_footprint_mm(*robot)).astype(np.int32)
        cv2.polylines(img, [body], True, green, 2, cv2.LINE_AA)
        hz = to_px(hold_zone_polygon_mm(robot)).astype(np.int32)      # pheu giu bong truoc long xoay
        cv2.polylines(img, [hz], True, yellow, 1, cv2.LINE_AA)
        a = to_px([robot[:2], intake_point(*robot)]).astype(int)
        cv2.arrowedLine(img, tuple(a[0]), tuple(a[1]), green, 2, cv2.LINE_AA, tipLength=0.3)
    # Chu MODE/FPS ve SAU khi thu nho anh (trong unity_link.send_frame) de khong bi mo
    return img


CAM_INDEX_FILE = os.path.join(os.path.dirname(os.path.abspath(__file__)), "selected_camera.txt")


def load_cam_index():
    """Camera chon lan truoc tren Unity (neu co), khong thi lay CAMERA_INDEX trong config.py."""
    try:
        with open(CAM_INDEX_FILE) as f:
            return int(f.read().strip())
    except (OSError, ValueError):
        return C.CAMERA_INDEX


def cam_list(scanner, cam, cam_index):
    """Chi nhung camera do duoc that; camera dang mo (dang cho anh) luon co trong danh sach."""
    lst = list(scanner.result) if scanner else []
    if cam is not None and not cam.dead and cam_index not in lst:
        lst.append(cam_index)
    return sorted(lst)


def save_cam_index(i):
    try:
        with open(CAM_INDEX_FILE, "w") as f:
            f.write(str(i))
    except OSError:
        pass


def main():
    # Neu Python bi crash o tang C (OpenCV/camera) thi ghi vet loi vao Brain/brain_crash.log
    import faulthandler
    try:
        faulthandler.enable(open(os.path.join(os.path.dirname(os.path.abspath(__file__)), "brain_crash.log"), "w"))
    except OSError:
        faulthandler.enable()
    ap = argparse.ArgumentParser()
    ap.add_argument("--source", default=C.SOURCE, choices=["odom", "sim", "camera"])
    args = ap.parse_args()
    source = args.source

    unity = UnityLink()
    bridge = None
    if source == "odom" or (source == "camera" and C.ESP32_ENABLED):
        bridge = Esp32Bridge()
        bridge.start()
    speed_ctl = WheelSpeedController()

    world = cam = tracker_cam = None
    to_px = None
    if source == "sim":
        from sim import SimWorld
        world = SimWorld()
        to_px = world.to_px
    elif source == "camera":
        from perception.vision import ArucoTracker, Camera, CameraScanner, detect_balls, make_occlusion_test
        tracker_cam = ArucoTracker()
        to_px = tracker_cam.field_to_pixel
    cam_retry = 0.0
    cam_index = load_cam_index()
    cam_fail = 0
    scanner = None
    if source == "camera":
        scanner = CameraScanner()
        scanner.start()                    # do 1 lan luc khoi dong de biet may co nhung camera nao
    print(f"Che do: {source.upper()} | Unity: trang thai -> {C.STATE_PORT}, anh -> {C.VIDEO_PORT}, lenh <- {C.CMD_PORT}")

    tracker = BallTracker()
    planner = Planner()
    trail = collections.deque(maxlen=3000)
    virtual = {}                       # bong ao dat tu Unity: id -> (x_mm, y_mm)
    next_virtual = C.VIRTUAL_BALL_ID_START
    mode = "stop"
    robot = None
    robot_time = 0.0
    collected = 0
    v = w = 0.0
    pwm = (0, 0)
    last = time.time()
    last_send = last_video = 0.0
    fps = 0.0
    finish_until = 0.0                 # tien them sau khi gom de bong cuoi lot han vao khoang chua
    perf = {"t": time.time(), "n": 0, "aruco": 0.0, "ball": 0.0, "video": 0.0}

    try:
        while True:
            now = time.time()
            dt = max(1e-3, min(now - last, 0.1))
            last = now
            fps += (1.0 / dt - fps) * 0.1
            perf["n"] += 1
            if now - perf["t"] >= 5.0:         # moi 5 s in thoi gian tung buoc (ms / vong) de tim cho cham
                n = max(1, perf["n"])
                cam_fps = f"{cam.fps:.1f}" if cam is not None else "-"
                print(f"[perf] loop {fps:.1f} FPS | camera {cam_fps} FPS | aruco {1000 * perf['aruco'] / n:.1f} ms"
                      f" | bong {1000 * perf['ball'] / n:.1f} ms | video {1000 * perf['video'] / n:.1f} ms")
                perf.update(t=now, n=0, aruco=0.0, ball=0.0, video=0.0)

            # ---------- lenh tu Unity ----------
            cmd, actions = unity.poll()
            names = [a.get("action") for a in actions]
            if "quit" in names:
                print("Unity yeu cau thoat")
                break
            if "reset_odom" in names:
                if bridge:
                    bridge.reset_odom()
                trail.clear()
                print("Da reset odometry")
            for a in actions:
                if a.get("action") == "add_ball":
                    bx, by = float(a.get("x", 0.0)), float(a.get("y", 0.0))
                    if 0 <= bx <= C.FIELD_X_MM and 0 <= by <= C.FIELD_Y_MM:
                        virtual[next_virtual] = (bx, by)
                        print(f"Them bong ao {next_virtual} tai ({bx:.0f}, {by:.0f}) mm")
                        next_virtual += 1
                elif a.get("action") == "clear_balls":
                    virtual.clear()
                    print("Da xoa het bong ao")
                elif a.get("action") == "scan_cameras" and scanner:
                    scanner.start()
                elif a.get("action") == "set_camera" and source == "camera":
                    idx = int(a.get("index", cam_index))
                    if idx != cam_index or cam is None:
                        print(f"Doi sang camera index {idx}")
                        cam_index, cam_fail = idx, 0
                        if cam:
                            cam.release()
                        cam, cam_retry = None, 0.0
                        tracker_cam.reset_anchor()         # camera moi -> tim lai ma goc
            manual_vw, manual_pwm = (0.0, 0.0), None
            if cmd and now - unity.last_cmd_time < C.MANUAL_TIMEOUT_S:
                mode = cmd.get("mode", mode)
                if "pwm_l" in cmd:
                    manual_pwm = (int(cmd.get("pwm_l", 0)), int(cmd.get("pwm_r", 0)))
                else:
                    manual_vw = (float(cmd.get("v", 0.0)), float(cmd.get("w", 0.0)))
            else:
                mode = "stop"              # Unity khong con gui lenh -> dung xe

            # ---------- cam nhan ----------
            tel = bridge.snapshot() if bridge else {}
            esp_ok = bool(bridge and bridge.connected)
            frame, aruco_res, ball_dets, meas = None, None, [], None
            if source == "sim":
                world.step(dt, v, w)
                frame = world.render()
                meas = world.robot_measurement()
                ball_dets = world.ball_detections()
                tel = world.telemetry()
                esp_ok = True
            elif source == "camera":
                if cam is None and now >= cam_retry:
                    cam_retry = now + C.CAMERA_RETRY_S
                    try:
                        cam = Camera(cam_index)
                        cam_fail = 0
                        save_cam_index(cam_index)          # chi luu khi camera mo duoc that
                        print(f"Da mo camera index {cam_index}")
                    except Exception as e:
                        print(f"Chua mo duoc camera: {e}")
                        cam = None
                        cam_fail += 1
                        # camera da chon khong con -> quay ve camera dau tien tim thay
                        if cam_fail >= 2 and scanner.done_once and scanner.result and cam_index not in scanner.result:
                            print(f"Camera index {cam_index} khong ton tai -> dung index {scanner.result[0]}")
                            cam_index, cam_fail, cam_retry = scanner.result[0], 0, 0.0
                scanner.open_index = cam.index if (cam is not None and not cam.dead) else -1
                frame = cam.read() if cam else None
                if cam is not None and cam.dead:
                    print(f"Mat anh camera > {C.CAMERA_STALL_S} s - thu mo lai")
                    cam.release()
                    cam = None
                if frame is not None:
                    _t0 = time.perf_counter()
                    aruco_res = tracker_cam.process(frame)
                    perf["aruco"] += time.perf_counter() - _t0
                    meas = aruco_res["robot"]                    # (x, y, yaw) he san hoac None
                    if tracker_cam.origin_ok and C.BALL_DETECTION:
                        _t0 = time.perf_counter()
                        found = detect_balls(frame, tracker_cam, meas or robot, aruco_res["markers"])
                        perf["ball"] += time.perf_counter() - _t0
                        ball_dets = [(b[0], b[1]) for b in found]
                if bridge:
                    if meas is not None:
                        bridge.set_pose_mm(*meas)          # camera thay xe -> chinh odometry (yaw qua Kalman)
                        if esp_ok:
                            meas = bridge.pose_mm()        # pose da loc (ArUco + gyro + encoder)
                    elif esp_ok:
                        odom_pose = bridge.pose_mm()       # mat camera / mat ma -> dung odometry
                        robot = odom_pose
            else:  # odom
                meas = bridge.pose_mm()

            if meas is not None:
                robot, robot_time = meas, now
            robot_valid = robot is not None and now - robot_time < C.ROBOT_LOST_TIMEOUT_S
            if source == "odom" or (source == "camera" and meas is None and esp_ok):
                robot_valid = esp_ok

            occ = None                         # sim: dung luat cu (gan than xe = bi che)
            if source == "camera":
                # vung camera khong thay (than xe che / ngoai khung / mat anh) -> bong o do duoc giu lai
                occ = make_occlusion_test(tracker_cam, robot, frame.shape if frame is not None else None)
            elif source == "odom":
                occ = lambda x, y: True
            balls, removed = tracker.update(ball_dets, robot, v, occ, dt)
            captured = tracker.captured_ids()
            if robot is not None:
                for bid, bx, by, picked in removed:
                    if picked:
                        collected += 1
                        finish_until = now + C.FINISH_PUSH_S
                        print(f"Da gom bong {bid} (tong {collected})")
                if not trail or math.hypot(robot[0] - trail[-1][0], robot[1] - trail[-1][1]) > 20:
                    trail.append(robot[:2])
                # bong ao: khong co vat ly (khong bi pheu day theo xe) -> vao pheu la tinh da gom ngay.
                # (Neu giu lai cho "day vao" nhu bong that thi xe quay di la bong ao roi khoi pheu
                #  -> lai thanh muc tieu -> xe nhich qua lai mai.)
                for vid, p in list(virtual.items()):
                    if in_hold_zone(robot, p[0], p[1]):
                        del virtual[vid]
                        collected += 1
                        print(f"Da nhat bong ao {vid}")
            balls = dict(balls)
            balls.update(virtual)              # bong that + bong ao cung vao lap path

            # ---------- dieu khien ----------
            # bong DA HUNG (trong pheu) khong lap path nua: xe chuyen sang day vao long
            targets = {k: p for k, p in balls.items() if k not in captured}
            order = planner.plan(robot, targets) if robot is not None else []
            held = [balls[k] for k in captured if k in balls]
            tilt = bool(tel.get("tilt", False))
            raw = None
            if mode == "manual":
                if manual_pwm is not None:
                    raw = manual_pwm
                    v, w = pwm_to_vw(*raw)
                else:
                    v, w = manual_vw
            elif mode == "auto" and robot_valid and (order or held or now < finish_until) and not tilt:
                if order:
                    # con bong de toi: chay toi bong ke tiep (dang tien nen bong trong pheu cung duoc day vao)
                    tv, tw = pursue(robot, balls[order[0]])
                else:
                    # het bong de toi ma pheu con bong / vua gom xong: tien them cho bong vao han
                    tv, tw = push_in(robot, held)
                tv = keep_in_field(robot, tv)
                v = rate_limit(v, tv, C.MAX_ACCEL, dt)            # tang/giam toc tu tu
                w = rate_limit(w, tw, C.MAX_ANG_ACCEL, dt)
            else:
                v, w = 0.0, 0.0

            if bridge and now - last_send >= 1.0 / C.ESP32_SEND_HZ:
                send_dt = now - last_send if last_send else 1.0 / C.ESP32_SEND_HZ
                last_send = now
                if raw is not None:
                    pwm = raw
                    speed_ctl.reset()
                elif v == 0.0 and w == 0.0:
                    pwm = (0, 0)
                    speed_ctl.reset()
                else:
                    pwm = speed_ctl.update(v, w, tel.get("wl", 0.0), tel.get("wr", 0.0), min(send_dt, 0.2))
                bridge.send_pwm(*pwm)
                # co cau gom bong: quay khi dang AUTO (PAUSE / STOP / MANUAL -> dung)
                bridge.set_stepper(C.STEPPER_AUTO and mode == "auto")
            elif not bridge:
                pwm = raw if raw is not None else (0, 0)

            # ---------- gui Unity ----------
            status = ""
            if source == "odom":
                if not esp_ok:
                    status = f"Waiting for ESP32 telemetry (UDP {C.ESP32_TELEMETRY_PORT})"
            else:
                if mode == "auto" and not balls:
                    status = "No balls detected"
                if source == "camera" and frame is not None and meas is None:
                    status = "Robot ArUco marker not visible" + (" - using odometry" if esp_ok else "")
                if source == "camera" and frame is not None and not tracker_cam.origin_ok:
                    status = "Origin ArUco marker not found on the floor"
                if source == "camera" and cam is None:
                    status = f"Camera not connected (index {cam_index})" + (" - using odometry" if esp_ok else "")
            if mode == "pause":
                status = "AUTO paused"

            # huong truc X / Y cua san tren anh camera (do, toan hoc: 0 = sang phai, nguoc kim dong ho)
            # -> Unity xoay goc nhin 2D cho giong anh camera
            view_deg, view_ok = 0.0, False
            if tracker_cam is not None and tracker_cam.origin_ok:
                o = np.array(C.ORIGIN_FIELD_MM, np.float64)
                p0, px, py = tracker_cam.field_to_pixel([o, o + [300.0, 0.0], o + [0.0, 300.0]])
                ax = math.degrees(math.atan2(-(px[1] - p0[1]), px[0] - p0[0]))
                ay = math.degrees(math.atan2(-(py[1] - p0[1]), py[0] - p0[0]))
                view_deg = ax
                view_ok = math.sin(math.radians(ay - ax)) > 0      # Y o ben trai X tren anh: khong bi lat guong
            if tilt:
                status = "WARNING: robot tilted"

            if source == "odom":
                pts = list(trail)
                step = max(1, len(pts) // 400)
                path = pts[::step] + ([pts[-1]] if pts and (len(pts) - 1) % step else [])
            else:
                path = ([robot[:2]] + [balls[i] for i in order]) if robot is not None else []

            unity.send_state({
                "t": round(now, 3),
                "source": source,
                "robot": {
                    "valid": robot_valid,
                    "x": round(robot[0], 1) if robot else 0.0,
                    "y": round(robot[1], 1) if robot else 0.0,
                    "yaw": round(math.degrees(robot[2]), 2) if robot else 0.0,
                    "roll": round(tel.get("roll", 0.0), 2), "pitch": round(tel.get("pitch", 0.0), 2),
                    "wl": round(tel.get("wl", 0.0), 3), "wr": round(tel.get("wr", 0.0), 3),
                    "tilt_alarm": tilt,
                },
                "balls": [{"id": i, "x": round(p[0], 1), "y": round(p[1], 1), "virtual": i in virtual,
                           "rank": (order.index(i) + 1) if i in order else 0} for i, p in balls.items()],
                "ball_d": C.BALL_DIAMETER_MM,
                "path": [{"x": round(p[0], 1), "y": round(p[1], 1)} for p in path],
                "status": status,
                "mode": mode,
                "esp_connected": esp_ok,
                "stepper": int(tel.get("step", 0)),
                "esp_ip": (bridge.ip or "") if bridge else "",
                "tele_rate": round(bridge.rate, 1) if bridge else 0.0,
                "batt": round(tel.get("batt", 0.0), 2),
                "enc_l": int(tel.get("enc_l", 0)), "enc_r": int(tel.get("enc_r", 0)),
                "gz": round(tel.get("gz", 0.0), 2),
                "dist": round(tel.get("dist", 0.0), 3),
                "pwm_l": int(pwm[0]), "pwm_r": int(pwm[1]),
                "fps": round(fps, 1),
                "cameras": cam_list(scanner, cam, cam_index) if source == "camera" else [],
                "cam_index": cam_index if source == "camera" else -1,
                "cam_scanning": bool(scanner and scanner.busy),
                "target_id": order[0] if order else -1,
                "collected": collected,
                "cmd_v": round(v, 3), "cmd_w": round(w, 3),
                "view_deg": round(view_deg, 1), "view_ok": view_ok,
            })

            if frame is not None and now - last_video >= 1.0 / C.VIDEO_HZ:
                last_video = now
                info = f"FPS {fps:4.1f}  balls {len(balls)}  collected {collected}"   # ESP32 da co badge tren Unity
                if source == "camera" and aruco_res is not None:
                    tracker_cam.draw(frame, aruco_res)
                if source == "sim" or (tracker_cam is not None and tracker_cam.origin_ok):
                    draw_overlay(frame, to_px, robot, balls, order, mode, info, virtual)
                _t0 = time.perf_counter()
                unity.send_frame(frame, [f"MODE: {mode.upper()}", info])
                perf["video"] += time.perf_counter() - _t0

            if source != "camera":
                time.sleep(max(0.0, 1.0 / 30 - (time.time() - now)))
    except KeyboardInterrupt:
        pass
    finally:
        if bridge:
            bridge.close()
        if cam:
            cam.release()
        print("Bo nao da dung (da gui lenh dung xe)")


if __name__ == "__main__":
    main()
