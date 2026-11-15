"""
Thu model detect bong (.onnx) truc tiep tren camera, KHONG can Unity / ESP32 / ArUco.
Chay:  bam dup tools/test_ball_onnx.bat  (hoac: python tools/test_ball_onnx.py tu thu muc Brain)
       Them duong dan anh/video de thu tren file:  python tools/test_ball_onnx.py anh.jpg
Hien: box + do tin cay, so bong, thoi gian suy luan (ms), FPS, chay GPU hay CPU.
Phim: '+'/'-' tang/giam nguong conf,  's' luu anh,  'q' thoat.
"""
import os
import sys
import time

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))   # thu muc Brain

import cv2

import config as C
from perception.ball_detector_onnx import OnnxBallDetector
from perception.vision import Camera

det = OnnxBallDetector(C.BALL_MODEL_PATH, C.BALL_CONF, C.BALL_IOU, C.BALL_USE_GPU)

src = sys.argv[1] if len(sys.argv) > 1 else None
still = None
if src and os.path.splitext(src)[1].lower() in (".jpg", ".jpeg", ".png", ".bmp"):
    still = cv2.imread(src)
    cam = None
elif src:
    cam = cv2.VideoCapture(src)
else:
    cam = Camera()

fps, t_prev, n_save = 0.0, time.perf_counter(), 0
while True:
    if still is not None:
        frame = still.copy()
    elif isinstance(cam, Camera):
        frame = cam.read()
    else:
        ok, frame = cam.read()
        frame = frame if ok else None
    if frame is None:
        if not isinstance(cam, Camera) and still is None:
            break
        continue

    dets = det.detect(frame)
    for cx, cy, r, conf in dets:
        cv2.circle(frame, (int(cx), int(cy)), int(max(r, 3)), (0, 255, 0), 2)
        cv2.putText(frame, f"{conf:.2f}", (int(cx + r + 2), int(cy)), cv2.FONT_HERSHEY_SIMPLEX, 0.5, (0, 255, 0), 1)

    now = time.perf_counter()
    fps = 0.9 * fps + 0.1 / max(now - t_prev, 1e-6)
    t_prev = now
    info = f"{det.provider.replace('ExecutionProvider', '')}  infer {det.last_ms:5.1f} ms  FPS {fps:4.1f}  balls {len(dets)}  conf {det.conf:.2f}"
    cv2.putText(frame, info, (10, 25), cv2.FONT_HERSHEY_SIMPLEX, 0.7, (0, 0, 255), 2)
    cv2.imshow("test_ball_onnx", frame)

    k = cv2.waitKey(1) & 0xFF
    if k == ord("q"):
        break
    if k in (ord("+"), ord("=")):
        det.conf = min(0.95, det.conf + 0.05)
    if k == ord("-"):
        det.conf = max(0.05, det.conf - 0.05)
    if k == ord("s"):
        n_save += 1
        name = f"ball_test_{n_save}.jpg"
        cv2.imwrite(name, frame)
        print("Da luu", name)

if cam is not None:
    cam.release()
cv2.destroyAllWindows()
