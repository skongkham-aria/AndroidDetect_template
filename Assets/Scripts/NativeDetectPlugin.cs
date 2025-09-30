using System;
using UnityEngine;

public static class NativeDetectPlugin
{
#if UNITY_ANDROID && !UNITY_EDITOR
    private static AndroidJavaObject nativeLib;

    static NativeDetectPlugin()
    {
        using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
        {
            var context = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
            nativeLib = new AndroidJavaObject("com.example.mynativedetectlib.NativeLib");
        }
    }

    public static bool InitializeDetector(string modelPath)
    {
        return nativeLib.Call<bool>("initializeDetector", modelPath);
    }

    public static int DetectObjects(byte[] imageBytes, int width, int height)
    {
        return nativeLib.Call<int>("detectObjects", imageBytes, width, height);
    }

    public static byte[] GetImageWithBoundingBoxes(byte[] imageBytes, int width, int height)
    {
        return nativeLib.Call<byte[]>("getImageWithBoundingBoxes", imageBytes, width, height);
    }
#else
    // Editor stub implementations
    public static bool InitializeDetector(string modelPath) => false;
    public static int DetectObjects(byte[] imageBytes, int width, int height) => 0;
    public static byte[] GetImageWithBoundingBoxes(byte[] imageBytes, int width, int height) => null;
#endif
}
