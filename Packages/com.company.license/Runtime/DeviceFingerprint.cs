using UnityEngine;

namespace Company.License
{
    // Cihazi backend'deki Device.fingerprint alanina baglamak icin kullanilan kimlik.
    // Quest/Pico (Android) uzerinde ANDROID_ID kullanilir; diger platformlarda
    // Unity'nin kendi cihaz kimligine dusulur.
    public static class DeviceFingerprint
    {
        private static string _cached;

        public static string Get()
        {
            if (!string.IsNullOrEmpty(_cached)) return _cached;

#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                using var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
                using var contentResolver = activity.Call<AndroidJavaObject>("getContentResolver");
                using var settingsSecure = new AndroidJavaClass("android.provider.Settings$Secure");
                string androidId = settingsSecure.CallStatic<string>("getString", contentResolver, "android_id");
                if (!string.IsNullOrEmpty(androidId))
                {
                    _cached = "android:" + androidId;
                    return _cached;
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[License] ANDROID_ID okunamadi, SystemInfo.deviceUniqueIdentifier'a dusuluyor: {e.Message}");
            }
#endif
            _cached = "device:" + SystemInfo.deviceUniqueIdentifier;
            return _cached;
        }
    }
}
