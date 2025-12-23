using Cysharp.Threading.Tasks;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public class VertexAnimationTools : EditorWindow
{
    private GameObject rootPrefab;
    private AnimationClip clip;
    private string outputPath = "Assets/VertexAnimaton/Textures";

    [MenuItem("Tools/Bake Vertex Animation Tools")]
    public static void ShowExample()
    {
        var wnd = GetWindow<VertexAnimationTools>();
        wnd.Show();
    }
    public void OnGUI()
    {

        GUILayout.Label("Vertex Animation Tool", EditorStyles.boldLabel);
        rootPrefab = (GameObject)EditorGUILayout.ObjectField("Root Prefab", rootPrefab, typeof(GameObject), false);
        clip = (AnimationClip)EditorGUILayout.ObjectField("Animation Clip", clip, typeof(AnimationClip), false);
        outputPath = EditorGUILayout.TextField("Output Path", outputPath);

        if (GUILayout.Button("???s", GUILayout.MaxWidth(80)))
        {
             BakeAnimation(rootPrefab, clip, outputPath).Forget();
        }
    }

    private Texture2D CreateAnimationTexture(int width, int height, bool isLooping)
    {
        return new Texture2D(width, height, TextureFormat.RGBAFloat, false)
        {
            wrapModeU = TextureWrapMode.Clamp,
            wrapModeV = isLooping ? TextureWrapMode.Repeat : TextureWrapMode.Clamp,
            filterMode = FilterMode.Point,
        };
    }

    private float PackNormal(Vector3 normal)
    {
        float nx = Mathf.Clamp01((normal.x * 0.5f) + 0.5f);
        float ny = Mathf.Clamp01((normal.y * 0.5f) + 0.5f);
        float nz = Mathf.Clamp01((normal.z * 0.5f) + 0.5f);

        int x = Mathf.Clamp(Mathf.RoundToInt(nx * 1023), 0, 1023);
        int y = Mathf.Clamp(Mathf.RoundToInt(ny * 1023), 0, 1023);
        int z = Mathf.Clamp(Mathf.RoundToInt(nz * 1023), 0, 1023);

        int packed = (x << 20) | (y << 10) | z;
        return BitConverter.ToSingle(BitConverter.GetBytes(packed), 0);
    }

    private async UniTask BakeAnimation(GameObject rootPrefab, AnimationClip clip, string outputPath)
    {
        // ???m?????????????Root??Transform????????
        rootPrefab.transform.localPosition = Vector3.zero;
        rootPrefab.transform.localRotation = Quaternion.identity;
        rootPrefab.transform.localScale = Vector3.one;

        var height = (int)(clip.length * clip.frameRate);
        var dt = 1f / clip.frameRate;

        var renderers = rootPrefab.GetComponentsInChildren<Renderer>();

        var names = renderers.Select(a => a.transform.name).ToList();
        var vatList = new List<Texture>();
        foreach (var renderer in renderers)
        {
            switch (renderer)
            {
                case MeshRenderer meshRenderer:
                {
                    var transform = meshRenderer.transform;
                    var meshFilter = meshRenderer.GetComponent<MeshFilter>();
                    var mesh = meshFilter.sharedMesh;
                    var vat = CreateAnimationTexture(mesh.vertexCount, height, clip.isLooping);
                    for (var k = 0; k < height; ++k)
                    {
                        var t = k * dt;
                        clip.SampleAnimation(rootPrefab.gameObject, t);

                        await UniTask.DelayFrame(1);

                        for (var j = 0; j < mesh.vertexCount; ++j)
                        {
                            var vertex = mesh.vertices[j];
                            var normal = mesh.normals[j];
                            var worldMatrix = Matrix4x4.TRS(transform.position, transform.rotation, transform.lossyScale);
                            var newPos = worldMatrix.MultiplyPoint(vertex);
                            var newNor = worldMatrix.MultiplyVector(normal).normalized;

                            float packedNormal = PackNormal(newNor);

                            vat.SetPixel(j, k, new Color(newPos.x, newPos.y, newPos.z, packedNormal));
                        }
                    }
                    vatList.Add(vat);
                    break;
                }
                case SkinnedMeshRenderer skinnedMeshRenderer:
                {
                    var transform = skinnedMeshRenderer.rootBone;
                    transform.position = Vector3.zero;
                    transform.rotation = Quaternion.identity;
                    transform.localScale = Vector3.one;
                    
                    var mesh = skinnedMeshRenderer.sharedMesh;
                    var vat = CreateAnimationTexture(mesh.vertexCount, height, clip.isLooping);
                    for (var k = 0; k < height; ++k)
                    {
                        var t = k * dt;
                        clip.SampleAnimation(rootPrefab.gameObject, t);

                        await UniTask.DelayFrame(1);

                        var bakedMesh = new Mesh();
                        skinnedMeshRenderer.BakeMesh(bakedMesh);
                        for (var j = 0; j < bakedMesh.vertexCount; ++j)
                        {
                            var newPos = bakedMesh.vertices[j];
                            var newNor = bakedMesh.normals[j];

                            float packedNormal = PackNormal(newNor);

                            vat.SetPixel(j, k, new Color(newPos.x, newPos.y, newPos.z, packedNormal));
                        }
                        DestroyImmediate(bakedMesh);
                    }
                    vatList.Add(vat);
                    break;
                }
            }
        }

        if (!Directory.Exists(outputPath))
        {
            Directory.CreateDirectory(outputPath);
        }

        for (var i = 0; i < renderers.Length; ++i)
        {
            AssetDatabase.CreateAsset(vatList[i], Path.Combine(outputPath, clip.name + names[i] + ".asset"));
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }
}
