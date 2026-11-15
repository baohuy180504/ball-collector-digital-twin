using System.Globalization;
using System.Net.Sockets;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;

namespace DigitalTwin
{
    /// <summary>
    /// Gui lenh tu Unity sang bo nao Python (UDP): che do AUTO / MANUAL / STOP va lai tay.
    /// Phim: W A S D hoac mui ten = lai (o che do MANUAL), M = MANUAL, O = AUTO, P = PAUSE, Space = dung het.
    /// Lai tay co 2 kieu: PWM truc tiep (giong web.py) hoac van toc v, w (qua bo PI tren Python).
    /// </summary>
    [DisallowMultipleComponent]
    public class TwinControl : MonoBehaviour
    {
        public string brainIp = "127.0.0.1";
        public int cmdPort = 5107;
        public float sendHz = 20f;

        [Header("Lai tay")]
        [Tooltip("Bat: gui PWM truc tiep (test xe). Tat: gui v, w de Python tu dieu khien toc do banh.")]
        public bool rawPwm = true;
        [Range(70, 255)] public int pwm = 160;
        [Range(0.05f, 0.5f)] public float manualSpeed = 0.20f;    // m/s
        [Range(0.2f, 3f)] public float manualTurn = 1.2f;         // rad/s

        public string Mode { get; private set; } = "stop";
        public float CmdV { get; private set; }
        public float CmdW { get; private set; }
        public int PwmL { get; private set; }
        public int PwmR { get; private set; }

        // Nut tren man hinh (TwinHud dat gia tri)
        [System.NonSerialized] public bool uiUp, uiDown, uiLeft, uiRight;

        UdpClient udp;
        float timer;
        string lastJson = "";

        void OnEnable()
        {
            if (cmdPort == 5007) cmdPort = 5107;      // scene cu: 5005-5007 nay danh cho ESP32
            udp = new UdpClient();
        }

        void OnDisable()
        {
            Mode = "stop";
            CmdV = CmdW = 0f;
            PwmL = PwmR = 0;
            Send(Build());
            udp?.Close();
            udp = null;
        }

        public void SetMode(string m)
        {
            Mode = m;
            if (m != "manual")
            {
                CmdV = CmdW = 0f;
                PwmL = PwmR = 0;
            }
            Send(Build());
        }

        public bool IsManual => Mode == "manual";
        public bool IsAuto => Mode == "auto" || Mode == "pause";
        public bool IsPaused => Mode == "pause";

        /// <summary>MANUAL bat/tat. Bat MANUAL thi tat AUTO.</summary>
        public void ToggleManual() => SetMode(IsManual ? "stop" : "manual");

        /// <summary>AUTO bat/tat. Bat AUTO thi tat MANUAL.</summary>
        public void ToggleAuto() => SetMode(IsAuto ? "stop" : "auto");

        /// <summary>PAUSE chi co tac dung khi dang AUTO: dung tam thoi / chay tiep.</summary>
        public void TogglePause()
        {
            if (Mode == "auto") SetMode("pause");
            else if (Mode == "pause") SetMode("auto");
        }

        public void ResetOdom() => Send("{\"action\":\"reset_odom\"}");

        /// <summary>Dat bong ao tai (x, y) mm tren san. Python dua vao lap path, xe AUTO se gom.</summary>
        public void AddVirtualBall(float xMm, float yMm)
        {
            var ci = CultureInfo.InvariantCulture;
            Send("{\"action\":\"add_ball\",\"x\":" + xMm.ToString("0.#", ci) + ",\"y\":" + yMm.ToString("0.#", ci) + "}");
        }

        public void ClearVirtualBalls() => Send("{\"action\":\"clear_balls\"}");

        /// <summary>Yeu cau Python do tim lai danh sach camera.</summary>
        public void ScanCameras() => Send("{\"action\":\"scan_cameras\"}");

        /// <summary>Doi sang camera co index (OpenCV) nay.</summary>
        public void SelectCamera(int index) =>
            Send("{\"action\":\"set_camera\",\"index\":" + index.ToString(CultureInfo.InvariantCulture) + "}");

        void Update()
        {
            var kb = Keyboard.current;
            bool up = uiUp, down = uiDown, left = uiLeft, right = uiRight;
            if (kb != null)
            {
                up |= kb.wKey.isPressed || kb.upArrowKey.isPressed;
                down |= kb.sKey.isPressed || kb.downArrowKey.isPressed;
                left |= kb.aKey.isPressed || kb.leftArrowKey.isPressed;
                right |= kb.dKey.isPressed || kb.rightArrowKey.isPressed;
                if (kb.mKey.wasPressedThisFrame) ToggleManual();
                if (kb.oKey.wasPressedThisFrame) ToggleAuto();
                if (kb.pKey.wasPressedThisFrame) TogglePause();
                if (kb.spaceKey.wasPressedThisFrame) SetMode("stop");
            }

            float fwd = (up ? 1f : 0f) - (down ? 1f : 0f);
            float turn = (left ? 1f : 0f) - (right ? 1f : 0f);
            CmdV = CmdW = 0f;
            PwmL = PwmR = 0;
            if (Mode == "manual")
            {
                if (rawPwm)
                {
                    // giong web.py: tien (+,+), lui (-,-), trai (-,+), phai (+,-)
                    PwmL = Mathf.RoundToInt(Mathf.Clamp(fwd - turn, -1f, 1f) * pwm);
                    PwmR = Mathf.RoundToInt(Mathf.Clamp(fwd + turn, -1f, 1f) * pwm);
                }
                else
                {
                    CmdV = fwd * manualSpeed;
                    CmdW = turn * manualTurn;
                }
            }

            string json = Build();
            timer += Time.unscaledDeltaTime;
            if (json != lastJson || timer >= 1f / sendHz)   // doi lenh thi gui ngay
            {
                timer = 0f;
                Send(json);
            }
        }

        string Build()
        {
            var ci = CultureInfo.InvariantCulture;
            if (Mode == "manual" && rawPwm)
                return "{\"mode\":\"manual\",\"pwm_l\":" + PwmL.ToString(ci) + ",\"pwm_r\":" + PwmR.ToString(ci) + "}";
            return "{\"mode\":\"" + Mode + "\",\"v\":" + CmdV.ToString("0.###", ci) + ",\"w\":" + CmdW.ToString("0.###", ci) + "}";
        }

        void Send(string json)
        {
            lastJson = json;
            if (udp == null) return;
            byte[] b = Encoding.UTF8.GetBytes(json);
            try { udp.Send(b, b.Length, brainIp, cmdPort); } catch { }
        }
    }
}
