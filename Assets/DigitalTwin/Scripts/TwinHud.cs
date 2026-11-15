using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace DigitalTwin
{
    /// <summary>
    /// Man hinh chia 2 (giao dien tieng Anh):
    ///   trai : mo hinh Unity, nut [Clear] [2D/3D View] goc tren phai. O che do 3D: keo chuot trai = xoay, chuot phai/giua = di chuyen, cuon = phong to/thu nho.
    ///   phai : CONTROL PANEL [MANUAL] [AUTO] [PAUSE] [ADD BALL], anh camera,
    ///          vung lai tay (thanh toc do + 4 nut mui ten) | dong ho attitude, 4 thong so Roll Pitch Yaw v.
    /// Nut dang bat co mau xanh la.
    /// </summary>
    public class TwinHud : MonoBehaviour
    {
        public TwinUdpReceiver receiver;
        public CameraFeedReceiver feed;
        public TwinControl control;
        public BrainLauncher launcher;
        public RobotTwin robot;
        public BallManager balls;
        public FieldView field;
        public Camera cam;

        [Range(0.4f, 0.8f)] public float twinWidth = 0.6f;
        public bool topView = true;
        [Tooltip("Xoay goc nhin 2D cho giong huong anh camera that (theo ma goc tren san)")]
        public bool alignToCamera = true;
        [Tooltip("Pin day (V) - 3S Li-ion/LiPo = 12.6")] public float batteryFullV = 12.6f;
        [Tooltip("Pin can (V) - 3S = 9.9")] public float batteryEmptyV = 9.9f;
        [Tooltip("Cap nhat so pin moi bao nhieu giay (lay trung binh trong khoang do)")] public float batteryUpdateSec = 60f;
        float viewDeg;                 // goc xoay hien tai cua goc nhin 2D

        [Header("3D view (chuot)")]
        public float orbitSpeed = 0.25f;
        public float zoomStep = 0.12f;

        GUIStyle title, label, center, badgeOk, badgeWarn, arrow, tileLabel, tileValue, btn, btnOn, statusStyle, card, ladderLabel, speedTitle, speedValue, speedScale;
        Texture2D triTex, skyTex, groundTex, bezelTex, horizonTex;
        Color[] horizonPx;
        float lastRoll = 999f, lastPitch = 999f;
        const int HorizonN = 160;
        readonly List<Texture2D> textures = new List<Texture2D>();
        int lastW, lastH;
        float lastTwinWidth;

        // dat bong ao
        bool placing;
        GameObject ghost;
        Rect viewBtnRect, clearBtnRect, restartBtnRect, battRect;
        GUIStyle btnYellow, hintStyle, menuItem;

        // menu chon camera
        bool camMenuOpen;
        Rect camBtnRect;

        Vector3 orbitTarget;
        float orbitYaw, orbitPitch, orbitDist;
        bool orbitInit;

        static readonly Color Cyan = new Color(0.0f, 0.74f, 0.86f);
        static readonly Color CyanEdge = new Color(0.0f, 0.5f, 0.62f);
        static readonly Color CyanHover = new Color(0.2f, 0.85f, 0.95f);
        static readonly Color Green = new Color(0.0f, 0.85f, 0.4f);
        static readonly Color GreenEdge = new Color(0.0f, 0.55f, 0.27f);
        static readonly Color Red = new Color(1f, 0.24f, 0.44f);
        static readonly Color TitleCol = new Color(0.0f, 0.9f, 1f);
        static readonly Color PanelBg = new Color(0.06f, 0.08f, 0.11f);
        static readonly Color CardBg = new Color(0.09f, 0.11f, 0.15f);
        static readonly Color CardEdge = new Color(0.16f, 0.19f, 0.24f);
        static readonly Color Yellow = new Color(1f, 0.85f, 0.15f);
        static readonly Color GreenBright = new Color(0.16f, 1f, 0.52f);
        static readonly Color GreenBrightHover = new Color(0.45f, 1f, 0.68f);
        static readonly Color GreenBrightEdge = new Color(0.0f, 0.72f, 0.36f);
        static readonly Color TrackBg = new Color(0.16f, 0.19f, 0.25f);

        T FindOrAdd<T>() where T : Component
        {
            var c = FindFirstObjectByType<T>();
            if (c == null) c = gameObject.AddComponent<T>();
            return c;
        }

        void Start()
        {
            // Tu them component con thieu (scene tao truoc khi co cac script nay)
            if (receiver == null) receiver = FindOrAdd<TwinUdpReceiver>();
            if (feed == null) feed = FindOrAdd<CameraFeedReceiver>();
            if (control == null) control = FindOrAdd<TwinControl>();
            if (launcher == null) launcher = FindOrAdd<BrainLauncher>();
            if (robot == null) robot = FindFirstObjectByType<RobotTwin>();
            if (balls == null) balls = FindFirstObjectByType<BallManager>();
            if (field == null) field = FindFirstObjectByType<FieldView>();
            if (cam == null) cam = Camera.main;
            if (control != null) control.rawPwm = true;     // thanh toc do = PWM (giong web.py)
            ghost = GameObject.CreatePrimitive(PrimitiveType.Sphere);   // bong ao di theo chuot khi dang dat
            ghost.name = "VirtualBallGhost";
            TwinUtil.RemoveCollider(ghost);
            ghost.GetComponent<Renderer>().sharedMaterial = TwinMaterials.Lit(new Color(1f, 0.9f, 0.3f));
            ghost.SetActive(false);
            ApplyView();
        }

        void Update()
        {
            if (Screen.width != lastW || Screen.height != lastH || !Mathf.Approximately(twinWidth, lastTwinWidth))
            {
                lastW = Screen.width;
                lastH = Screen.height;
                lastTwinWidth = twinWidth;
                ApplyView();
            }
            HandlePlacement();
            if (!topView) HandleOrbitInput();
            var msg = receiver != null ? receiver.Latest : null;
            float targetDeg = alignToCamera && msg != null && msg.view_ok ? msg.view_deg : 0f;
            if (Mathf.Abs(Mathf.DeltaAngle(viewDeg, targetDeg)) > 0.05f)
            {
                viewDeg = Mathf.LerpAngle(viewDeg, targetDeg, 1f - Mathf.Exp(-6f * Time.unscaledDeltaTime));
                if (topView) ApplyView();
            }
            var kb = Keyboard.current;
            if (kb != null && kb.f5Key.wasPressedThisFrame && launcher != null) launcher.Restart();   // F5 = chay lai Python
        }

        void OnDestroy()
        {
            foreach (var t in textures) if (t != null) Destroy(t);
        }

        // ------------------------------------------------------------------ camera view

        public void ApplyView()
        {
            if (cam == null || field == null) return;
            cam.rect = new Rect(0f, 0f, twinWidth, 1f);
            cam.nearClipPlane = 0.01f;
            cam.farClipPlane = 100f;
            Vector3 c = field.CenterUnity;
            if (topView)
            {
                // Truc X san hien tren man hinh o goc viewDeg (giong anh camera). Huong "len" cua man hinh
                // trong he san = (sin, cos) -> doi sang Unity.
                cam.orthographic = true;
                float aspect = cam.aspect > 0.01f ? cam.aspect : 1f;
                float th = viewDeg * Mathf.Deg2Rad, cs = Mathf.Abs(Mathf.Cos(th)), sn = Mathf.Abs(Mathf.Sin(th));
                float halfW = field.lengthX * 0.5f * cs + field.widthY * 0.5f * sn;   // khung bao san da xoay
                float halfH = field.lengthX * 0.5f * sn + field.widthY * 0.5f * cs;
                cam.orthographicSize = Mathf.Max(halfH, halfW / aspect) * 1.12f;
                Vector3 up = RosUnity.ToUnity(Mathf.Sin(th), Mathf.Cos(th), 0f);
                cam.transform.SetPositionAndRotation(c + Vector3.up * 5f, Quaternion.LookRotation(Vector3.down, up));
            }
            else
            {
                cam.orthographic = false;
                cam.fieldOfView = 50f;
                if (!orbitInit) ResetOrbit();
                PlaceOrbitCamera();
            }
        }

        void ResetOrbit()
        {
            Vector3 c = field.CenterUnity;
            float s = Mathf.Max(field.lengthX, field.widthY);
            Vector3 off = RosUnity.ToUnity(-0.9f * s, -0.35f * s, 0.75f * s);
            orbitTarget = c;
            orbitDist = off.magnitude;
            Vector3 fwd = -off / orbitDist;
            orbitYaw = Mathf.Atan2(fwd.x, fwd.z) * Mathf.Rad2Deg;
            orbitPitch = -Mathf.Asin(fwd.y) * Mathf.Rad2Deg;
            orbitInit = true;
        }

        void PlaceOrbitCamera()
        {
            var rot = Quaternion.Euler(orbitPitch, orbitYaw, 0f);
            cam.transform.SetPositionAndRotation(orbitTarget + rot * Vector3.back * orbitDist, rot);
        }

        void HandleOrbitInput()
        {
            var mouse = Mouse.current;
            if (mouse == null || cam == null) return;
            Vector2 pos = mouse.position.ReadValue();
            bool inside = pos.x >= 0 && pos.x <= Screen.width * twinWidth && pos.y >= 0 && pos.y <= Screen.height;
            if (!inside) return;

            Vector2 d = mouse.delta.ReadValue();
            bool changed = false;
            if (!placing && mouse.leftButton.isPressed && d != Vector2.zero)   // xoay
            {
                orbitYaw += d.x * orbitSpeed;
                orbitPitch = Mathf.Clamp(orbitPitch - d.y * orbitSpeed, 5f, 89f);
                changed = true;
            }
            if ((mouse.rightButton.isPressed || mouse.middleButton.isPressed) && d != Vector2.zero)   // di chuyen
            {
                Vector3 right = cam.transform.right;
                Vector3 fwd = Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up).normalized;
                float k = orbitDist * 0.0015f;
                orbitTarget -= (right * d.x + fwd * d.y) * k;
                changed = true;
            }
            float sc = mouse.scroll.ReadValue().y;                               // phong to / thu nho
            if (Mathf.Abs(sc) > 0.01f)
            {
                orbitDist = Mathf.Clamp(orbitDist * (1f - Mathf.Sign(sc) * zoomStep), 0.3f, 30f);
                changed = true;
            }
            if (changed) PlaceOrbitCamera();
        }

        // ------------------------------------------------------------------ dat bong ao

        float BallRadius => balls != null ? balls.ballDiameter * 0.5f : 0.037f;

        void HandlePlacement()
        {
            if (!placing)
            {
                if (ghost != null && ghost.activeSelf) ghost.SetActive(false);
                return;
            }
            var mouse = Mouse.current;
            var kb = Keyboard.current;
            if ((kb != null && kb.escapeKey.wasPressedThisFrame) || (mouse != null && mouse.rightButton.wasPressedThisFrame))
            {
                placing = false;                                   // huy
                ghost.SetActive(false);
                return;
            }
            if (mouse == null || cam == null || field == null) return;

            Vector2 pos = mouse.position.ReadValue();
            var gui = new Vector2(pos.x, Screen.height - pos.y);
            bool inMap = pos.x >= 0 && pos.x <= Screen.width * twinWidth && pos.y >= 0 && pos.y <= Screen.height
                         && !viewBtnRect.Contains(gui) && !clearBtnRect.Contains(gui) && !restartBtnRect.Contains(gui) && !battRect.Contains(gui);
            bool ok = false;
            Vector3 hit = Vector3.zero;
            if (inMap)
            {
                var ray = cam.ScreenPointToRay(pos);
                var plane = new Plane(Vector3.up, new Vector3(0f, BallRadius, 0f));
                if (plane.Raycast(ray, out float t))
                {
                    hit = ray.GetPoint(t);
                    Vector3 ros = RosUnity.ToRos(hit);
                    ok = ros.x >= 0f && ros.x <= field.lengthX && ros.y >= 0f && ros.y <= field.widthY;
                }
            }
            ghost.SetActive(ok);
            if (!ok) return;
            ghost.transform.position = hit;
            ghost.transform.localScale = Vector3.one * (2f * BallRadius * (1f + 0.08f * Mathf.Sin(Time.unscaledTime * 8f)));

            if (mouse.leftButton.wasPressedThisFrame && control != null)
            {
                Vector3 ros = RosUnity.ToRos(hit);
                control.AddVirtualBall(ros.x * 1000f, ros.y * 1000f);
                placing = false;                                   // dat xong -> nut ve trang thai cu
                ghost.SetActive(false);
            }
        }

        // ------------------------------------------------------------------ textures ve bang code

        Texture2D NewTex(int w, int h)
        {
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.DontSave,
            };
            textures.Add(t);
            return t;
        }

        /// <summary>Hinh chu nhat bo goc (khu rang cua), dung lam nen 9-slice.</summary>
        Texture2D RoundedTex(int size, float radius, Color fill, Color edge, float edgeW)
        {
            var t = NewTex(size, size);
            var px = new Color[size * size];
            float half = size / 2f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float qx = Mathf.Abs(x + 0.5f - half) - (half - radius);
                    float qy = Mathf.Abs(y + 0.5f - half) - (half - radius);
                    float d = new Vector2(Mathf.Max(qx, 0f), Mathf.Max(qy, 0f)).magnitude + Mathf.Min(Mathf.Max(qx, qy), 0f) - radius;
                    var c = Color.Lerp(fill, edge, Mathf.Clamp01(d + edgeW + 0.5f));
                    c.a *= Mathf.Clamp01(0.5f - d);
                    px[y * size + x] = c;
                }
            t.SetPixels(px);
            t.Apply();
            return t;
        }

        /// <summary>Tam giac trang huong len (sieu lay mau 4x4 cho canh min).</summary>
        Texture2D TriangleTex(int size)
        {
            var t = NewTex(size, size);
            var px = new Color[size * size];
            Vector2 a = new Vector2(size * 0.5f, size * 0.9f), b = new Vector2(size * 0.1f, size * 0.14f), c = new Vector2(size * 0.9f, size * 0.14f);
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    int hit = 0;
                    for (int sy = 0; sy < 4; sy++)
                        for (int sx = 0; sx < 4; sx++)
                            if (InTri(new Vector2(x + (sx + 0.5f) / 4f, y + (sy + 0.5f) / 4f), a, b, c)) hit++;
                    px[y * size + x] = new Color(1f, 1f, 1f, hit / 16f);
                }
            t.SetPixels(px);
            t.Apply();
            return t;
        }

        static bool InTri(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            float d1 = (p.x - b.x) * (a.y - b.y) - (a.x - b.x) * (p.y - b.y);
            float d2 = (p.x - c.x) * (b.y - c.y) - (b.x - c.x) * (p.y - c.y);
            float d3 = (p.x - a.x) * (c.y - a.y) - (c.x - a.x) * (p.y - a.y);
            bool neg = d1 < 0 || d2 < 0 || d3 < 0, pos = d1 > 0 || d2 > 0 || d3 > 0;
            return !(neg && pos);
        }

        /// <summary>Gradient doc 1 x n (duoi -> tren).</summary>
        Texture2D GradientTex(Color bottom, Color top, int n = 64)
        {
            var t = NewTex(1, n);
            for (int i = 0; i < n; i++) t.SetPixel(0, i, Color.Lerp(bottom, top, i / (n - 1f)));
            t.Apply();
            return t;
        }

        /// <summary>Mat na dong ho: ngoai vong = mau the, vien tron toi, trong suot o giua.</summary>
        Texture2D BezelTex(int size)
        {
            var t = NewTex(size, size);
            var px = new Color[size * size];
            float h = size / 2f, ro = h - 1f, ri = h * 0.88f;
            var bezel = new Color(0.13f, 0.15f, 0.19f);
            var ring = new Color(0.0f, 0.74f, 0.86f, 0.9f);
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float r = new Vector2(x + 0.5f - h, y + 0.5f - h).magnitude;
                    Color c;
                    if (r < ri - 1f) c = new Color(0, 0, 0, 0);
                    else if (r < ri + 1f)
                    {
                        c = ring;
                        c.a *= Mathf.Clamp01((r - (ri - 1f)) / 1f);       // vong sang mong o mep trong
                    }
                    else
                    {
                        float k = Mathf.Clamp01((r - ri) / (ro - ri));
                        c = Color.Lerp(bezel * 1.25f, bezel, k);
                        c.a = 1f;
                        c = Color.Lerp(c, CardBg, Mathf.Clamp01(r - ro + 0.5f));   // ngoai vien = mau the
                    }
                    px[y * size + x] = c;
                }
            t.SetPixels(px);
            t.Apply();
            return t;
        }

        void InitStyles()
        {
            if (title != null) return;
            title = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, fontSize = 12 };
            title.normal.textColor = TitleCol;
            label = new GUIStyle(GUI.skin.label) { fontSize = 12 };
            center = new GUIStyle(label) { alignment = TextAnchor.MiddleCenter };
            statusStyle = new GUIStyle(label) { fontStyle = FontStyle.Bold };
            statusStyle.normal.textColor = Red;
            badgeOk = new GUIStyle(GUI.skin.box) { fontStyle = FontStyle.Bold, fontSize = 11, alignment = TextAnchor.MiddleCenter };
            badgeOk.normal.textColor = Green;
            badgeWarn = new GUIStyle(badgeOk);
            badgeWarn.normal.textColor = Red;
            btn = new GUIStyle(GUI.skin.button) { fontStyle = FontStyle.Bold, fontSize = 12 };
            btnOn = new GUIStyle(btn) { border = new RectOffset(8, 8, 8, 8) };       // nut dang bat: xanh la sang
            btnOn.normal.background = RoundedTex(24, 5f, GreenBright, GreenBrightEdge, 1.5f);
            btnOn.hover.background = RoundedTex(24, 5f, GreenBrightHover, GreenBrightEdge, 1.5f);
            btnOn.active.background = btnOn.normal.background;
            btnOn.normal.textColor = btnOn.hover.textColor = btnOn.active.textColor = new Color(0.02f, 0.16f, 0.07f);
            btnYellow = new GUIStyle(btnOn);                                       // nut ADD BALL dang bat: vang
            btnYellow.normal.background = RoundedTex(24, 5f, Yellow, new Color(0.75f, 0.58f, 0.0f), 1.5f);
            btnYellow.hover.background = RoundedTex(24, 5f, new Color(1f, 0.92f, 0.45f), new Color(0.75f, 0.58f, 0.0f), 1.5f);
            btnYellow.active.background = btnYellow.normal.background;
            btnYellow.normal.textColor = btnYellow.hover.textColor = btnYellow.active.textColor = new Color(0.22f, 0.16f, 0.0f);
            hintStyle = new GUIStyle(label) { fontStyle = FontStyle.Bold, fontSize = 13 };
            hintStyle.normal.textColor = Yellow;
            speedTitle = new GUIStyle(label) { fontStyle = FontStyle.Bold, fontSize = 12 };
            speedTitle.normal.textColor = new Color(0.75f, 0.8f, 0.86f);
            speedValue = new GUIStyle(label) { fontStyle = FontStyle.Bold, fontSize = 12, alignment = TextAnchor.MiddleCenter, wordWrap = false, clipping = TextClipping.Overflow };
            speedValue.normal.textColor = Color.white;
            speedScale = new GUIStyle(label) { fontSize = 10 };
            speedScale.normal.textColor = new Color(0.5f, 0.56f, 0.62f);

            card = new GUIStyle { border = new RectOffset(12, 12, 12, 12) };
            card.normal.background = RoundedTex(32, 10f, CardBg, CardEdge, 1.2f);

            arrow = new GUIStyle { border = new RectOffset(14, 14, 14, 14) };
            arrow.normal.background = RoundedTex(48, 12f, Cyan, CyanEdge, 2f);
            arrow.hover.background = RoundedTex(48, 12f, CyanHover, CyanEdge, 2f);
            arrow.active.background = RoundedTex(48, 12f, Green, GreenEdge, 2f);
            triTex = TriangleTex(64);

            tileLabel = new GUIStyle(label) { fontSize = 11, alignment = TextAnchor.UpperCenter };
            tileLabel.normal.textColor = new Color(0.6f, 0.66f, 0.72f);
            tileValue = new GUIStyle(label) { fontSize = 18, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            tileValue.normal.textColor = TitleCol;
            ladderLabel = new GUIStyle(label) { fontSize = 9, alignment = TextAnchor.MiddleCenter };
            ladderLabel.normal.textColor = new Color(1f, 1f, 1f, 0.85f);

            skyTex = GradientTex(new Color(0.35f, 0.62f, 0.9f), new Color(0.06f, 0.25f, 0.55f));     // sang gan chan troi
            groundTex = GradientTex(new Color(0.24f, 0.15f, 0.09f), new Color(0.52f, 0.34f, 0.2f));  // sang gan chan troi
            bezelTex = BezelTex(256);
        }

        GUIStyle battStyle;
        float battShown = -1f, battSum, battLastUpdate;
        int battCount;

        // So pin hien thi: trung binh trong batteryUpdateSec giay, cap nhat 1 lan moi chu ky (khong nhay lien tuc).
        // Lan dau co du lieu thi hien ngay; mat ket noi ESP32 thi xoa.
        float SmoothedBattery(float v)
        {
            if (Event.current.type != EventType.Repaint) return battShown;   // OnGUI goi nhieu lan/khung
            if (v <= 0.5f) { battShown = -1f; battSum = 0f; battCount = 0; return battShown; }
            battSum += v;
            battCount++;
            float now = Time.unscaledTime;
            if (battShown < 0f || now - battLastUpdate >= batteryUpdateSec)
            {
                battShown = battSum / battCount;
                battSum = 0f;
                battCount = 0;
                battLastUpdate = now;
            }
            return battShown;
        }

        // O pin: "11.84 V / 12.6 V" + thanh muc pin; xanh > 50%, vang 20-50%, do < 20%
        void DrawBattery(Rect r, float v)
        {
            if (battStyle == null)
            {
                battStyle = new GUIStyle(label) { fontSize = 12, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            }
            GUI.Box(r, GUIContent.none, btn);
            bool ok = v > 0.5f;
            float frac = ok ? Mathf.Clamp01((v - batteryEmptyV) / Mathf.Max(0.1f, batteryFullV - batteryEmptyV)) : 0f;
            Color c = !ok ? new Color(0.55f, 0.6f, 0.66f) : frac > 0.5f ? Green : frac > 0.2f ? new Color(1f, 0.78f, 0.2f) : Red;

            // icon pin nho ben trai
            Rect body = new Rect(r.x + 9, r.y + 8, 22, 11);
            Fill(new Rect(body.x - 1, body.y - 1, body.width + 2, body.height + 2), new Color(0.75f, 0.8f, 0.86f, 0.9f));
            Fill(body, new Color(0.08f, 0.1f, 0.13f));
            Fill(new Rect(body.xMax + 1, body.y + 3, 2, 5), new Color(0.75f, 0.8f, 0.86f, 0.9f));
            if (ok) Fill(new Rect(body.x + 1, body.y + 1, (body.width - 2) * frac, body.height - 2), c);

            battStyle.normal.textColor = c;
            string txt = ok ? string.Format("{0:0.00} V / {1:0.0} V", v, batteryFullV) : string.Format("-- V / {0:0.0} V", batteryFullV);
            GUI.Label(new Rect(r.x + 34, r.y, r.width - 38, r.height), txt, battStyle);
        }

        static void Fill(Rect r, Color c)
        {
            var old = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = old;
        }

        bool ToggleButton(Rect r, string text, bool active, bool enabled = true)
        {
            var oldBg = GUI.backgroundColor;
            bool oldEn = GUI.enabled;
            GUI.enabled = enabled;
            bool clicked = GUI.Button(r, text, active ? btnOn : btn);
            GUI.backgroundColor = oldBg;
            GUI.enabled = oldEn;
            return clicked;
        }

        // ------------------------------------------------------------------ GUI

        void OnGUI()
        {
            InitStyles();
            var m = receiver != null ? receiver.Latest : null;
            bool alive = receiver != null && receiver.SecondsSinceLast < 1f;
            bool py = launcher != null && launcher.Running;

            float x0 = Screen.width * twinWidth, pw = Screen.width - x0, pad = 8f, gap = 6f;
            float fw = pw - 2 * pad;
            float lx = x0 + pad;

            // ---------- trai ----------
            GUI.Label(new Rect(8, 4, 400, 20), "DIGITAL TWIN", title);
            if (m != null && !string.IsNullOrEmpty(m.status))
                GUI.Label(new Rect(8, 22, Mathf.Max(100f, x0 - 220f), 20), m.status, statusStyle);

            // goc tren phai map: [Restart Brain] [Clear] [2D/3D View] [PIN]
            battRect = new Rect(x0 - 8 - 150, 6, 150, 26);
            DrawBattery(battRect, SmoothedBattery(m != null && m.esp_connected ? m.batt : -1f));
            viewBtnRect = new Rect(battRect.x - 6 - 108, 6, 108, 26);
            clearBtnRect = new Rect(viewBtnRect.x - 6 - 66, 6, 66, 26);
            restartBtnRect = new Rect(clearBtnRect.x - 6 - 120, 6, 120, 26);
            if (GUI.Button(restartBtnRect, "Restart (F5)", btn) && launcher != null) launcher.Restart();
            if (GUI.Button(clearBtnRect, "Clear", btn) && control != null) control.ClearVirtualBalls();
            if (GUI.Button(viewBtnRect, "2D/3D View", btn))
            {
                topView = !topView;
                ApplyView();
            }
            if (placing)
                GUI.Label(new Rect(8, Screen.height - 46, x0 - 16, 20), "Click on the map to place a virtual ball   (Esc / right-click: cancel)", hintStyle);
            if (!topView)
                GUI.Label(new Rect(8, Screen.height - 22, x0 - 16, 20), "Left drag: rotate   Right drag: pan   Scroll: zoom", label);
            DrawAxesGizmo(new Vector2(110f, Screen.height - 100f), 52f);

            // ---------- phai: nen + header ----------
            Fill(new Rect(x0, 0, pw, Screen.height), PanelBg);
            GUI.Label(new Rect(lx, 4, fw * 0.4f, 20), "CONTROL PANEL", title);
            bool esp = m != null && m.esp_connected;
            GUI.Box(new Rect(x0 + pw - pad - 260, 4, 112, 18), py && alive ? "BRAIN ONLINE" : "BRAIN OFFLINE", py && alive ? badgeOk : badgeWarn);
            GUI.Box(new Rect(x0 + pw - pad - 142, 4, 142, 18), esp ? "ESP32 CONNECTED" : "ESP32 OFFLINE", esp ? badgeOk : badgeWarn);

            // ---------- hang nut chinh: MANUAL | AUTO | PAUSE | ADD BALL | CAMERA ----------
            float y = 28, bh = 30, bw4 = (fw - 4 * gap) / 5f;
            if (control != null)
            {
                if (ToggleButton(new Rect(lx, y, bw4, bh), "MANUAL", control.IsManual)) control.ToggleManual();
                if (ToggleButton(new Rect(lx + (bw4 + gap), y, bw4, bh), "AUTO", control.IsAuto)) control.ToggleAuto();
                if (ToggleButton(new Rect(lx + 2 * (bw4 + gap), y, bw4, bh), control.IsPaused ? "RESUME" : "PAUSE",
                                 control.IsPaused, control.IsAuto)) control.TogglePause();
            }
            var oldEn = GUI.enabled;
            if (GUI.Button(new Rect(lx + 3 * (bw4 + gap), y, bw4, bh), "ADD BALL", placing ? btnYellow : btn))
                placing = !placing;                                // bam: lay bong ao, chuot vao map de dat
            GUI.enabled = oldEn;
            camBtnRect = new Rect(lx + 4 * (bw4 + gap), y, bw4, bh);
            DrawCameraButton(camBtnRect, m);

            // ---------- anh camera ----------
            y += bh + gap;
            const float tileH = 50f, ctrlMin = 200f;
            var tex = feed != null ? feed.Texture : null;
            float aspect = tex != null && tex.width > 2 ? (float)tex.height / tex.width : 9f / 16f;
            float fh = Mathf.Max(80f, Mathf.Min(fw * aspect, Screen.height - y - ctrlMin - tileH - 3 * gap));
            var fr = new Rect(lx, y, fw, fh);
            GUI.Box(fr, GUIContent.none, card);
            if (tex != null && feed.SecondsSinceLast < 2f) GUI.DrawTexture(new Rect(fr.x + 2, fr.y + 2, fr.width - 4, fr.height - 4), tex, ScaleMode.ScaleToFit);
            else GUI.Label(fr, "No camera image", center);
            GUI.Label(new Rect(fr.x + 8, fr.y + 4, 200, 18), "CAMERA FEED", title);

            // ---------- vung lai tay | attitude ----------
            y = fr.yMax + gap;
            float ctrlH = Mathf.Max(130f, Screen.height - y - tileH - 2 * gap);
            float half = (fw - gap) / 2f;
            var left = new Rect(lx, y, half, ctrlH);
            var right = new Rect(lx + half + gap, y, half, ctrlH);
            GUI.Box(left, GUIContent.none, card);
            GUI.Box(right, GUIContent.none, card);
            DrawTeleop(left);
            DrawAttitude(right, m);

            // ---------- 4 thong so ----------
            DrawTiles(new Rect(lx, left.yMax + gap, fw, tileH), m);

            if (camMenuOpen) DrawCameraMenu(m);                   // ve sau cung de nam tren cac phan khac
        }

        // ------------------------------------------------------------------ chon camera

        /// <summary>Thu tu camera (camera1, camera2...) cua index dang dung, 0 neu chua biet.</summary>
        static int CameraNumber(TwinMessage m)
        {
            if (m == null || m.cameras == null) return 0;
            int k = System.Array.IndexOf(m.cameras, m.cam_index);
            return k >= 0 ? k + 1 : 0;
        }

        void DrawCameraButton(Rect r, TwinMessage m)
        {
            int n = CameraNumber(m);
            string txt = n > 0 ? "CAMERA " + n : "CAMERA";
            if (GUI.Button(new Rect(r.x, r.y, r.width, r.height), txt + "      ", camMenuOpen ? btnOn : btn))
            {
                camMenuOpen = !camMenuOpen;
                if (camMenuOpen && control != null) control.ScanCameras();   // mo menu: do tim lai camera
            }
            // tam giac: huong xuong khi dong, huong len khi mo
            float s = Mathf.Min(12f, r.height * 0.4f);
            var c = new Vector2(r.xMax - 14f, r.center.y);
            var mat = GUI.matrix;
            GUIUtility.RotateAroundPivot(camMenuOpen ? 0f : 180f, c);
            var old = GUI.color;
            GUI.color = camMenuOpen ? new Color(0.02f, 0.16f, 0.07f) : new Color(0.85f, 0.9f, 0.95f);
            GUI.DrawTexture(new Rect(c.x - s / 2f, c.y - s / 2f, s, s), triTex);
            GUI.color = old;
            GUI.matrix = mat;
        }

        void DrawCameraMenu(TwinMessage m)
        {
            if (menuItem == null)
            {
                menuItem = new GUIStyle(btn) { alignment = TextAnchor.MiddleLeft, padding = new RectOffset(10, 6, 0, 0) };
            }
            int[] cams = m != null && m.cameras != null ? m.cameras : new int[0];
            bool scanning = m != null && m.cam_scanning;
            const float ih = 26f, gapI = 3f;
            int rows = Mathf.Max(1, cams.Length) + (scanning ? 1 : 0);
            float w = Mathf.Max(camBtnRect.width, 130f);
            var box = new Rect(camBtnRect.xMax - w, camBtnRect.yMax + 4f, w, rows * (ih + gapI) + gapI + 4f);

            // bam ra ngoai menu: dong menu
            var e = Event.current;
            if (e.rawType == EventType.MouseDown && !box.Contains(e.mousePosition) && !camBtnRect.Contains(e.mousePosition))
            {
                camMenuOpen = false;
                return;
            }

            GUI.Box(box, GUIContent.none, card);
            float yy = box.y + gapI + 2f;
            if (cams.Length == 0)
            {
                GUI.Label(new Rect(box.x + 10f, yy, w - 20f, ih), scanning ? "" : "No camera found", label);
                if (!scanning) yy += ih + gapI;
            }
            for (int k = 0; k < cams.Length; k++)
            {
                bool cur = m != null && cams[k] == m.cam_index;
                if (GUI.Button(new Rect(box.x + 4f, yy, w - 8f, ih), "camera" + (k + 1), cur ? btnOn : menuItem))
                {
                    if (!cur && control != null) control.SelectCamera(cams[k]);
                    camMenuOpen = false;
                }
                yy += ih + gapI;
            }
            if (scanning) GUI.Label(new Rect(box.x + 10f, yy, w - 20f, ih), "Scanning...", label);
        }

        void DrawTeleop(Rect r)
        {
            if (control == null) return;
            bool manual = control.IsManual;
            bool oldEn = GUI.enabled;
            var oldCol = GUI.color;
            GUI.enabled = manual;                                   // khong MANUAL: mo, khong bam duoc
            GUI.color = manual ? Color.white : new Color(1f, 1f, 1f, 0.35f);

            // thanh toc do o tren
            control.pwm = SpeedSlider(new Rect(r.x + 12, r.y + 8, r.width - 24, 50), control.pwm, 70, 255, manual);

            // 4 nut mui ten: giu de chay, tha ra dung
            float top = r.y + 62, avail = r.height - 70;
            float cell = Mathf.Min((r.width - 28) / 3f, avail / 3f);
            float gx = r.x + (r.width - 3 * cell) / 2f, gy = top + (avail - 3 * cell) / 2f;
            float g = Mathf.Max(3f, cell * 0.07f), sz = cell - 2 * g;
            bool up = ArrowButton(new Rect(gx + cell + g, gy + g, sz, sz), 0f);
            bool lf = ArrowButton(new Rect(gx + g, gy + cell + g, sz, sz), -90f);
            bool rt = ArrowButton(new Rect(gx + 2 * cell + g, gy + cell + g, sz, sz), 90f);
            bool dn = ArrowButton(new Rect(gx + cell + g, gy + 2 * cell + g, sz, sz), 180f);

            GUI.enabled = oldEn;
            GUI.color = oldCol;
            if (Event.current.type == EventType.Repaint)
            {
                control.uiUp = manual && up;
                control.uiDown = manual && dn;
                control.uiLeft = manual && lf;
                control.uiRight = manual && rt;
            }
        }

        static void RoundRect(Rect r, Color c, float radius)
        {
            GUI.DrawTexture(r, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, c, 0f, radius);
        }

        /// <summary>
        /// Thanh toc do tu ve: nhan SPEED + o gia tri, rail bo tron co phan da chon mau cyan,
        /// nut keo tron, vach 0/25/50/75/100 %. Keo hoac bam vao rail de chinh, cuon chuot de tang/giam.
        /// </summary>
        int SpeedSlider(Rect r, int value, int min, int max, bool enabled)
        {
            float a = enabled ? 1f : 0.6f;            // GUI.color cua vung da lam mo san
            Color A(Color c) { c.a *= a; return c; }

            // hang tren: SPEED ............ [ 193 PWM | 67% ]
            GUI.Label(new Rect(r.x, r.y, 80, 20), "SPEED", speedTitle);
            float pct = (value - min) / (float)(max - min);
            var pill = new Rect(r.xMax - 150, r.y, 150, 22);
            RoundRect(pill, A(new Color(0.32f, 0.26f, 0.02f)), 11f);
            GUI.Label(pill, $"{value} PWM  ·  {Mathf.RoundToInt(pct * 100f),3}%", speedValue);

            // rail
            var rail = new Rect(r.x + 9, r.y + 32, r.width - 18, 8);
            int id = GUIUtility.GetControlID(FocusType.Passive, r);
            var e = Event.current;
            var hit = new Rect(r.x, rail.y - 10, r.width, 28);
            int FromX(float x) => Mathf.RoundToInt(Mathf.Lerp(min, max, Mathf.Clamp01((x - rail.x) / rail.width)));
            if (enabled)
            {
                switch (e.GetTypeForControl(id))
                {
                    case EventType.MouseDown:
                        if (e.button == 0 && hit.Contains(e.mousePosition))
                        {
                            GUIUtility.hotControl = id;
                            value = FromX(e.mousePosition.x);
                            e.Use();
                        }
                        break;
                    case EventType.MouseDrag:
                        if (GUIUtility.hotControl == id)
                        {
                            value = FromX(e.mousePosition.x);
                            e.Use();
                        }
                        break;
                    case EventType.MouseUp:
                        if (GUIUtility.hotControl == id)
                        {
                            GUIUtility.hotControl = 0;
                            e.Use();
                        }
                        break;
                    case EventType.ScrollWheel:
                        if (hit.Contains(e.mousePosition))
                        {
                            value = Mathf.Clamp(value - Mathf.RoundToInt(Mathf.Sign(e.delta.y)) * 5, min, max);
                            e.Use();
                        }
                        break;
                }
            }

            if (e.type == EventType.Repaint)
            {
                pct = (value - min) / (float)(max - min);
                RoundRect(rail, A(TrackBg), 4f);
                var fill = new Rect(rail.x, rail.y, Mathf.Max(8f, rail.width * pct), rail.height);
                RoundRect(fill, A(Yellow), 4f);
                for (int i = 0; i <= 4; i++)                               // vach 0 / 25 / 50 / 75 / 100 %
                {
                    float tx = rail.x + rail.width * i / 4f;
                    Fill(new Rect(tx - 0.5f, rail.yMax + 4, 1f, 4f), A(new Color(0.4f, 0.46f, 0.52f)));
                }
                float kx = rail.x + rail.width * pct;
                bool drag = GUIUtility.hotControl == id;
                float ks = drag ? 22f : 18f;
                var knob = new Rect(kx - ks / 2f, rail.center.y - ks / 2f, ks, ks);
                RoundRect(new Rect(knob.x - 3, knob.y - 3, knob.width + 6, knob.height + 6), A(new Color(1f, 0.85f, 0.15f, 0.35f)), (ks + 6) / 2f);
                RoundRect(knob, A(Color.white), ks / 2f);
                RoundRect(new Rect(knob.center.x - 4, knob.center.y - 4, 8, 8), A(Yellow), 4f);
            }
            GUI.Label(new Rect(rail.x - 6, rail.yMax + 7, 40, 14), min.ToString(), speedScale);
            var right = new GUIStyle(speedScale) { alignment = TextAnchor.UpperRight };
            GUI.Label(new Rect(rail.xMax - 34, rail.yMax + 7, 40, 14), max.ToString(), right);
            return value;
        }

        /// <summary>Nut bo goc nen cyan + tam giac trang xoay theo huong (do, 0 = len).</summary>
        bool ArrowButton(Rect r, float angle)
        {
            bool held = GUI.RepeatButton(r, GUIContent.none, arrow);
            float s = r.width * 0.42f;
            var c = r.center;
            var mat = GUI.matrix;
            GUIUtility.RotateAroundPivot(angle, c);
            GUI.DrawTexture(new Rect(c.x - s / 2f, c.y - s / 2f, s, s), triTex);
            GUI.matrix = mat;
            return held;
        }

        void DrawAttitude(Rect r, TwinMessage m)
        {
            float roll = m != null && m.robot != null ? m.robot.roll : 0f;
            float pitch = m != null && m.robot != null ? m.robot.pitch : 0f;
            float side = Mathf.Floor(Mathf.Min(r.width, r.height) - 16f);
            var box = new Rect(Mathf.Round(r.x + (r.width - side) / 2f), Mathf.Round(r.y + (r.height - side) / 2f), side, side);
            float R = side / 2f, Ri = R * 0.88f;
            float pxPerDeg = Ri / 35f;                              // ~ +-35 do hien trong mat dong ho

            GUI.BeginGroup(box);
            var c = new Vector2(R, R);
            var mat = GUI.matrix;

            // --- troi / dat: ve vao texture tron (khong ve hinh xoay lon tran ra ngoai dong ho) ---
            float shift = Mathf.Clamp(pitch * pxPerDeg, -Ri, Ri);
            UpdateHorizon(roll, shift / R);
            GUI.DrawTexture(new Rect(0, 0, side, side), horizonTex);

            // --- vach pitch (xoay theo roll), chi ve vach nam trong mat dong ho ---
            GUIUtility.RotateAroundPivot(-roll, c);
            for (int p = -30; p <= 30; p += 5)
            {
                if (p == 0) continue;
                float yy = c.y + shift - p * pxPerDeg;
                if (Mathf.Abs(yy - c.y) > Ri * 0.8f) continue;
                bool major = p % 10 == 0;
                float w = major ? side * 0.26f : side * 0.12f;
                Fill(new Rect(c.x - w / 2f, yy - 0.75f, w, 1.5f), new Color(1f, 1f, 1f, 0.85f));
                if (major)
                {
                    string t = Mathf.Abs(p).ToString();
                    GUI.Label(new Rect(c.x - w / 2f - 20f, yy - 7f, 18f, 14f), t, ladderLabel);
                    GUI.Label(new Rect(c.x + w / 2f + 2f, yy - 7f, 18f, 14f), t, ladderLabel);
                }
            }
            GUI.matrix = mat;

            // --- vien dong ho (che phan vuong ben ngoai) ---
            GUI.DrawTexture(new Rect(0, 0, side, side), bezelTex);

            // --- thang roll co dinh phia tren ---
            foreach (int a in new[] { -60, -45, -30, -20, -10, 0, 10, 20, 30, 45, 60 })
            {
                bool big = a % 30 == 0;
                float len = big ? side * 0.07f : side * 0.04f;
                GUIUtility.RotateAroundPivot(a, c);
                Fill(new Rect(c.x - (big ? 1f : 0.75f), c.y - Ri, big ? 2f : 1.5f, len), Color.white);
                GUI.matrix = mat;
            }

            // --- kim roll (xoay theo roll) ---
            GUIUtility.RotateAroundPivot(-roll, c);
            float ts = side * 0.075f;
            GUI.color = Yellow;
            GUI.DrawTexture(new Rect(c.x - ts / 2f, c.y - Ri + side * 0.075f, ts, ts), triTex);
            GUI.color = Color.white;
            GUI.matrix = mat;

            // --- ky hieu may bay co dinh o giua ---
            float wing = side * 0.2f, th = Mathf.Max(3f, side * 0.022f);
            var shadow = new Color(0f, 0f, 0f, 0.45f);
            Fill(new Rect(c.x - wing - side * 0.06f + 1, c.y - th / 2f + 1, wing, th), shadow);
            Fill(new Rect(c.x + side * 0.06f + 1, c.y - th / 2f + 1, wing, th), shadow);
            Fill(new Rect(c.x - wing - side * 0.06f, c.y - th / 2f, wing, th), Yellow);
            Fill(new Rect(c.x + side * 0.06f, c.y - th / 2f, wing, th), Yellow);
            Fill(new Rect(c.x - side * 0.06f - th / 2f, c.y - th / 2f, th, side * 0.05f), Yellow);
            Fill(new Rect(c.x + side * 0.06f - th / 2f, c.y - th / 2f, th, side * 0.05f), Yellow);
            Fill(new Rect(c.x - th, c.y - th, 2f * th, 2f * th), Yellow);
            GUI.EndGroup();
        }

        /// <summary>
        /// Ve troi/dat vao texture tron HorizonN x HorizonN (chi ve lai khi roll/pitch doi).
        /// shiftN: do dich chan troi theo ban kinh (duong = chan troi xuong duoi).
        /// </summary>
        void UpdateHorizon(float rollDeg, float shiftN)
        {
            if (horizonTex == null)
            {
                horizonTex = NewTex(HorizonN, HorizonN);
                horizonPx = new Color[HorizonN * HorizonN];
            }
            if (Mathf.Abs(rollDeg - lastRoll) < 0.05f && Mathf.Abs(shiftN - lastPitch) < 0.001f) return;
            lastRoll = rollDeg;
            lastPitch = shiftN;

            float a = rollDeg * Mathf.Deg2Rad, ca = Mathf.Cos(a), sa = Mathf.Sin(a);
            float half = HorizonN / 2f, line = 1.6f / half;
            Color skyHi = new Color(0.35f, 0.62f, 0.9f), skyLo = new Color(0.06f, 0.25f, 0.55f);
            Color gndHi = new Color(0.52f, 0.34f, 0.2f), gndLo = new Color(0.24f, 0.15f, 0.09f);
            for (int y = 0; y < HorizonN; y++)
            {
                float v = (y + 0.5f - half) / half;                       // len tren = duong
                for (int x = 0; x < HorizonN; x++)
                {
                    float u = (x + 0.5f - half) / half;
                    float r2 = u * u + v * v;
                    if (r2 > 1.02f) { horizonPx[y * HorizonN + x] = Color.clear; continue; }
                    float vr = -u * sa + v * ca;                         // toa do theo phuong vuong goc chan troi
                    float dh = vr + shiftN;                              // > 0: troi
                    Color col = dh > 0f ? Color.Lerp(skyHi, skyLo, Mathf.Clamp01(dh / 1.6f))
                                        : Color.Lerp(gndHi, gndLo, Mathf.Clamp01(-dh / 1.6f));
                    if (Mathf.Abs(dh) < line) col = Color.Lerp(Color.white, col, Mathf.Abs(dh) / line);
                    horizonPx[y * HorizonN + x] = col;
                }
            }
            horizonTex.SetPixels(horizonPx);
            horizonTex.Apply();
        }

        /// <summary>
        /// He truc o goc duoi trai khung mo hinh. 2D: x, y. 3D: x, y, z xoay theo goc nhin.
        /// Huong truc tinh tu camera that nen luon khop voi san.
        /// </summary>
        void DrawAxesGizmo(Vector2 o, float len)
        {
            if (cam == null || Event.current.type != EventType.Repaint) return;
            var axes = new[]
            {
                (name: "x", dir: RosUnity.ToUnity(1f, 0f, 0f), col: new Color(1f, 0.3f, 0.3f)),
                (name: "y", dir: RosUnity.ToUnity(0f, 1f, 0f), col: new Color(0.3f, 0.95f, 0.4f)),
                (name: "z", dir: RosUnity.ToUnity(0f, 0f, 1f), col: new Color(0.35f, 0.6f, 1f)),
            };
            int n = topView ? 2 : 3;
            var items = new List<(string name, Vector2 d, float depth, Color col)>();
            for (int i = 0; i < n; i++)
            {
                Vector3 v = cam.transform.InverseTransformDirection(axes[i].dir);
                items.Add((axes[i].name, new Vector2(v.x, -v.y), v.z, axes[i].col));
            }
            items.Sort((p, q) => q.depth.CompareTo(p.depth));          // truc o xa ve truoc

            var mat = GUI.matrix;
            var lbl = new GUIStyle(label) { fontStyle = FontStyle.Bold, fontSize = 15, alignment = TextAnchor.MiddleCenter };
            foreach (var it in items)
            {
                float l = it.d.magnitude * len;
                if (l < 4f) continue;                                    // truc huong thang vao man hinh
                Vector2 dir = it.d.normalized;
                float ang = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
                float head = 16f;
                Vector2 tip = o + dir * l;

                GUIUtility.RotateAroundPivot(ang, o);                   // than mui ten
                Fill(new Rect(o.x, o.y - 2f, Mathf.Max(0f, l - head * 0.7f), 4f), it.col);
                GUI.matrix = mat;
                GUIUtility.RotateAroundPivot(ang + 90f, tip);           // dau mui ten
                GUI.color = it.col;
                GUI.DrawTexture(new Rect(tip.x - head / 2f, tip.y - head * 0.2f, head, head), triTex);
                GUI.color = Color.white;
                GUI.matrix = mat;

                lbl.normal.textColor = it.col;
                Vector2 lp = tip + dir * 13f;
                GUI.Label(new Rect(lp.x - 10f, lp.y - 10f, 20f, 20f), it.name, lbl);
            }
            RoundRect(new Rect(o.x - 5f, o.y - 5f, 10f, 10f), Color.white, 5f);   // goc toa do
        }

        void DrawTiles(Rect r, TwinMessage m)
        {
            var s = m != null ? m.robot : null;
            float R = robot != null ? robot.wheelRadius : 0.0325f;
            float v = 0f;
            if (s != null && (Mathf.Abs(s.wl) > 1e-3f || Mathf.Abs(s.wr) > 1e-3f)) v = R * (s.wl + s.wr) * 0.5f;
            else if (robot != null) v = robot.LinearVel;

            string[] names = { "ROLL", "PITCH", "YAW", "V" };
            string[] vals =
            {
                s != null ? $"{s.roll:0.0}°" : "--",
                s != null ? $"{s.pitch:0.0}°" : "--",
                s != null ? $"{s.yaw:0.0}°" : "--",
                $"{v:0.00} m/s",
            };
            float w = (r.width - 3 * 6f) / 4f;
            for (int i = 0; i < 4; i++)
            {
                var t = new Rect(r.x + i * (w + 6f), r.y, w, r.height);
                GUI.Box(t, GUIContent.none, card);
                GUI.Label(new Rect(t.x, t.y + 4, t.width, 16), names[i], tileLabel);
                GUI.Label(new Rect(t.x, t.y + 16, t.width, t.height - 18), vals[i], tileValue);
            }
        }
    }
}
