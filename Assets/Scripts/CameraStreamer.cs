using UnityEngine;
using UnityEngine.UI;
using System.Collections;

public class CameraStreamer : MonoBehaviour
{
        [Header("UI Components")]
        public RawImage cameraDisplay;

        [Header("Camera Settings")]
        public int requestedWidth = 1920;
        public int requestedHeight = 1080;
        public int requestedFPS = 30;

        [Header("Detection Model")]
        public string modelPath; // Set this in Inspector or code
        private bool detectorReady = false;

        private WebCamTexture webCamTexture;
        private Texture2D tex;
        private bool isCameraInitialized = false;

    void Start()
    {
        StartCoroutine(RequestAndStartCamera());
        StartCoroutine(CopyModelAndInitDetector());
    }

    IEnumerator CopyModelAndInitDetector()
    {
        string srcPath = System.IO.Path.Combine(Application.streamingAssetsPath, modelPath);
        string dstPath = System.IO.Path.Combine(Application.persistentDataPath, modelPath);

        Debug.Log($"[CameraStreamer] Copying model from {srcPath} to {dstPath}");

        #if UNITY_ANDROID && !UNITY_EDITOR
        UnityEngine.Networking.UnityWebRequest www = UnityEngine.Networking.UnityWebRequest.Get(srcPath);
        yield return www.SendWebRequest();
        if (www.result != UnityEngine.Networking.UnityWebRequest.Result.Success)
        {
            Debug.LogError("[CameraStreamer] Failed to load model from StreamingAssets: " + www.error);
            yield break;
        }
        System.IO.File.WriteAllBytes(dstPath, www.downloadHandler.data);
        #else
        System.IO.File.Copy(srcPath, dstPath, true);
        #endif

        Debug.Log("[CameraStreamer] Model copied to: " + dstPath);
        detectorReady = NativeDetectPlugin.InitializeDetector(dstPath);
        Debug.Log("[CameraStreamer] Detector initialized: " + detectorReady);
        yield break;
    }

    System.Collections.IEnumerator RequestAndStartCamera()
    {
        // Request camera permission if not already granted
        if (!Application.HasUserAuthorization(UserAuthorization.WebCam))
        {
            Debug.Log("Requesting camera permission...");
            yield return Application.RequestUserAuthorization(UserAuthorization.WebCam);
        }

        if (!Application.HasUserAuthorization(UserAuthorization.WebCam))
        {
            Debug.LogError("Camera permission denied. Cannot start camera stream.");
            yield break;
        }

        StartCameraStream();
    }

    void StartCameraStream()
    {
        // Get available cameras
        WebCamDevice[] devices = WebCamTexture.devices;
        if (devices.Length == 0)
        {
            Debug.LogError("No camera devices found!");
            ShowErrorOnRawImage("No camera found");
            return;
        }

        // Use the first available camera (usually back camera on mobile)
        string deviceName = devices[0].name;
        foreach (WebCamDevice device in devices)
        {
            if (!device.isFrontFacing)
            {
                deviceName = device.name;
                break;
            }
        }

        Debug.Log($"Using camera: {deviceName}");

        // Create WebCamTexture
        webCamTexture = new WebCamTexture(deviceName, requestedWidth, requestedHeight, requestedFPS);

        // Assign texture to RawImage
        if (cameraDisplay != null)
        {
            cameraDisplay.texture = webCamTexture;
        }
        else
        {
            Debug.LogError("Camera Display RawImage is not assigned!");
            return;
        }

        // Start the camera
        webCamTexture.Play();
        isCameraInitialized = true;

        StartCoroutine(CheckCameraStarted());
        Debug.Log($"Camera started - Resolution: {webCamTexture.width}x{webCamTexture.height}");
    }

    System.Collections.IEnumerator CheckCameraStarted()
    {
        float timeout = 5f;
        float timer = 0f;
        while (webCamTexture != null && !webCamTexture.didUpdateThisFrame && timer < timeout)
        {
            yield return null;
            timer += Time.deltaTime;
        }
        if (webCamTexture != null && !webCamTexture.didUpdateThisFrame)
        {
            Debug.LogError("Camera failed to start or no frames received.");
            ShowErrorOnRawImage("Camera failed to start");
        }
    }

    void ShowErrorOnRawImage(string message)
    {
        if (cameraDisplay != null)
        {
            cameraDisplay.color = Color.black;
            // Optionally, add a Text UI element for error messages
        }
    }

    void Update()
    {
        // Handle orientation and scaling
        if (isCameraInitialized && webCamTexture != null && webCamTexture.isPlaying)
        {
            AdjustCameraOrientation();
            AdjustRawImageSize();

            // --- Object Detection Integration ---
            if (detectorReady && webCamTexture.didUpdateThisFrame)
            {
                // Create Texture2D if needed
                if (tex == null || tex.width != webCamTexture.width || tex.height != webCamTexture.height)
                    tex = new Texture2D(webCamTexture.width, webCamTexture.height, TextureFormat.RGB24, false);

                tex.SetPixels(webCamTexture.GetPixels());
                tex.Apply();
                byte[] imageBytes = tex.GetRawTextureData();

                // Call detection plugin
                Debug.Log($"[CameraStreamer] Calling DetectObjects on frame {tex.width}x{tex.height}, bytes: {imageBytes.Length}");
                int detectedCount = NativeDetectPlugin.DetectObjects(imageBytes, tex.width, tex.height);
                Debug.Log($"[CameraStreamer] DetectObjects result: {detectedCount}");
                byte[] resultImage = NativeDetectPlugin.GetImageWithBoundingBoxes(imageBytes, tex.width, tex.height);
                Debug.Log($"[CameraStreamer] GetImageWithBoundingBoxes result: {(resultImage != null ? resultImage.Length.ToString() : "null")}");

                // Update display with bounding boxes
                if (resultImage != null && resultImage.Length == imageBytes.Length)
                {
                    tex.LoadRawTextureData(resultImage);
                    tex.Apply();
                    cameraDisplay.texture = tex;
                }
            }
        }
    }

    void AdjustCameraOrientation()
    {
        if (cameraDisplay == null || webCamTexture == null) return;

        // Get the camera rotation
        int rotationAngle = webCamTexture.videoRotationAngle;
        cameraDisplay.transform.localRotation = Quaternion.Euler(0, 0, -rotationAngle);

        // Adjust scale for front-facing cameras (mirror effect)
        if (webCamTexture.videoVerticallyMirrored)
        {
            cameraDisplay.transform.localScale = new Vector3(1, -1, 1);
        }
        else
        {
            cameraDisplay.transform.localScale = Vector3.one;
        }
    }

    void AdjustRawImageSize()
    {
        if (cameraDisplay == null || webCamTexture == null) return;
        RectTransform rt = cameraDisplay.rectTransform;

        // Get camera texture aspect ratio
        float camWidth = webCamTexture.width > 16 ? webCamTexture.width : requestedWidth;
        float camHeight = webCamTexture.height > 16 ? webCamTexture.height : requestedHeight;
        float camAspect = camWidth / camHeight;

        // Get screen aspect ratio
        float screenAspect = (float)Screen.width / Screen.height;

        // If camera is rotated 90/270, swap aspect
        int rot = webCamTexture.videoRotationAngle % 180;
        if (rot != 0)
        {
            camAspect = 1f / camAspect;
        }

        // Set anchor to stretch full
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.anchoredPosition = Vector2.zero;

        // Fit RawImage inside screen (letterbox, no crop)
        float scale = 1f;
        if (camAspect > screenAspect)
        {
            // Camera wider than screen: fit width, letterbox top/bottom
            scale = screenAspect / camAspect;
            rt.sizeDelta = new Vector2(0, 0);
            rt.localScale = new Vector3(1, scale, 1);
        }
        else
        {
            // Camera taller than screen: fit height, letterbox left/right
            scale = camAspect / screenAspect;
            rt.sizeDelta = new Vector2(0, 0);
            rt.localScale = new Vector3(scale, 1, 1);
        }
    }

    void OnApplicationPause(bool pauseStatus)
    {
        // Pause/Resume camera when app is paused/resumed
        if (webCamTexture != null)
        {
            if (pauseStatus)
            {
                webCamTexture.Pause();
                Debug.Log("Camera paused");
            }
            else
            {
                webCamTexture.Play();
                Debug.Log("Camera resumed");
            }
        }
    }

    void OnDestroy()
    {
        // Clean up camera resources
        if (webCamTexture != null)
        {
            webCamTexture.Stop();
            Destroy(webCamTexture);
            Debug.Log("Camera stopped and cleaned up");
        }
    }

    // Public method to switch between front and back camera
    public void SwitchCamera()
    {
        if (webCamTexture != null)
        {
            webCamTexture.Stop();
            Destroy(webCamTexture);
        }

        WebCamDevice[] devices = WebCamTexture.devices;
        if (devices.Length < 2) return;

        // Find the other camera
        string currentDevice = webCamTexture?.deviceName ?? "";
        string newDevice = "";
        
        foreach (WebCamDevice device in devices)
        {
            if (device.name != currentDevice)
            {
                newDevice = device.name;
                break;
            }
        }

        if (!string.IsNullOrEmpty(newDevice))
        {
            webCamTexture = new WebCamTexture(newDevice, requestedWidth, requestedHeight, requestedFPS);
            cameraDisplay.texture = webCamTexture;
            webCamTexture.Play();
            Debug.Log($"Switched to camera: {newDevice}");
        }
    }

    // Public method to get camera info (useful for debugging)
    public string GetCameraInfo()
    {
        if (webCamTexture == null) return "No camera active";
        
        return $"Camera: {webCamTexture.deviceName}\n" +
               $"Resolution: {webCamTexture.width}x{webCamTexture.height}\n" +
               $"FPS: {webCamTexture.requestedFPS}\n" +
               $"Rotation: {webCamTexture.videoRotationAngle}°\n" +
               $"Mirrored: {webCamTexture.videoVerticallyMirrored}";
    }
}