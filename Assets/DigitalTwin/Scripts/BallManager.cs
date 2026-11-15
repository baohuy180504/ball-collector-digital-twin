using System.Collections.Generic;
using UnityEngine;

namespace DigitalTwin
{
    /// <summary>
    /// Tao / cap nhat / xoa qua bong theo ID trong goi UDP.
    /// Bong khong con trong goi moi nhat thi bi xoa (Python quyet dinh viec nay).
    /// </summary>
    public class BallManager : MonoBehaviour
    {
        public TwinUdpReceiver receiver;
        [Tooltip("Duong kinh bong that (m)")]
        public float ballDiameter = 0.074f;
        public Color ballColor = new Color(1f, 0.55f, 0.1f);
        public Color targetColor = new Color(1f, 0.15f, 0.15f);
        public Color virtualColor = new Color(1f, 0.85f, 0.15f);      // bong ao: vang
        public float sharpness = 20f;
        public bool showIds = true;

        class Ball
        {
            public GameObject go;
            public Vector3 target;
            public int rank;              // thu tu nhat (0 = chua xep)
        }

        readonly Dictionary<int, Ball> balls = new Dictionary<int, Ball>();
        readonly HashSet<int> seen = new HashSet<int>();
        readonly List<int> toRemove = new List<int>();
        Material mat, matTarget, matVirtual;
        int lastSeq = -1;

        public int Count => balls.Count;

        void Awake()
        {
            if (receiver == null) receiver = FindFirstObjectByType<TwinUdpReceiver>();
            mat = TwinMaterials.Lit(ballColor);
            matTarget = TwinMaterials.Lit(targetColor);
            matVirtual = TwinMaterials.Lit(virtualColor);
        }

        void Update()
        {
            var m = receiver != null ? receiver.Latest : null;
            if (m != null && receiver.Sequence != lastSeq)
            {
                lastSeq = receiver.Sequence;
                seen.Clear();
                if (m.ball_d > 1f && !Mathf.Approximately(m.ball_d * 0.001f, ballDiameter))
                {
                    ballDiameter = m.ball_d * 0.001f;                 // lay duong kinh tu config.py
                    foreach (var bb in balls.Values) bb.go.transform.localScale = Vector3.one * ballDiameter;
                }
                if (m.balls != null)
                {
                    foreach (var b in m.balls)
                    {
                        seen.Add(b.id);
                        Vector3 p = RosUnity.ToUnity(b.x * 0.001f, b.y * 0.001f, ballDiameter * 0.5f);
                        if (!balls.TryGetValue(b.id, out var ball))
                        {
                            ball = Create(b.id);
                            ball.go.transform.position = p;
                            balls[b.id] = ball;
                        }
                        ball.target = p;
                        ball.rank = b.rank;
                        ball.go.GetComponent<Renderer>().sharedMaterial = b.id == m.target_id ? matTarget : (b.@virtual ? matVirtual : mat);
                    }
                }
                toRemove.Clear();
                foreach (var id in balls.Keys) if (!seen.Contains(id)) toRemove.Add(id);
                foreach (var id in toRemove)
                {
                    Destroy(balls[id].go);
                    balls.Remove(id);
                }
            }

            float k = 1f - Mathf.Exp(-sharpness * Time.deltaTime);
            foreach (var ball in balls.Values)
                ball.go.transform.position = Vector3.Lerp(ball.go.transform.position, ball.target, k);
        }

        Ball Create(int id)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "ball_" + id;
            TwinUtil.RemoveCollider(go);
            go.transform.SetParent(transform, true);
            go.transform.localScale = Vector3.one * ballDiameter;
            go.GetComponent<Renderer>().sharedMaterial = mat;
            return new Ball { go = go };
        }

        void OnGUI()
        {
            if (!showIds) return;
            var cam = Camera.main;
            if (cam == null) return;
            foreach (var kv in balls)
            {
                Vector3 sp = cam.WorldToScreenPoint(kv.Value.go.transform.position + Vector3.up * ballDiameter);
                if (sp.z <= 0f) continue;
                GUI.Label(new Rect(sp.x - 10f, Screen.height - sp.y - 22f, 40f, 20f), kv.Value.rank > 0 ? kv.Value.rank.ToString() : "-");
            }
        }
    }
}
