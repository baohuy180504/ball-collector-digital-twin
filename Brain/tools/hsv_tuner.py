"""
Chinh nguong mau HSV cho bong bang thanh keo.
Chay:  bam dup tools/hsv_tuner.bat  (hoac: python tools/hsv_tuner.py tu thu muc Brain)
       -> keo cho den khi cua so 'mask' chi con bong mau trang.
Nhan 's' de in gia tri (chep vao config.py), 'q' de thoat.
"""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))   # thu muc Brain

import cv2
import numpy as np

import config as C
from perception.vision import Camera

cam = Camera()
cv2.namedWindow("mask")
lo, hi = C.BALL_HSV_LOW, C.BALL_HSV_HIGH
for name, val, mx in [("H lo", lo[0], 179), ("S lo", lo[1], 255), ("V lo", lo[2], 255),
                      ("H hi", hi[0], 179), ("S hi", hi[1], 255), ("V hi", hi[2], 255)]:
    cv2.createTrackbar(name, "mask", val, mx, lambda _: None)

while True:
    frame = cam.read()
    if frame is None:
        continue
    g = lambda n: cv2.getTrackbarPos(n, "mask")
    low, high = (g("H lo"), g("S lo"), g("V lo")), (g("H hi"), g("S hi"), g("V hi"))
    mask = cv2.inRange(cv2.cvtColor(frame, cv2.COLOR_BGR2HSV), np.array(low), np.array(high))
    cv2.imshow("camera", cv2.resize(frame, (640, 360)))
    cv2.imshow("mask", cv2.resize(mask, (640, 360)))
    k = cv2.waitKey(1) & 0xFF
    if k == ord("s"):
        print(f"BALL_HSV_LOW = {low}\nBALL_HSV_HIGH = {high}")
    elif k == ord("q"):
        break
cam.release()
cv2.destroyAllWindows()
