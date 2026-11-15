using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

namespace DigitalTwin
{
    /// <summary>
    /// Nhan goi JSON qua UDP tren thread rieng, chi giu goi moi nhat.
    /// Cac script khac doc Latest tren main thread va so sanh Sequence de biet co goi moi.
    /// </summary>
    [DisallowMultipleComponent]
    public class TwinUdpReceiver : MonoBehaviour
    {
        public int port = 5105;

        public TwinMessage Latest { get; private set; }
        public int Sequence { get; private set; }
        public float PacketRate { get; private set; }
        public string LastError { get; private set; }
        public float SecondsSinceLast => Time.unscaledTime - lastPacketTime;

        UdpClient client;
        Thread thread;
        volatile bool running;
        readonly object lockObj = new object();
        string pendingJson;
        int pendingCount;
        float lastPacketTime = -999f;
        int rateCount;
        float rateTimer;

        void OnEnable()
        {
            if (port == 5005) port = 5105;   // scene cu: 5005 nay la cong telemetry cua ESP32
            Application.runInBackground = true;
            try
            {
                client = new UdpClient(new IPEndPoint(IPAddress.Any, port));
                client.Client.ReceiveBufferSize = 1 << 20;
                running = true;
                thread = new Thread(ReceiveLoop) { IsBackground = true, Name = "TwinUdpReceiver" };
                thread.Start();
                Debug.Log($"[DigitalTwin] Dang nghe UDP cong {port}");
            }
            catch (Exception e)
            {
                LastError = e.Message;
                Debug.LogError($"[DigitalTwin] Khong mo duoc UDP cong {port}: {e.Message}");
            }
        }

        void OnDisable()
        {
            running = false;
            try { client?.Close(); } catch { }
            client = null;
            if (thread != null && thread.IsAlive) thread.Join(200);
            thread = null;
        }

        void ReceiveLoop()
        {
            var ep = new IPEndPoint(IPAddress.Any, 0);
            while (running)
            {
                try
                {
                    byte[] data = client.Receive(ref ep);
                    string s = Encoding.UTF8.GetString(data);
                    lock (lockObj)
                    {
                        pendingJson = s;
                        pendingCount++;
                    }
                }
                catch (ObjectDisposedException) { break; }
                catch (SocketException) { if (!running) break; }
                catch (Exception e) { LastError = e.Message; }
            }
        }

        void Update()
        {
            string json;
            int n;
            lock (lockObj)
            {
                json = pendingJson;
                n = pendingCount;
                pendingJson = null;
                pendingCount = 0;
            }

            if (json != null)
            {
                try
                {
                    var msg = JsonUtility.FromJson<TwinMessage>(json);
                    if (msg != null)
                    {
                        Latest = msg;
                        Sequence++;
                        lastPacketTime = Time.unscaledTime;
                    }
                }
                catch (Exception e)
                {
                    LastError = "JSON: " + e.Message;
                }
            }

            rateCount += n;
            rateTimer += Time.unscaledDeltaTime;
            if (rateTimer >= 1f)
            {
                PacketRate = rateCount / rateTimer;
                rateCount = 0;
                rateTimer = 0f;
            }
        }
    }
}
