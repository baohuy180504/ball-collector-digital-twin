using UnityEngine;

namespace DigitalTwin
{
    /// <summary>
    /// Doi he toa do ROS (x tien, y trai, z len, tay phai) sang Unity (x phai, y len, z tien, tay trai).
    /// He toa do SAN cung dung quy uoc ROS: goc tai 1 goc san, X doc chieu dai, Y doc chieu rong,
    /// nhin tu tren xuong thi X -> Y la nguoc chieu kim dong ho, Z huong len. Don vi: met.
    /// </summary>
    public static class RosUnity
    {
        public static Vector3 ToUnity(float x, float y, float z) => new Vector3(-y, z, x);
        public static Vector3 ToUnity(Vector3 ros) => new Vector3(-ros.y, ros.z, ros.x);
        public static Vector3 ToRos(Vector3 u) => new Vector3(u.z, -u.x, u.y);

        /// <summary>Quaternion ROS (x,y,z,w) -> Quaternion Unity.</summary>
        public static Quaternion ToUnity(Quaternion q) => new Quaternion(q.y, -q.z, -q.x, q.w);

        /// <summary>Quaternion ROS tu roll/pitch/yaw (rad), thu tu URDF: R = Rz(yaw) * Ry(pitch) * Rx(roll).</summary>
        public static Quaternion RosRpy(float roll, float pitch, float yaw)
        {
            float cr = Mathf.Cos(roll * 0.5f), sr = Mathf.Sin(roll * 0.5f);
            float cp = Mathf.Cos(pitch * 0.5f), sp = Mathf.Sin(pitch * 0.5f);
            float cy = Mathf.Cos(yaw * 0.5f), sy = Mathf.Sin(yaw * 0.5f);
            return new Quaternion(
                sr * cp * cy - cr * sp * sy,
                cr * sp * cy + sr * cp * sy,
                cr * cp * sy - sr * sp * cy,
                cr * cp * cy + sr * sp * sy);
        }

        public static Quaternion RpyToUnity(float roll, float pitch, float yaw) => ToUnity(RosRpy(roll, pitch, yaw));

        /// <summary>Quay mot goc (rad) quanh truc bieu dien trong he ROS, tra ve quaternion Unity.</summary>
        public static Quaternion AxisAngleToUnity(Vector3 rosAxis, float rad)
        {
            Vector3 a = rosAxis.normalized;
            float s = Mathf.Sin(rad * 0.5f);
            return ToUnity(new Quaternion(a.x * s, a.y * s, a.z * s, Mathf.Cos(rad * 0.5f)));
        }

        /// <summary>Kich thuoc hop (dai theo x, y, z cua ROS) -> localScale Unity.</summary>
        public static Vector3 ScaleToUnity(Vector3 ros) => new Vector3(Mathf.Abs(ros.y), Mathf.Abs(ros.z), Mathf.Abs(ros.x));
    }

    public static class TwinMaterials
    {
        public static Material Lit(Color c)
        {
            var sh = Shader.Find("Universal Render Pipeline/Lit");
            if (sh == null) sh = Shader.Find("Standard");
            var m = new Material(sh);
            SetColor(m, c);
            return m;
        }

        public static Material Unlit(Color c)
        {
            var sh = Shader.Find("Universal Render Pipeline/Unlit");
            if (sh == null) sh = Shader.Find("Sprites/Default");
            var m = new Material(sh);
            SetColor(m, c);
            return m;
        }

        public static void SetColor(Material m, Color c)
        {
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
        }
    }

    public static class TwinUtil
    {
        public static Transform FindDeep(Transform parent, string name)
        {
            if (parent.name == name) return parent;
            foreach (Transform child in parent)
            {
                var t = FindDeep(child, name);
                if (t != null) return t;
            }
            return null;
        }

        public static void RemoveCollider(GameObject go)
        {
            var col = go.GetComponent<Collider>();
            if (col == null) return;
            if (Application.isPlaying) Object.Destroy(col);
            else Object.DestroyImmediate(col);
        }

        public static void DestroyAny(Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) Object.Destroy(o);
            else Object.DestroyImmediate(o);
        }
    }
}
