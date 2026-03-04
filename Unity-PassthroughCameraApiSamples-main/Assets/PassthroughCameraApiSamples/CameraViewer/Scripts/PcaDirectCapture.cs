using System;
using System.IO;
using Meta.XR;
using UnityEngine;
using UnityEngine.Rendering;

public class PcaDirectCapture : MonoBehaviour
{
    [Header("Press Space in Editor. On-device we’ll later swap to controller input.")]
    public KeyCode captureKey = KeyCode.Space;

    [Header("JPG Quality 1-100")]
    [Range(1, 100)]
    public int jpgQuality = 90;

    private PassthroughCameraAccess pca;
    private bool busy;

    void Awake()
    {
        pca = GetComponent<PassthroughCameraAccess>();
        if (pca == null)
        {
            Debug.LogError("PcaDirectCapture must be attached to the same GameObject as PassthroughCameraAccess.");
        }
    }

    void Update()
    {
        if (pca == null) return;

        if (Input.GetKeyDown(captureKey))
        {
            TryCaptureFromPcaMaterial();
        }
    }

    void TryCaptureFromPcaMaterial()
    {
        if (busy) return;

        if (pca.TargetMaterial == null)
        {
            Debug.LogError(
                "PassthroughCameraAccess.TargetMaterial is not set. " +
                "Assign a material to TargetMaterial so we can read the live camera texture from it."
            );
            return;
        }

        // PassthroughCameraAccess uses _MainTex by default if TexturePropertyName is blank
        string prop = string.IsNullOrEmpty(pca.TexturePropertyName) ? "_MainTex" : pca.TexturePropertyName;

        Texture tex = pca.TargetMaterial.GetTexture(prop);
        if (tex == null)
        {
            Debug.LogError($"No texture found on TargetMaterial property '{prop}'.");
            return;
        }

        busy = true;

        // Handle both RenderTexture and Texture2D
        if (tex is RenderTexture rt)
        {
            CaptureRenderTexture(rt);
        }
        else if (tex is Texture2D t2d)
        {
            CaptureTexture2D(t2d);
        }
        else
        {
            Debug.LogError($"Unsupported texture type: {tex.GetType().Name}. Expected RenderTexture or Texture2D.");
            busy = false;
        }
    }

    void CaptureRenderTexture(RenderTexture rt)
    {
        AsyncGPUReadback.Request(rt, 0, request =>
        {
            if (request.hasError)
            {
                Debug.LogError("AsyncGPUReadback failed for RenderTexture.");
                busy = false;
                return;
            }

            var data = request.GetData<byte>();
            var cpuTex = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
            cpuTex.LoadRawTextureData(data);
            cpuTex.Apply();

            SaveJpg(cpuTex);
            Destroy(cpuTex);
            busy = false;
        });
    }

    void CaptureTexture2D(Texture2D t2d)
    {
        // If it’s readable on CPU, easiest path:
        try
        {
            // Some GPU-backed Texture2D are not readable; EncodeToJPG will throw.
            byte[] jpg = t2d.EncodeToJPG(jpgQuality);
            WriteBytes(jpg, t2d.width, t2d.height);
            busy = false;
        }
        catch (Exception)
        {
            // Fallback: GPU readback from Texture2D
            AsyncGPUReadback.Request(t2d, 0, request =>
            {
                if (request.hasError)
                {
                    Debug.LogError("AsyncGPUReadback failed for Texture2D.");
                    busy = false;
                    return;
                }

                var data = request.GetData<byte>();
                var cpuTex = new Texture2D(t2d.width, t2d.height, TextureFormat.RGBA32, false);
                cpuTex.LoadRawTextureData(data);
                cpuTex.Apply();

                SaveJpg(cpuTex);
                Destroy(cpuTex);
                busy = false;
            });
        }
    }

    void SaveJpg(Texture2D cpuTex)
    {
        byte[] jpg = cpuTex.EncodeToJPG(jpgQuality);
        WriteBytes(jpg, cpuTex.width, cpuTex.height);
    }

    void WriteBytes(byte[] jpg, int w, int h)
    {
        string name = $"pca_{DateTime.Now:yyyyMMdd_HHmmss_fff}_{w}x{h}.jpg";
        string path = Path.Combine(Application.persistentDataPath, name);
        File.WriteAllBytes(path, jpg);
        Debug.Log($"Saved PCA frame: {path}");
    }
}
