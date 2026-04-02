using System;
using System.Collections;
using Meta.XR;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

public class PcaDirectCapture : MonoBehaviour
{
    [SerializeField] private PassthroughCameraAccess pca;

    [Header("Debug")]
    [SerializeField] private bool logRuntimeState = true;
    [SerializeField] private float runtimeLogIntervalSeconds = 2f;
    [SerializeField] private float autoRestartDelaySeconds = 5f;
    [SerializeField] private bool autoRestartIfNotPlaying = true;

    [Header("Capture Timing")]
    [SerializeField] private float readyTimeoutSeconds = 1.0f;
    [SerializeField] private float updatedFrameTimeoutSeconds = 0.5f;
    [SerializeField] private float gpuReadbackTimeoutSeconds = 1.0f;

    private bool busy;
    private float nextStateLogTime = 0f;
    private bool restartAttempted = false;

    public bool IsBusy => busy;

    void Awake()
    {
        // Debug.Log("[Passthrough] PcaDirectCapture Awake");

        if (pca == null)
        {
            Debug.Log("[Passthrough] pca field was null, attempting GetComponent<PassthroughCameraAccess>()");
            pca = GetComponent<PassthroughCameraAccess>();
        }

        if (pca == null)
        {
            Debug.LogError("[Passthrough] PassthroughCameraAccess missing.");
        }
        else
        {
            // Debug.Log("[Passthrough] Found PassthroughCameraAccess component.");
        }
    }

    void Start()
    {
        if (pca == null)
        {
            Debug.LogError("[Passthrough] Start: pca is null.");
            return;
        }

        Debug.Log(
            $"[Passthrough] Start State => enabled={pca.enabled}, " +
            $"isPlaying={pca.IsPlaying}, " +
            $"updatedThisFrame={pca.IsUpdatedThisFrame}, " +
            $"resolution={pca.CurrentResolution}"
        );
    }

    void Update()
    {
        if (pca == null) return;

        if (logRuntimeState && Time.time >= nextStateLogTime)
        {
            nextStateLogTime = Time.time + runtimeLogIntervalSeconds;

            // Debug.Log(
            //     $"[Passthrough] Runtime State => enabled={pca.enabled}, " +
            //     $"isPlaying={pca.IsPlaying}, " +
            //     $"updatedThisFrame={pca.IsUpdatedThisFrame}, " +
            //     $"resolution={pca.CurrentResolution}"
            // );
        }

        if (autoRestartIfNotPlaying &&
            !restartAttempted &&
            pca.enabled &&
            !pca.IsPlaying &&
            Time.time >= autoRestartDelaySeconds)
        {
            restartAttempted = true;
            Debug.Log("[Passthrough] PCA not playing after delay. Restarting component...");
            StartCoroutine(RestartPca());
        }
    }

    public bool IsReady()
    {
        if (pca == null)
        {
            Debug.LogWarning("[Passthrough] IsReady called but pca is null.");
            return false;
        }

        Debug.Log(
            $"[Passthrough] IsReady State => enabled={pca.enabled}, " +
            $"isPlaying={pca.IsPlaying}, " +
            $"updatedThisFrame={pca.IsUpdatedThisFrame}, " +
            $"resolution={pca.CurrentResolution}"
        );

        return pca.enabled &&
               pca.IsPlaying &&
               pca.CurrentResolution.x > 0 &&
               pca.CurrentResolution.y > 0;
    }

    public void TryCaptureLatestFrame(Action<Texture2D> onSuccess, Action<string> onError)
    {
        if (busy)
        {
            onError?.Invoke("[Passthrough] Passthrough capture already in progress.");
            return;
        }

        StartCoroutine(CaptureLatestFrameRoutine(onSuccess, onError));
    }

    private IEnumerator CaptureLatestFrameRoutine(Action<Texture2D> onSuccess, Action<string> onError)
    {
        Debug.Log("[Passthrough] CaptureLatestFrameRoutine started");

        if (pca == null)
        {
            onError?.Invoke("[Passthrough] PassthroughCameraAccess missing.");
            yield break;
        }

        busy = true;

        float startTime = Time.time;

        while (Time.time - startTime < readyTimeoutSeconds)
        {
            if (pca != null &&
                pca.enabled &&
                pca.IsPlaying &&
                pca.CurrentResolution.x > 0 &&
                pca.CurrentResolution.y > 0)
            {
                break;
            }

            yield return null;
        }

        if (pca == null)
        {
            busy = false;
            onError?.Invoke("[Passthrough] PassthroughCameraAccess became null.");
            yield break;
        }

        if (!pca.enabled)
        {
            busy = false;
            onError?.Invoke("[Passthrough] PCA disabled.");
            yield break;
        }

        if (!pca.IsPlaying)
        {
            busy = false;
            onError?.Invoke("[Passthrough] PCA not playing.");
            yield break;
        }

        Vector2Int res = pca.CurrentResolution;
        if (res.x <= 0 || res.y <= 0)
        {
            busy = false;
            onError?.Invoke("[Passthrough] Invalid resolution.");
            yield break;
        }

        Debug.Log($"[Passthrough] Ready => {res}");

        float updateStart = Time.time;
        while (Time.time - updateStart < updatedFrameTimeoutSeconds)
        {
            if (pca.IsUpdatedThisFrame)
                break;

            yield return null;
        }

        Texture src = pca.GetTexture();
        if (src == null)
        {
            busy = false;
            onError?.Invoke("[Passthrough] GetTexture returned null.");
            yield break;
        }

        int width = res.x;
        int height = res.y;

        Debug.Log(
            $"[Passthrough] Got source texture: " +
            $"type={src.GetType().Name}, width={src.width}, height={src.height}, target={src.dimension}"
        );

        bool requestDone = false;
        bool requestError = false;
        string requestErrorMessage = null;
        NativeArray<Color32> readbackData = default;

        AsyncGPUReadback.Request(src, 0, TextureFormat.RGBA32, request =>
        {
            requestDone = true;

            if (request.hasError)
            {
                requestError = true;
                requestErrorMessage = "[Passthrough] AsyncGPUReadback request had an error.";
                return;
            }

            try
            {
                readbackData = request.GetData<Color32>();
            }
            catch (Exception e)
            {
                requestError = true;
                requestErrorMessage = $"[Passthrough] Failed to get readback data: {e.Message}";
            }
        });

        float readbackStart = Time.time;
        while (!requestDone && (Time.time - readbackStart < gpuReadbackTimeoutSeconds))
        {
            yield return null;
        }

        if (!requestDone)
        {
            busy = false;
            onError?.Invoke("[Passthrough] AsyncGPUReadback timed out.");
            yield break;
        }

        if (requestError)
        {
            busy = false;
            onError?.Invoke(requestErrorMessage ?? "[Passthrough] AsyncGPUReadback failed.");
            yield break;
        }

        int expectedPixels = width * height;
        if (!readbackData.IsCreated || readbackData.Length != expectedPixels)
        {
            busy = false;
            onError?.Invoke(
                $"[Passthrough] Readback size mismatch. len={readbackData.Length}, expected={expectedPixels}, " +
                $"texSize={src.width}x{src.height}, pcaRes={width}x{height}"
            );
            yield break;
        }

        Texture2D tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
        tex.SetPixels32(readbackData.ToArray());
        tex.Apply(false, false);

        Debug.Log(
            $"[Passthrough] GPU readback capture complete: {width}x{height}, readbackLen={readbackData.Length}"
        );

        busy = false;
        onSuccess?.Invoke(tex);
    }

    private IEnumerator RestartPca()
    {
        if (pca == null)
        {
            Debug.LogError("[Passthrough] RestartPca called but pca is null.");
            yield break;
        }

        Debug.Log("[Passthrough] PCA disabled for restart.");
        pca.enabled = false;

        yield return new WaitForSeconds(1f);

        pca.enabled = true;
        Debug.Log("[Passthrough] PCA re-enabled after restart.");

        yield return null;

        Debug.Log(
            $"[Passthrough] Post-Restart State => enabled={pca.enabled}, " +
            $"isPlaying={pca.IsPlaying}, " +
            $"updatedThisFrame={pca.IsUpdatedThisFrame}, " +
            $"resolution={pca.CurrentResolution}"
        );
    }
}