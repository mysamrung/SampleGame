using UnityEditor;

[InitializeOnLoad]
public class MeshletReloadManager
{
    static MeshletReloadManager()
    {
        AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;
    }

    private static void OnBeforeAssemblyReload()
    {
        MeshletManager.ReleaseAll();
    }
}
