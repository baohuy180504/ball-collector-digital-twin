"""
Gan ID on dinh cho bong giua cac frame + xac dinh bong DA GOM theo co cau gom that.

Co cau gom (he xe, mm; goc = tam truc banh, x ve dau xe, y sang trai):
  - Long xoay nam ngay tren truc banh; truoc long la PHEU (vung giu bong) toi mieng HOLD_FRONT_X_MM.
  - Thuc te: bong vao pheu se duoc long cuon vao khi xe TIEP TUC TIEN (vd dang chay toi bong ke tiep).

Trang thai moi bong:
  TU DO --(thay bong nam trong pheu)--> DA HUNG --(bien mat lien tuc >= PICK_CONFIRM_S)--> DA GOM
                                           |
                                           +--(thay lai ben ngoai pheu / xe lui / day qua lau)--> TU DO
  - Bong DA HUNG khong con la muc tieu cua planner (xe di tiep toi bong khac, tien len la day bong vao).
  - Bong mat dau trong pheu: coi nhu van nam trong pheu va di theo xe (de ghep lai neu hien lai).
  - Bong khong the nam DUOI than xe (bong 74 mm > khe ho gam) -> vi tri nhot trong than xe la bong ma, xoa.
  - Bong tu do mat dau: chi xoa khi camera THAY RO cho do ma khong co bong; bi khuat -> giu.
"""
import math

import config as C


def to_robot_frame(robot, x, y):
    """Diem (mm, he san) -> he xe (mm): x ve phia dau xe, y sang trai. robot = (x, y, yaw) tam xoay."""
    rx, ry, yaw = robot
    dx, dy = x - rx, y - ry
    c, s = math.cos(yaw), math.sin(yaw)
    return dx * c + dy * s, -dx * s + dy * c


def from_robot_frame(robot, lx, ly):
    """Nguoc lai: he xe (mm) -> he san (mm)."""
    rx, ry, yaw = robot
    c, s = math.cos(yaw), math.sin(yaw)
    return rx + lx * c - ly * s, ry + lx * s + ly * c


# ------------------------------------------------------------------ hinh hoc co cau gom
def cage_front_x():
    """Mep truoc long xoay (mm, he xe)."""
    return C.CAGE_X_MM + C.CAGE_RADIUS_MM


def contact_x():
    """Tam bong khi bong AP vao long (mm, he xe)."""
    return cage_front_x() + C.BALL_DIAMETER_MM / 2.0


def hold_half_width(lx):
    """Nua be rong pheu tai khoang cach lx (noi suy tuyen tinh tu sat long toi mieng pheu)."""
    x0, x1 = cage_front_x(), C.HOLD_FRONT_X_MM
    k = 0.0 if x1 <= x0 else min(1.0, max(0.0, (lx - x0) / (x1 - x0)))
    return C.HOLD_BACK_HALF_W_MM + k * (C.HOLD_FRONT_HALF_W_MM - C.HOLD_BACK_HALF_W_MM)


def in_hold_zone_local(lx, ly):
    return cage_front_x() <= lx <= C.HOLD_FRONT_X_MM and abs(ly) <= hold_half_width(lx)


def in_hold_zone(robot, x, y):
    """Bong (he san) dang nam trong pheu truoc long xoay."""
    if robot is None:
        return False
    return in_hold_zone_local(*to_robot_frame(robot, x, y))


def at_cage_local(lx, ly):
    """Bong dang ap vao long (du gan de long cuon) va nam trong be ngang long."""
    return lx <= contact_x() + C.CONTACT_TOL_MM and abs(ly) <= C.CAGE_HALF_WIDTH_MM


def hold_zone_polygon_mm(robot):
    """4 dinh pheu (he san, mm) de ve len anh."""
    x0, x1 = cage_front_x(), C.HOLD_FRONT_X_MM
    loc = [(x0, C.HOLD_BACK_HALF_W_MM), (x1, C.HOLD_FRONT_HALF_W_MM),
           (x1, -C.HOLD_FRONT_HALF_W_MM), (x0, -C.HOLD_BACK_HALF_W_MM)]
    return [from_robot_frame(robot, lx, ly) for lx, ly in loc]


def in_robot_body(robot, x, y, margin_mm=20.0):
    """Diem nam trong THAN xe (phia sau mep truoc long xoay) - khong the la bong tren san."""
    if robot is None:
        return False
    lx, ly = to_robot_frame(robot, x, y)
    r, hw = C.BODY_REAR_M * 1000, C.BODY_HALF_WIDTH_M * 1000
    return -r - margin_mm <= lx <= cage_front_x() - 10.0 and abs(ly) <= hw + margin_mm


def near_robot_body(robot, x, y, margin_mm=None):
    """Diem nam trong footprint xe noi rong them margin (luat 'bi che' cu, dung cho che do sim)."""
    if robot is None:
        return False
    m = C.OCCLUSION_MARGIN_MM if margin_mm is None else margin_mm
    lx, ly = to_robot_frame(robot, x, y)
    f, r, hw = C.BODY_FRONT_M * 1000, C.BODY_REAR_M * 1000, C.BODY_HALF_WIDTH_M * 1000
    return -r - m <= lx <= f + m and abs(ly) <= hw + m


# ------------------------------------------------------------------ tracker
class BallTracker:
    def __init__(self, gate_mm=150.0, max_missing=8, confirm_hits=3, alpha=0.5):
        self.gate = gate_mm
        self.max_missing = max_missing
        self.confirm = confirm_hits
        self.alpha = alpha
        self.tracks = {}      # id -> dict
        self.next_id = 1

    @staticmethod
    def _new(x, y):
        return {"x": x, "y": y, "missing": 0, "unseen": 0, "hits": 1, "in_body": 0,
                "cap": False,      # da hung (dang/da nam trong pheu)
                "cap_t": 0.0,      # thoi gian da hung ma VAN THAY bong (s) - de phat hien bong ket
                "miss_t": 0.0}     # thoi gian mat dau lien tuc (s)

    def captured_ids(self):
        """ID bong da hung (planner bo qua)."""
        return {tid for tid, t in self.tracks.items() if t["cap"] and t["hits"] >= self.confirm}

    def update(self, dets, robot=None, v=0.0, occluded=None, dt=1.0 / 30):
        """
        dets: list (x_mm, y_mm). robot: pose xe (x, y, yaw). v: van toc lenh (m/s). dt: s.
        occluded(x, y) -> True neu camera khong the thay vi tri do (vision.make_occlusion_test).
        Tra ve (confirmed: dict id->(x,y), removed: list (id, x, y, picked)).
        """
        if occluded is None:
            occluded = lambda x, y: near_robot_body(robot, x, y)

        # ---------- 1. ghep phat hien moi voi bong cu (gan nhat truoc) ----------
        pairs = []
        for tid, t in self.tracks.items():
            for j, (x, y) in enumerate(dets):
                d = math.hypot(t["x"] - x, t["y"] - y)
                if d < self.gate:
                    pairs.append((d, tid, j))
        pairs.sort()
        used_t, used_d = set(), set()
        for _, tid, j in pairs:
            if tid in used_t or j in used_d:
                continue
            t = self.tracks[tid]
            t["x"] += self.alpha * (dets[j][0] - t["x"])
            t["y"] += self.alpha * (dets[j][1] - t["y"])
            t["missing"] = t["unseen"] = t["in_body"] = 0
            t["miss_t"] = 0.0
            t["hits"] += 1
            self._seen(t, robot, v, dt)
            used_t.add(tid)
            used_d.add(j)

        # ---------- 2. bong khong thay trong khung nay ----------
        removed = []
        for tid in list(self.tracks):
            if tid in used_t:
                continue
            t = self.tracks[tid]
            t["missing"] += 1
            t["miss_t"] += dt
            picked = gone = False
            if t["cap"] and v < -0.02:
                t["cap"] = False                                   # xe lui: bong o lai, khong con trong pheu
            if t["cap"] and robot is not None:
                # bong trong pheu: di theo xe, bi long chan (chua the chui vao than xe neu chua cuon)
                lx, ly = to_robot_frame(robot, t["x"], t["y"])
                lx = max(lx, contact_x())
                hw = max(0.0, hold_half_width(lx) - C.BALL_DIAMETER_MM / 2.0)
                ly = max(-hw, min(hw, ly))
                t["x"], t["y"] = from_robot_frame(robot, lx, ly)
                if t["miss_t"] >= C.PICK_CONFIRM_S:
                    picked = gone = True                           # mat trong pheu du lau -> da vao khoang chua
            else:
                if in_robot_body(robot, t["x"], t["y"], margin_mm=0.0):
                    t["in_body"] += 1                              # vi tri nam duoi than xe: khong the co bong
                if not occluded(t["x"], t["y"]):
                    t["unseen"] += 1                               # camera thay ro cho do ma khong co bong
                gone = (t["unseen"] > self.max_missing or t["in_body"] > 3
                        or t["missing"] > C.BALL_HOLD_FRAMES)
            if gone:
                if t["hits"] >= self.confirm:
                    removed.append((tid, t["x"], t["y"], picked))
                del self.tracks[tid]

        # ---------- 3. phat hien moi chua ghep -> bong moi ----------
        for j, (x, y) in enumerate(dets):
            if j not in used_d:
                t = self._new(x, y)
                self._seen(t, robot, v, dt)
                self.tracks[self.next_id] = t
                self.next_id += 1

        confirmed = {tid: (t["x"], t["y"]) for tid, t in self.tracks.items() if t["hits"] >= self.confirm}
        return confirmed, removed

    @staticmethod
    def _seen(t, robot, v, dt):
        """Bong DANG THAY: vao pheu -> da hung; thay ben ngoai pheu -> tu do."""
        if robot is None:
            return
        lx, ly = to_robot_frame(robot, t["x"], t["y"])
        if in_hold_zone_local(lx, ly) and v > -0.02:
            if not t["cap"]:
                t["cap"], t["cap_t"] = True, 0.0
            t["cap_t"] += dt
            if t["cap_t"] > C.PUSH_TIMEOUT_S:
                t["cap"] = False          # nam trong pheu qua lau ma khong vao (ket) -> tha, xe lam lai
                t["cap_t"] = -C.PUSH_TIMEOUT_S   # cho mot luc moi cho hung lai
        else:
            t["cap"] = False
            t["cap_t"] = min(t["cap_t"], 0.0)
