using System;

namespace DigitalTwin
{
    // Goi JSON Python gui sang Unity qua UDP (xem Assets/DigitalTwin/README.md).
    // Vi tri: mm trong he toa do san. Goc: do. Toc do banh: rad/s.

    [Serializable]
    public class RobotState
    {
        public bool valid = true;     // false khi camera mat ma ArUco
        public float x, y;            // mm
        public float yaw;             // do, nguoc chieu kim dong ho tu truc X san
        public float roll, pitch;     // do, tu IMU
        public float wl, wr;          // rad/s banh trai/phai tu encoder (0 neu khong co)
        public bool tilt_alarm;       // ESP32 bao nghieng qua nguong
    }

    [Serializable]
    public class BallState
    {
        public int id;
        public float x, y;            // mm
        public bool @virtual;         // bong ao dat tu Unity (JSON: "virtual")
        public int rank;              // thu tu nhat theo planner (1 = bong dang toi, 0 = chua xep)
    }

    [Serializable]
    public class PathPoint
    {
        public float x, y;            // mm
    }

    [Serializable]
    public class TwinMessage
    {
        public double t;              // giay (thoi gian phia Python)
        public RobotState robot;
        public BallState[] balls;
        public PathPoint[] path;
        public string status;

        // Trang thai bo nao
        public string mode;           // "auto" | "manual" | "stop"
        public bool esp_connected;
        public float batt;            // V
        public float fps;
        public int target_id = -1;
        public int collected;

        // Camera (nut CAMERA tren CONTROL PANEL)
        public int[] cameras;         // cac camera index tim thay
        public int cam_index = -1;    // camera dang dung
        public bool cam_scanning;     // dang do tim camera
        public float cmd_v, cmd_w;    // lenh dang gui xuong xe

        // Che do test xe (source = "odom")
        public string source;         // "odom" | "sim" | "camera"
        public string esp_ip;
        public float tele_rate;       // goi telemetry / s tu ESP32
        public int enc_l, enc_r;      // xung encoder cong don
        public float gz;              // do/s
        public float dist;            // m, quang duong odometry
        public int pwm_l, pwm_r;      // PWM dang gui xuong xe
        public float ball_d;          // mm, duong kinh bong (config.py)
        public float view_deg;        // huong truc X san tren anh camera (do, 0 = sang phai, nguoc kim dong ho)
        public bool view_ok;          // da biet huong (camera thay ma goc)
    }
}
