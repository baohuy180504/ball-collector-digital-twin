using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;

namespace DigitalTwin
{
    /// <summary>Nhan anh camera (moi goi UDP la 1 anh JPEG) tu bo nao va dua vao Texture2D.</summary>
    [DisallowMultipleComponent]
    public class CameraFeedReceiver : MonoBehaviour
    {
        public int port = 5106;

        public Texture2D Texture { get; private set; }
        public float Fps { get; private set; }
        public float SecondsSinceLast => Time.unscaledTime - lastTime;

        UdpClient client;
        Thread thread;
        volatile bool running;
        readonly object lockObj = new object();
        byte[] pending;
        float lastTime = -999f;
        int count;
        float timer;

        void OnEnable()
        {
            if (port == 5006) port = 5106;   // scene cu
            try
            {
                client = new UdpClient(new IPEndPoint(IPAddress.Any, port));
                client.Client.ReceiveBufferSize = 1 << 21;
                running = true;
                thread = new Thread(Loop) { IsBackground = true, Name = "CameraFeedReceiver" };
                thread.Start();
            }
            catch (Exception e)
            {
                Debug.LogError($"[DigitalTwin] Khong mo duoc UDP cong anh {port}: {e.Message}");
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

        void Loop()
        {
            var ep = new IPEndPoint(IPAddress.Any, 0);
            while (running)
            {
                try
                {
                    byte[] data = client.Receive(ref ep);
                    lock (lockObj) pending = data;
                }
                catch (ObjectDisposedException) { break; }
                catch (SocketException) { if (!running) break; }
            }
        }

        void Update()
        {
            byte[] data;
            lock (lockObj)
            {
                data = pending;
                pending = null;
            }
            if (data != null)
            {
                if (Texture == null) Texture = new Texture2D(2, 2, TextureFormat.RGB24, false);
                if (Texture.LoadImage(data, false))
                {
                    lastTime = Time.unscaledTime;
                    count++;
                }
            }
            timer += Time.unscaledDeltaTime;
            if (timer >= 1f)
            {
                Fps = count / timer;
                count = 0;
                timer = 0f;
            }
        }

        void OnDestroy()
        {
            if (Texture != null) Destroy(Texture);
        }
    }
}
