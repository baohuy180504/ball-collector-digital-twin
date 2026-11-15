"""
Detect bong bang model YOLO (.onnx) xuat tu Ultralytics, chay bang ONNX Runtime.
  - Uu tien GPU (CUDAExecutionProvider), khong co thi tu chuyen sang CPU.
  - Doc kich thuoc dau vao tu chinh file .onnx (vd 640x640), letterbox giong luc train.
  - Dau ra YOLO (1, 4+nc, N): cx, cy, w, h, diem tung lop -> loc nguong + NMS.
Tra ve list (cx_px, cy_px, r_px, conf) tren anh goc.
"""
import glob
import os
import site
import sys
import time

import cv2
import numpy as np


def _add_cuda_dlls():
    """Them thu muc DLL cua cac goi nvidia-*-cu12 (cai qua pip) vao PATH. Can cho onnxruntime < 1.21 tren Windows."""
    if os.name != "nt":
        return []
    roots = list(site.getsitepackages()) + [os.path.join(sys.prefix, "Lib", "site-packages")]
    dirs = []
    for sp in roots:
        for d in glob.glob(os.path.join(sp, "nvidia", "*", "bin")):
            if d not in dirs:
                dirs.append(d)
    if dirs:
        os.environ["PATH"] = os.pathsep.join(dirs) + os.pathsep + os.environ.get("PATH", "")
        for d in dirs:
            try:
                os.add_dll_directory(d)
            except (OSError, AttributeError):
                pass
    return dirs


_add_cuda_dlls()
import onnxruntime as ort  # noqa: E402  (import sau khi da them DLL CUDA)


class OnnxBallDetector:
    def __init__(self, model_path, conf=0.15, iou=0.5, use_gpu=True, class_ids=None):
        if not os.path.exists(model_path):
            raise FileNotFoundError(f"Khong thay model: {model_path}")
        self.conf, self.iou = conf, iou
        self.class_ids = class_ids                 # None = moi lop (model hien tai chi co 'ball')
        so = ort.SessionOptions()
        so.graph_optimization_level = ort.GraphOptimizationLevel.ORT_ENABLE_ALL
        so.log_severity_level = 3                  # an canh bao vat vanh
        providers = ["CPUExecutionProvider"]
        if use_gpu and "CUDAExecutionProvider" in ort.get_available_providers():
            providers = ["CUDAExecutionProvider", "CPUExecutionProvider"]
        self.sess = ort.InferenceSession(model_path, so, providers=providers)
        self.provider = self.sess.get_providers()[0]
        inp = self.sess.get_inputs()[0]
        self.input_name = inp.name
        self.in_h, self.in_w = int(inp.shape[2]), int(inp.shape[3])
        self.output_name = self.sess.get_outputs()[0].name
        self.last_ms = 0.0
        # warm-up: lan chay dau luon cham (khoi tao GPU)
        self.sess.run(None, {self.input_name: np.zeros((1, 3, self.in_h, self.in_w), np.float32)})
        print(f"[BallDetector] {os.path.basename(model_path)}  input {self.in_w}x{self.in_h}  chay tren: {self.provider}")
        if self.provider != "CUDAExecutionProvider" and use_gpu:
            print("[BallDetector] CANH BAO: khong dung duoc GPU -> chay CPU (cham hon). Xem check_ort.py")

    # ---------- tien xu ly: letterbox giong Ultralytics ----------
    def _letterbox(self, img):
        h, w = img.shape[:2]
        s = min(self.in_w / w, self.in_h / h)
        nw, nh = int(round(w * s)), int(round(h * s))
        px, py = (self.in_w - nw) // 2, (self.in_h - nh) // 2
        canvas = np.full((self.in_h, self.in_w, 3), 114, np.uint8)
        canvas[py:py + nh, px:px + nw] = cv2.resize(img, (nw, nh), interpolation=cv2.INTER_LINEAR)
        return canvas, s, px, py

    def detect(self, frame_bgr):
        t0 = time.perf_counter()
        img, s, px, py = self._letterbox(frame_bgr)
        blob = cv2.dnn.blobFromImage(img, 1.0 / 255.0, swapRB=True)        # BGR->RGB, /255, NCHW float32
        out = self.sess.run([self.output_name], {self.input_name: blob})[0]
        pred = np.squeeze(out, 0).T                                          # (N, 4+nc)
        boxes, scores_all = pred[:, :4], pred[:, 4:]
        if self.class_ids is not None:
            scores_all = scores_all[:, self.class_ids]
        scores = scores_all.max(axis=1)
        keep = scores >= self.conf
        boxes, scores = boxes[keep], scores[keep]
        result = []
        if len(scores):
            # cx,cy,w,h (anh letterbox) -> x,y,w,h tren anh goc
            cx = (boxes[:, 0] - px) / s
            cy = (boxes[:, 1] - py) / s
            bw, bh = boxes[:, 2] / s, boxes[:, 3] / s
            xywh = np.stack([cx - bw / 2, cy - bh / 2, bw, bh], axis=1)
            idx = cv2.dnn.NMSBoxes(xywh.tolist(), scores.tolist(), self.conf, self.iou)
            for i in np.array(idx).reshape(-1):
                result.append((float(cx[i]), float(cy[i]), float((bw[i] + bh[i]) / 4.0), float(scores[i])))
        self.last_ms = (time.perf_counter() - t0) * 1000.0
        return result
