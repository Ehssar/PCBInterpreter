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

        RenderTexture rt = RenderTexture.GetTemporary(
            width,
            height,
            0,
            RenderTextureFormat.ARGB32,
            RenderTextureReadWrite.sRGB
        );

        try
        {
            Graphics.Blit(source, rt);
        }
        catch (Exception e)
        {
            RenderTexture.ReleaseTemporary(rt);
            busy = false;
            onError?.Invoke($"Graphics.Blit failed: {e.Message}");
            return;
        }

        AsyncGPUReadback.Request(rt, 0, TextureFormat.RGBA32, request =>
        {
            try
            {
                if (request.hasError)
                {
                    busy = false;
                    onError?.Invoke("AsyncGPUReadback failed after blit.");
                    return;
                }

                var data = request.GetData<byte>();
                Texture2D cpuTex = new Texture2D(width, height, TextureFormat.RGBA32, false);
                cpuTex.LoadRawTextureData(data);
                cpuTex.Apply();

                busy = false;
                onSuccess?.Invoke(cpuTex);
            }
            catch (Exception e)
            {
                busy = false;
                onError?.Invoke($"Failed to build CPU texture after blit: {e.Message}");
            }
            finally
            {
                RenderTexture.ReleaseTemporary(rt);
            }
        });
    }
}