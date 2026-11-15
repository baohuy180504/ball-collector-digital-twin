using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Xml;
using DigitalTwin;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Menu "Digital Twin" tren thanh menu Unity:
///   1. Build Robot From URDF : doc file URDF (box/cylinder/sphere/mesh OBJ) -> dung model + prefab, khong vat ly.
///   2. Setup Scene           : tao san, bo nhan UDP, bong, path, HUD, camera va dat robot vao scene.
/// </summary>
public static class DigitalTwinMenu
{
    const string CadUrdf = "Assets/URDF/robot_gom_bong_cad.urdf";     // model CAD (mesh OBJ)
    const string BoxUrdf = "Assets/URDF/robot_gom_bong.urdf";         // model hop don gian (du phong)
    static string DefaultUrdf => File.Exists(CadUrdf) ? CadUrdf : BoxUrdf;
    static string urdfDir = "Assets/URDF";
    const string MatDir = "Assets/DigitalTwin/Materials";
    const string PrefabDir = "Assets/DigitalTwin/Prefabs";

    [MenuItem("Digital Twin/1. Build Robot From URDF")]
    static void BuildRobotMenu()
    {
        string path = DefaultUrdf;
        if (!File.Exists(path))
        {
            path = EditorUtility.OpenFilePanel("Chon file URDF", Application.dataPath, "urdf");
            if (string.IsNullOrEmpty(path)) return;
        }
        var go = BuildRobot(path);
        if (go != null)
        {
            Selection.activeGameObject = go;
            EditorSceneManager.MarkSceneDirty(go.scene);
        }
    }

    [MenuItem("Digital Twin/2. Setup Scene")]
    static void SetupScene()
    {
        var twin = GameObject.Find("DigitalTwin");
        if (twin == null)
        {
            twin = new GameObject("DigitalTwin");
            Undo.RegisterCreatedObjectUndo(twin, "Setup Digital Twin");
        }
        twin.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

        var rx = GetOrAdd<TwinUdpReceiver>(twin);
        var field = GetOrAdd<FieldView>(twin);
        var balls = GetOrAdd<BallManager>(twin);
        var path = GetOrAdd<PathView>(twin);
        var hud = GetOrAdd<TwinHud>(twin);
        var feed = GetOrAdd<CameraFeedReceiver>(twin);
        var control = GetOrAdd<TwinControl>(twin);
        var launcher = GetOrAdd<BrainLauncher>(twin);
        rx.port = 5105;            // 5005/5006 danh cho ESP32
        feed.port = 5106;
        control.cmdPort = 5107;
        launcher.cmdPort = 5107;
        field.Build();
        balls.receiver = rx;
        path.receiver = rx;

        var robot = Object.FindFirstObjectByType<RobotTwin>();
        if (robot == null)
        {
            GameObject go = null;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabDir + "/robot_gom_bong.prefab");
            if (prefab != null) go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            else if (File.Exists(DefaultUrdf)) go = BuildRobot(DefaultUrdf);
            if (go != null) robot = go.GetComponent<RobotTwin>();
        }
        if (robot != null)
        {
            robot.receiver = rx;
            robot.transform.SetPositionAndRotation(RosUnity.ToUnity(0.5f, field.widthY * 0.5f, 0f), Quaternion.identity);
            EditorUtility.SetDirty(robot);
        }
        else
        {
            Debug.LogWarning("[DigitalTwin] Chua co robot. Chay 'Digital Twin/1. Build Robot From URDF' truoc.");
        }

        var cam = Camera.main;
        if (cam == null)
        {
            var cgo = new GameObject("Main Camera") { tag = "MainCamera" };
            cam = cgo.AddComponent<Camera>();
            Undo.RegisterCreatedObjectUndo(cgo, "Setup Digital Twin");
        }
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.12f, 0.12f, 0.14f);

        hud.receiver = rx;
        hud.feed = feed;
        hud.control = control;
        hud.launcher = launcher;
        hud.robot = robot;
        hud.balls = balls;
        hud.field = field;
        hud.cam = cam;
        hud.ApplyView();

        if (Object.FindFirstObjectByType<Light>() == null)
        {
            var lgo = new GameObject("Directional Light");
            var l = lgo.AddComponent<Light>();
            l.type = LightType.Directional;
            lgo.transform.rotation = Quaternion.Euler(60f, -30f, 0f);
            Undo.RegisterCreatedObjectUndo(lgo, "Setup Digital Twin");
        }

        EditorUtility.SetDirty(twin);
        EditorUtility.SetDirty(cam);
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        Selection.activeGameObject = twin;
        Debug.Log("[DigitalTwin] Da setup scene. Bam Play: Unity tu chay Brain/main.py (che do theo Brain/config.py).");
    }

    static T GetOrAdd<T>(GameObject go) where T : Component
    {
        var c = go.GetComponent<T>();
        if (c == null) c = Undo.AddComponent<T>(go);
        return c;
    }

    // ------------------------------------------------------------------ URDF

    public static GameObject BuildRobot(string urdfPath)
    {
        var doc = new XmlDocument();
        doc.Load(urdfPath);
        urdfDir = Path.GetDirectoryName(urdfPath);
        var robotEl = doc.DocumentElement;
        string robotName = robotEl.GetAttribute("name");
        if (string.IsNullOrEmpty(robotName)) robotName = "robot";

        var colors = new Dictionary<string, Color>();
        foreach (XmlElement m in robotEl.SelectNodes("material"))
        {
            if (m.SelectSingleNode("color") is XmlElement c)
                colors[m.GetAttribute("name")] = ParseColor(c.GetAttribute("rgba"));
        }

        var links = new Dictionary<string, XmlElement>();
        foreach (XmlElement l in robotEl.SelectNodes("link")) links[l.GetAttribute("name")] = l;

        var joints = new List<XmlElement>();
        var children = new HashSet<string>();
        foreach (XmlElement j in robotEl.SelectNodes("joint"))
        {
            joints.Add(j);
            children.Add(LinkRef(j, "child"));
        }

        string rootLink = null;
        foreach (var n in links.Keys)
            if (!children.Contains(n)) { rootLink = n; break; }
        if (rootLink == null)
        {
            Debug.LogError("[DigitalTwin] Khong tim thay link goc trong URDF.");
            return null;
        }

        var existing = GameObject.Find(robotName);
        if (existing != null) Undo.DestroyObjectImmediate(existing);

        EnsureFolder(MatDir);
        EnsureFolder(PrefabDir);

        var root = new GameObject(robotName);
        Undo.RegisterCreatedObjectUndo(root, "Build robot from URDF");

        var linkTf = new Dictionary<string, Transform>();
        linkTf[rootLink] = MakeLink(links[rootLink], root.transform, colors);

        var pending = new List<XmlElement>(joints);
        bool progress = true;
        while (pending.Count > 0 && progress)
        {
            progress = false;
            for (int i = pending.Count - 1; i >= 0; i--)
            {
                var j = pending[i];
                string p = LinkRef(j, "parent"), c = LinkRef(j, "child");
                if (!linkTf.ContainsKey(p) || !links.ContainsKey(c)) continue;
                var t = MakeLink(links[c], linkTf[p], colors);
                ReadOrigin(j, out var xyz, out var rpy);
                t.localPosition = RosUnity.ToUnity(xyz);
                t.localRotation = RosUnity.RpyToUnity(rpy.x, rpy.y, rpy.z);
                linkTf[c] = t;
                pending.RemoveAt(i);
                progress = true;
            }
        }
        if (pending.Count > 0) Debug.LogWarning($"[DigitalTwin] {pending.Count} joint khong noi duoc vao cay link.");

        root.AddComponent<RobotTwin>();
        string prefabPath = $"{PrefabDir}/{robotName}.prefab";
        PrefabUtility.SaveAsPrefabAssetAndConnect(root, prefabPath, InteractionMode.UserAction);
        Debug.Log($"[DigitalTwin] Da dung robot '{robotName}' ({linkTf.Count} link) -> {prefabPath}");
        return root;
    }

    static Transform MakeLink(XmlElement link, Transform parent, Dictionary<string, Color> colors)
    {
        var go = new GameObject(link.GetAttribute("name"));
        var t = go.transform;
        t.SetParent(parent, false);

        int i = 0;
        foreach (XmlElement v in link.SelectNodes("visual"))
        {
            if (!(v.SelectSingleNode("geometry") is XmlElement geom)) continue;
            GameObject g;
            Vector3 scale;
            if (geom.SelectSingleNode("box") is XmlElement box)
            {
                g = GameObject.CreatePrimitive(PrimitiveType.Cube);
                scale = RosUnity.ScaleToUnity(ParseVec(box.GetAttribute("size")));
            }
            else if (geom.SelectSingleNode("cylinder") is XmlElement cyl)
            {
                g = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                float r = F(cyl.GetAttribute("radius")), len = F(cyl.GetAttribute("length"));
                scale = new Vector3(2f * r, 0.5f * len, 2f * r);   // cylinder Unity: cao 2, phi 1, truc y
            }
            else if (geom.SelectSingleNode("sphere") is XmlElement sph)
            {
                g = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                scale = Vector3.one * 2f * F(sph.GetAttribute("radius"));
            }
            else if (geom.SelectSingleNode("mesh") is XmlElement mesh)
            {
                string assetPath = ResolveMeshPath(mesh.GetAttribute("filename"));
                var model = assetPath != null ? AssetDatabase.LoadAssetAtPath<GameObject>(assetPath) : null;
                if (model == null)
                {
                    Debug.LogWarning($"[DigitalTwin] Link {go.name}: khong tim thay mesh '{mesh.GetAttribute("filename")}' " +
                                     "(Unity chi doc .obj/.fbx; STL can doi sang OBJ). Bo qua.");
                    continue;
                }
                g = (GameObject)Object.Instantiate(model);
                scale = mesh.HasAttribute("scale") ? RosUnity.ScaleToUnity(ParseVec(mesh.GetAttribute("scale"))) : Vector3.one;
            }
            else
            {
                Debug.LogWarning($"[DigitalTwin] Link {go.name}: geometry khong ho tro, bo qua.");
                continue;
            }

            g.name = "visual_" + i++;
            foreach (var col0 in g.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(col0);
            g.transform.SetParent(t, false);
            ReadOrigin(v, out var xyz, out var rpy);
            g.transform.localPosition = RosUnity.ToUnity(xyz);
            g.transform.localRotation = RosUnity.RpyToUnity(rpy.x, rpy.y, rpy.z);
            g.transform.localScale = scale;

            string matName = "default";
            Color col = new Color(0.6f, 0.6f, 0.6f);
            if (v.SelectSingleNode("material") is XmlElement me)
            {
                matName = me.GetAttribute("name");
                if (me.SelectSingleNode("color") is XmlElement ce) col = ParseColor(ce.GetAttribute("rgba"));
                else if (colors.TryGetValue(matName, out var c)) col = c;
            }
            var mat = GetMaterial(matName, col);
            foreach (var rend in g.GetComponentsInChildren<Renderer>(true))
            {
                var mats = rend.sharedMaterials;
                for (int k = 0; k < mats.Length; k++) mats[k] = mat;
                rend.sharedMaterials = mats;
            }
        }
        return t;
    }

    // "meshes/body.obj" (tuong doi file URDF) hoac "package://pkg/meshes/x.STL" -> duong dan "Assets/..."
    static string ResolveMeshPath(string filename)
    {
        if (string.IsNullOrEmpty(filename)) return null;
        string rel = filename.Replace('\\', '/');
        if (rel.StartsWith("package://"))
        {
            rel = rel.Substring("package://".Length);
            int slash = rel.IndexOf('/');
            if (slash >= 0) rel = rel.Substring(slash + 1);   // bo ten package
        }
        string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "..")).Replace('\\', '/').TrimEnd('/') + "/";
        string full = Path.GetFullPath(Path.Combine(urdfDir, rel)).Replace('\\', '/');
        string[] candidates = { full, Path.ChangeExtension(full, ".obj").Replace('\\', '/'), Path.ChangeExtension(full, ".fbx").Replace('\\', '/') };
        foreach (var c in candidates)
        {
            if (!File.Exists(c)) continue;
            return c.StartsWith(projectRoot) ? c.Substring(projectRoot.Length) : null;
        }
        return null;
    }

    static Material GetMaterial(string name, Color c)
    {
        if (string.IsNullOrEmpty(name)) name = "default";
        string path = $"{MatDir}/{name}.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            m = TwinMaterials.Lit(c);
            AssetDatabase.CreateAsset(m, path);
        }
        else
        {
            TwinMaterials.SetColor(m, c);
            EditorUtility.SetDirty(m);
        }
        return m;
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }

    static string LinkRef(XmlElement joint, string tag) =>
        (joint.SelectSingleNode(tag) as XmlElement)?.GetAttribute("link") ?? "";

    static void ReadOrigin(XmlElement el, out Vector3 xyz, out Vector3 rpy)
    {
        xyz = Vector3.zero;
        rpy = Vector3.zero;
        if (el.SelectSingleNode("origin") is XmlElement o)
        {
            if (o.HasAttribute("xyz")) xyz = ParseVec(o.GetAttribute("xyz"));
            if (o.HasAttribute("rpy")) rpy = ParseVec(o.GetAttribute("rpy"));
        }
    }

    // Luon dung InvariantCulture: Windows tieng Viet dung dau phay thap phan.
    static float F(string s) => float.Parse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture);

    static Vector3 ParseVec(string s)
    {
        var p = s.Split(new[] { ' ', '\t', '\n', '\r' }, System.StringSplitOptions.RemoveEmptyEntries);
        return new Vector3(F(p[0]), F(p[1]), F(p[2]));
    }

    static Color ParseColor(string s)
    {
        var p = s.Split(new[] { ' ', '\t', '\n', '\r' }, System.StringSplitOptions.RemoveEmptyEntries);
        return new Color(F(p[0]), F(p[1]), F(p[2]), p.Length > 3 ? F(p[3]) : 1f);
    }
}
