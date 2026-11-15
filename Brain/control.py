"""
Lap thu tu nhat bong (nearest-neighbour + 2-opt) va bam theo bong (lai theo goc lech).
"""
import math

import config as C


def wrap(a):
    return (a + math.pi) % (2 * math.pi) - math.pi


def intake_point(x, y, yaw):
    d = C.INTAKE_AHEAD_M * 1000
    return x + d * math.cos(yaw), y + d * math.sin(yaw)


# ------------------------------------------------------------------ lap thu tu

def _d(a, b):
    return math.hypot(a[0] - b[0], a[1] - b[1])


def path_length(start, order, pts):
    """Tong quang duong (mm): start -> pts[order[0]] -> ... (duong ho, khong quay ve)."""
    total, cur = 0.0, start
    for i in order:
        total += _d(cur, pts[i])
        cur = pts[i]
    return total


def nearest_neighbour(start, ids, pts):
    order, left, cur = [], set(ids), start
    while left:
        n = min(left, key=lambda i: _d(cur, pts[i]))
        order.append(n)
        left.remove(n)
        cur = pts[n]
    return order


def two_opt(start, order, pts, max_rounds=50):
    """
    2-opt tren duong ho bat dau tu start: dao nguoc doan order[i..k] neu tong quang duong ngan di.
    Lap toi khi khong con cai thien (thuong vai vong). Do phuc tap moi vong O(n^2).
    """
    order = list(order)
    n = len(order)
    if n < 2:
        return order
    for _ in range(max_rounds):
        improved = False
        for i in range(n - 1):
            a = start if i == 0 else pts[order[i - 1]]
            b = pts[order[i]]
            for k in range(i + 1, n):
                c = pts[order[k]]
                dk = pts[order[k + 1]] if k + 1 < n else None
                old = _d(a, b) + (_d(c, dk) if dk is not None else 0.0)
                new = _d(a, c) + (_d(b, dk) if dk is not None else 0.0)
                if new < old - 1e-6:
                    order[i:k + 1] = reversed(order[i:k + 1])
                    b = pts[order[i]]
                    improved = True
        if not improved:
            break
    return order


def best_order(start, ids, pts):
    return two_opt(start, nearest_neighbour(start, ids, pts), pts)


class Planner:
    """
    Thu tu nhat = nearest-neighbour roi toi uu bang 2-opt (moi khung hinh).
    Chong doi muc tieu lien tuc: chi doi bong muc tieu khi thu tu moi ngan hon
    thu tu giu muc tieu cu it nhat PLAN_SWITCH_MM.
    """

    def __init__(self):
        self.target = None

    def plan(self, robot, balls):
        if not balls:
            self.target = None
            return []
        start = intake_point(*robot)
        order = best_order(start, balls.keys(), balls)
        if self.target in balls and order[0] != self.target:
            rest = [i for i in balls if i != self.target]
            keep = [self.target] + best_order(balls[self.target], rest, balls)
            if path_length(start, order, balls) > path_length(start, keep, balls) - C.PLAN_SWITCH_MM:
                order = keep
        self.target = order[0]
        return order


# ------------------------------------------------------------------ bam bong

def pursue(robot, target):
    """Tra ve (v m/s, w rad/s) de dua diem gom toi bong."""
    x, y, yaw = robot
    alpha = wrap(math.atan2(target[1] - y, target[0] - x) - yaw)
    w = max(-C.W_MAX, min(C.W_MAX, C.K_W * alpha))
    ix, iy = intake_point(x, y, yaw)
    dist = math.hypot(target[0] - ix, target[1] - iy) / 1000.0        # tu diem gom toi bong
    slow = min(1.0, max(C.APPROACH_MIN_RATIO, dist / C.SLOW_DIST_M))
    v = C.V_MAX * max(0.0, math.cos(alpha)) * slow
    if abs(alpha) > math.radians(C.TURN_IN_PLACE_DEG):
        v = 0.0                    # lech nhieu: quay tai cho truoc
        if math.hypot(target[0] - x, target[1] - y) < 0.9 * C.INTAKE_AHEAD_M * 1000:
            v = -0.3 * C.V_MAX     # bong qua sat truc banh (diem gom khong voi toi): lui ra
    return v, w


def push_in(robot, held):
    """
    Dang co bong trong pheu: tien thang voi PUSH_SPEED de bong ap vao long xoay va bi cuon vao.
    Lai nhe de qua bong gan long nhat nam giua be ngang long. Tra ve (v m/s, w rad/s).
    """
    if not held:
        return C.PUSH_SPEED, 0.0                            # khong con thay bong: tien thang
    x, y, yaw = robot
    c, s = math.cos(yaw), math.sin(yaw)
    loc = [((bx - x) * c + (by - y) * s, -(bx - x) * s + (by - y) * c) for bx, by in held]
    lx, ly = min(loc, key=lambda p: p[0])                   # qua sat long nhat
    alpha = math.atan2(ly, max(lx, 100.0))
    w = max(-C.PUSH_W_MAX, min(C.PUSH_W_MAX, C.K_W * alpha))
    return C.PUSH_SPEED, w


def rate_limit(prev, target, max_rate, dt):
    """Gioi han toc do thay doi (gia toc) de xe khong giat / xoay dot ngot."""
    step = max_rate * dt
    return prev + max(-step, min(step, target - prev))


def keep_in_field(robot, v):
    """Khong cho diem gom chay ra ngoai san."""
    ix, iy = intake_point(*robot)
    m = C.FIELD_MARGIN_M * 1000
    out = not (m < ix < C.FIELD_X_MM - m and m < iy < C.FIELD_Y_MM - m)
    return 0.0 if (out and v > 0) else v
