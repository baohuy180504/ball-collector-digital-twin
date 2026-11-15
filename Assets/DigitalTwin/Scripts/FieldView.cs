using UnityEngine;

namespace DigitalTwin
{
    /// <summary>
    /// Ve san (vung camera nhin thay): mat san, luoi, vien. He truc toa do ve o goc man hinh (TwinHud).
    /// GameObject nay phai dat tai (0,0,0), khong xoay.
    /// </summary>
    public class FieldView : MonoBehaviour
    {
        [Tooltip("Chieu dai san theo truc X (m)")]
        public float lengthX = 3.0f;
        [Tooltip("Chieu rong san theo truc Y (m)")]
        public float widthY = 2.0f;
        public float gridStep = 0.25f;
        public Color floorColor = new Color(0.18f, 0.36f, 0.22f);
        [Tooltip("Keo dai mat san + luoi ra moi phia (m) de khi xoay 2D luoi van phu kin man hinh")]
        public float extendMargin = 6.0f;
        [Tooltip("Ve vien trang quanh vung san lam viec (3 x 2 m)")]
        public bool drawFieldBorder = false;
        public Color gridColor = new Color(0.45f, 0.62f, 0.48f);
        public Color borderColor = new Color(0.95f, 0.95f, 0.95f);

        const string GenName = "_FieldGenerated";

        public Vector3 CenterUnity => RosUnity.ToUnity(lengthX * 0.5f, widthY * 0.5f, 0f);

        void Start()
        {
            Build();      // luon dung lai khi Play (cap nhat ban ve cu trong scene)
        }

        [ContextMenu("Rebuild field")]
        public void Build()
        {
            var old = transform.Find(GenName);
            if (old != null) TwinUtil.DestroyAny(old.gameObject);

            var root = new GameObject(GenName).transform;
            root.SetParent(transform, false);

            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.name = "Floor";
            TwinUtil.RemoveCollider(floor);
            floor.transform.SetParent(root, false);
            float m = Mathf.Max(0f, extendMargin);
            floor.transform.localPosition = RosUnity.ToUnity(lengthX * 0.5f, widthY * 0.5f, -0.005f);
            floor.transform.localScale = RosUnity.ScaleToUnity(new Vector3(lengthX + 2f * m, widthY + 2f * m, 0.01f));
            floor.GetComponent<Renderer>().sharedMaterial = TwinMaterials.Lit(floorColor);

            var gridMat = TwinMaterials.Unlit(gridColor);
            var borderMat = TwinMaterials.Unlit(borderColor);
            // luoi phu ca vung keo dai (canh theo goc san 0,0)
            float x0 = -m, x1 = lengthX + m, y0 = -m, y1 = widthY + m;
            int i0 = Mathf.CeilToInt(x0 / gridStep - 1e-3f), i1 = Mathf.FloorToInt(x1 / gridStep + 1e-3f);
            int j0 = Mathf.CeilToInt(y0 / gridStep - 1e-3f), j1 = Mathf.FloorToInt(y1 / gridStep + 1e-3f);
            for (int i = i0; i <= i1; i++)
                Line(root, gridMat, new Vector2(i * gridStep, y0), new Vector2(i * gridStep, y1), 0.003f);
            for (int j = j0; j <= j1; j++)
                Line(root, gridMat, new Vector2(x0, j * gridStep), new Vector2(x1, j * gridStep), 0.003f);

            // vien trang = vung lam viec (camera nhin thay), ve cao hon luoi
            if (!drawFieldBorder) return;
            Line(root, borderMat, new Vector2(0f, 0f), new Vector2(lengthX, 0f), 0.01f, 0.002f);
            Line(root, borderMat, new Vector2(lengthX, 0f), new Vector2(lengthX, widthY), 0.01f, 0.002f);
            Line(root, borderMat, new Vector2(lengthX, widthY), new Vector2(0f, widthY), 0.01f, 0.002f);
            Line(root, borderMat, new Vector2(0f, widthY), new Vector2(0f, 0f), 0.01f, 0.002f);
        }

        static void Line(Transform parent, Material m, Vector2 a, Vector2 b, float w, float z = 0.001f)
        {
            var go = new GameObject("line");
            go.transform.SetParent(parent, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = false;
            lr.positionCount = 2;
            lr.SetPosition(0, RosUnity.ToUnity(a.x, a.y, z));
            lr.SetPosition(1, RosUnity.ToUnity(b.x, b.y, z));
            lr.widthMultiplier = w;
            lr.sharedMaterial = m;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
        }
    }
}
