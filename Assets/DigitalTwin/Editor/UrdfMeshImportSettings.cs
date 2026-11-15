using UnityEditor;

/// <summary>
/// Thiet lap import cho mesh trong Assets/URDF/meshes:
/// giu dung don vi met, tu tinh normal (mesh CAD khong co normal), khong tao material/collider/animation.
/// </summary>
public class UrdfMeshImportSettings : AssetPostprocessor
{
    void OnPreprocessModel()
    {
        if (!assetPath.Replace('\\', '/').Contains("/URDF/meshes/")) return;
        var mi = (ModelImporter)assetImporter;
        mi.globalScale = 1f;
        mi.useFileScale = true;
        mi.bakeAxisConversion = false;
        mi.importNormals = ModelImporterNormals.Calculate;
        mi.normalSmoothingAngle = 30f;
        mi.importTangents = ModelImporterTangents.None;
        mi.materialImportMode = ModelImporterMaterialImportMode.None;
        mi.addCollider = false;
        mi.importAnimation = false;
        mi.animationType = ModelImporterAnimationType.None;
        mi.isReadable = false;
        mi.meshCompression = ModelImporterMeshCompression.Off;
    }
}
