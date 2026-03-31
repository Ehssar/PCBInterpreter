using System;
using Meta.XR;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

public class PcaDirectCapture : MonoBehaviour
{
    [SerializeField] private PassthroughCameraAccess pca;
    private bool busy;

    void Awake()
    {
        if (pca == null)
        {
            pca = GetComponent<PassthroughCameraAccess>();
        }

        if (pca == null)
        {
            Debug.LogError("PassthroughCameraAccess missing.");
        }
    }

    public bool IsBusy => busy;

    public void TryCaptureLatestFrame(Action<Texture2D> onSuccess, Action<string> onError)
    {
        if (pca == null)
        {
            onError?.Invoke("PassthroughCameraAccess missing.");
            return;
        }

        if (busy)
        {
            onError?.Invoke("Passthrough capture already in progress.");
            return;
        }

        if (pca.TargetMaterial == null)
        {
            onError?.Invoke(
                "PassthroughCameraAccess.TargetMaterial is not set. " +
                "Assign a material so the live camera texture can be read."
            );
            return;
        }

        string prop = string.IsNullOrEmpty(pca.TexturePropertyName) ? "_MainTex" : pca.TexturePropertyName;
        Texture tex = pca.TargetMaterial.GetTexture(prop);

        if (tex == null)
        {
            onError?.Invoke($"No texture found on TargetMaterial property '{prop}'.");
            return;
        }

        busy = true;

        // Always normalize through an uncompressed RenderTexture first.
        CaptureViaBlit(tex, onSuccess, onError);
    }

    private void CaptureViaBlit(Texture source, Action<Texture2D> onSuccess, Action<string> onError)
    {
        int width = source.width;
        int height = source.height;

        RenderTexture rt = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32);
        rt.Create();

        RenderTexture prev = RenderTexture.active;

        try
        {
            // Force proper rendering into RT
            Graphics.Blit(source, rt);

            RenderTexture.active = rt;

            Texture2D cpuTex = new Texture2D(width, height, TextureFormat.RGB24, false);
            cpuTex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            cpuTex.Apply();

            onSuccess?.Invoke(cpuTex);
        }
        catch (Exception e)
        {
            onError?.Invoke($"ReadPixels failed: {e.Message}");
        }
        finally
        {
            RenderTexture.active = prev;
            rt.Release();
            busy = false;
        }
    }
}