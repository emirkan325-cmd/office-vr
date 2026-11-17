using UnityEngine;

namespace Company.License
{
    // Hub'in bir icerik uygulamasini (ayri bir APK) baslatmasi icin kullanilir.
    // Android'de "baska bir uygulamayi ac" islemi standart bir Intent cagrisidir,
    // ekstra bir native plugin gerektirmez.
    public static class AppLauncher
    {
        public static bool LaunchPackage(string androidPackageName)
        {
            if (string.IsNullOrEmpty(androidPackageName))
            {
                Debug.LogError("[License] AppLauncher: bos paket adi.");
                return false;
            }

#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                using var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
                using var packageManager = activity.Call<AndroidJavaObject>("getPackageManager");
                using var launchIntent = packageManager.Call<AndroidJavaObject>("getLaunchIntentForPackage", androidPackageName);

                if (launchIntent == null)
                {
                    Debug.LogError($"[License] AppLauncher: '{androidPackageName}' cihazda kurulu degil veya baslatma intent'i yok.");
                    return false;
                }

                activity.Call("startActivity", launchIntent);
                return true;
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[License] AppLauncher: '{androidPackageName}' baslatilamadi: {e.Message}");
                return false;
            }
#else
            Debug.LogWarning($"[License] AppLauncher: sadece Android'de calisir (Editor/diger platformlarda no-op). Hedef: {androidPackageName}");
            return false;
#endif
        }
    }
}
