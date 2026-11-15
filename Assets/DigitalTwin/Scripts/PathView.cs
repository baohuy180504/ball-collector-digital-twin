using UnityEngine;

namespace DigitalTwin
{
    /// <summary>Ve path tu goi UDP bang LineRenderer, sat mat san.</summary>
    public class PathView : MonoBehaviour
    {
        public TwinUdpReceiver receiver;
        public float width = 0.012f;
        public Color color = new Color(1f, 0.85f, 0.1f);
        public float height = 0.004f;

        LineRenderer lr;
        int lastSeq = -1;

        void Awake()
        {
            if (receiver == null) receiver = FindFirstObjectByType<TwinUdpReceiver>();
            lr = GetComponent<LineRenderer>();
            if (lr == null) lr = gameObject.AddComponent<LineRenderer>();
            lr.useWorldSpace = true;
            lr.widthMultiplier = width;
            lr.numCornerVertices = 2;
            lr.numCapVertices = 2;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lr.sharedMaterial = TwinMaterials.Unlit(color);
            lr.positionCount = 0;
        }

        void Update()
        {
            var m = receiver != null ? receiver.Latest : null;
            if (m == null || receiver.Sequence == lastSeq) return;
            lastSeq = receiver.Sequence;

            var p = m.path;
            int n = p != null ? p.Length : 0;
            lr.positionCount = n;
            for (int i = 0; i < n; i++)
                lr.SetPosition(i, RosUnity.ToUnity(p[i].x * 0.001f, p[i].y * 0.001f, height));
        }
    }
}
