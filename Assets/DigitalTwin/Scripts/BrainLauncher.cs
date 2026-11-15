using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Text;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace DigitalTwin
{
    /// <summary>
    /// Bam Play -> tu chay "python Brain/main.py", in log cua Python vao Console Unity.
    /// Dung Play -> tat Python (ESP32 tu dung xe nho watchdog).
    /// </summary>
    [DisallowMultipleComponent]
    public class BrainLauncher : MonoBehaviour
    {
        public bool autoStart = true;
        [Tooltip("Uu tien Python trong Brain/.venv (tao bang Brain/setup_venv.bat). Tat de dung Python Exe ben duoi.")]
        public bool useVenv = true;
        [Tooltip("Chi dung khi khong co .venv hoac tat Use Venv: python, py, hoac duong dan day du toi python.exe")]
        public string pythonExe = "python";

        public string PythonUsed { get; private set; } = "";
        [Tooltip("Duong dan tinh tu thu muc project (ngang hang Assets)")]
        public string script = "Brain/main.py";
        [Tooltip("Vi du: --source sim   |   --source camera   |   --source camera --esp 192.168.1.50")]
        public string arguments = "";
        [Tooltip("Cong nhan lenh cua Python (de bao Python dung xe va thoat)")]
        public int cmdPort = 5107;

        public bool Running => proc != null && !proc.HasExited;
        public string LastLine { get; private set; } = "";
        /// <summary>Che do dang chon tren giao dien: "odom" | "camera" | "sim" ("" = theo config.py). Luu lai cho lan Play sau.</summary>
        public string SourceOverride { get; private set; } = "";
        const string PrefKey = "DigitalTwin.Source";

        Process proc;
        readonly ConcurrentQueue<(string text, bool err)> logs = new ConcurrentQueue<(string, bool)>();

        void Start()
        {
            // Giao dien da bo nut chon nguon: luon theo config.py (SOURCE = "camera"); xoa lua chon cu (TEST / SIM)
            SourceOverride = "";
            PlayerPrefs.DeleteKey(PrefKey);
            if (autoStart) Launch();
        }

        /// <summary>Doi che do (TEST / CAMERA / SIM): chay lai Python voi --source moi.</summary>
        public void SwitchSource(string source)
        {
            SourceOverride = source;
            PlayerPrefs.SetString(PrefKey, source);
            PlayerPrefs.Save();
            Restart();
        }

        public void Launch()
        {
            if (Running) return;
            string root = Directory.GetParent(Application.dataPath).FullName;
            string full = Path.GetFullPath(Path.Combine(root, script));
            if (!File.Exists(full) && script.Replace('\\', '/').EndsWith("Brain/brain.py"))
            {
                script = "Brain/main.py";          // scene cu con luu ten file truoc khi doi
                full = Path.GetFullPath(Path.Combine(root, script));
            }
            if (!File.Exists(full))
            {
                Debug.LogError($"[Brain] Khong thay {full}");
                return;
            }
            string dir = Path.GetDirectoryName(full);
            string exe = pythonExe;
            if (useVenv)
            {
                string win = Path.Combine(dir, ".venv", "Scripts", "python.exe");
                string nix = Path.Combine(dir, ".venv", "bin", "python");
                if (File.Exists(win)) exe = win;
                else if (File.Exists(nix)) exe = nix;
                else Debug.LogWarning($"[Brain] Chua co venv trong {dir}. Bam dup Brain/setup_venv.bat de tao. Tam dung '{pythonExe}'.");
            }
            PythonUsed = exe;
            string src = string.IsNullOrEmpty(SourceOverride) ? "" : "--source " + SourceOverride;
            var psi = new ProcessStartInfo(exe, $"-u \"{full}\" {src} {arguments}")
            {
                WorkingDirectory = Path.GetDirectoryName(full),
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            psi.EnvironmentVariables["PYTHONIOENCODING"] = "utf-8";
            proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
            proc.OutputDataReceived += (s, e) => { if (e.Data != null) logs.Enqueue((e.Data, false)); };
            proc.ErrorDataReceived += (s, e) => { if (e.Data != null) logs.Enqueue((e.Data, true)); };
            proc.Exited += (s, e) => logs.Enqueue(("Python da thoat", true));
            try
            {
                proc.Start();
                proc.BeginOutputReadLine();
                proc.BeginErrorReadLine();
                Debug.Log($"[Brain] Da chay: {exe} {script} {src} {arguments}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Brain] Khong chay duoc '{exe}': {ex.Message}. Sua o BrainLauncher > Python Exe.");
                proc = null;
            }
        }

        public void Stop()
        {
            if (proc == null) return;
            try
            {
                if (!proc.HasExited)
                {
                    // Bao Python tu thoat de no gui lenh dung xe (cmd:0,0) truoc khi tat
                    using (var u = new UdpClient())
                    {
                        byte[] b = Encoding.UTF8.GetBytes("{\"action\":\"quit\"}");
                        for (int i = 0; i < 3; i++) u.Send(b, b.Length, "127.0.0.1", cmdPort);
                    }
                    if (!proc.WaitForExit(800)) proc.Kill();
                }
            }
            catch { }
            proc.Dispose();
            proc = null;
        }

        public void Restart()
        {
            Stop();
            Launch();
        }

        void Update()
        {
            while (logs.TryDequeue(out var l))
            {
                LastLine = l.text;
                if (l.err) Debug.LogWarning("[Brain] " + l.text);
                else Debug.Log("[Brain] " + l.text);
            }
        }

        void OnDisable() => Stop();
        void OnApplicationQuit() => Stop();
    }
}
