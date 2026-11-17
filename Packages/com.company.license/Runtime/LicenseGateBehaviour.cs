using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Events;

namespace Company.License
{
    [Serializable]
    public class LicenseReasonEvent : UnityEvent<string> { }

    // Sahneye eklenip Inspector'dan LicenseConfig atanacak tek bilesen.
    // Kilit ekrani icin iki secenek var: ya "Use Built In Lock Screen" acilir (hicbir
    // tasarim isi gerekmez), ya da OnLicenseLocked/OnLicenseValid event'lerine kendi
    // ekraninizi baglarsiniz.
    public class LicenseGateBehaviour : MonoBehaviour
    {
        public LicenseConfig config;

        [Tooltip("Acikken, lisans gecersizse koddan olusturulan basit bir uyari paneli gosterilir. " +
                 "Kendi kilit ekraninizi kullanacaksaniz kapatip event'lere baglanin.")]
        public bool useBuiltInLockScreen = true;

        [Tooltip("Lisans gecersizken kullaniciya gosterilecek mesaj.")]
        [TextArea(2, 4)]
        public string lockScreenMessage = "Bu uygulamanin lisansi gecerli degil.\nLutfen yoneticinizle iletisime gecin.";

        [Tooltip("Lisans gecersizken oyunu duraklat (zaman ve ses durur). Kapatilirsa kilit ekrani " +
                 "sadece bir uyari olur, oyun arkada oynanabilir kalir.")]
        public bool pauseGameWhenLocked = true;

        [Tooltip("Acikken, kilit ekraninda lisans anahtari uygulama icinden girilebilir " +
                 "(panelde tus takimi). Bu modda uygulama kendini KAPATMAZ, anahtar bekler.")]
        public bool allowKeyEntry = true;

        [Tooltip("Kilitlendikten kac saniye sonra uygulama kapansin (0 = kapanmasin). " +
                 "Yalnizca allowKeyEntry KAPALIYKEN gecerlidir.")]
        public float quitAfterSeconds = 8f;

        public UnityEvent OnLicenseValid;
        public UnityEvent OnLicenseLocked;
        public LicenseReasonEvent OnLicenseLockedWithReason;

        private LicenseClient _client;
        private SimpleLockScreen _lockScreen;

        [Serializable]
        private class ProvisioningFile
        {
            public string activationKey;
        }

        private void Awake()
        {
            _client = new LicenseClient(config);
        }

        // Admin cihazi kaldirdiginda / koltugu serbest biraktiginda / lisansi askiya aldiginda
        // (backend "license.recheck" gonderir) veya broker baglantiyi reddettiginde, 12 saatlik
        // heartbeat'i beklemeden lisansi hemen sunucuya sor. Sunucuya ulasilamazsa heartbeat
        // yerel token'a gore karar verir, yani bu yol yanlislikla KILITLEMEZ.
        private const float ServerRecheckMinIntervalSeconds = 10f;
        private const float BrokerRejectRecheckMinIntervalSeconds = 300f;
        // Backend mesaji DB degisikliginden HEMEN ONCE yollar; sormadan once DB'nin guncellenmesini bekle.
        private const float ServerRecheckDelaySeconds = 3f;
        private float _lastForcedHeartbeatAt = float.NegativeInfinity;

        private void OnEnable()
        {
            MqttCommandManager.LicenseCheckRequested += OnLicenseCheckRequested;
        }

        private void OnDisable()
        {
            MqttCommandManager.LicenseCheckRequested -= OnLicenseCheckRequested;
        }

        private void OnLicenseCheckRequested(string reason)
        {
            bool fromServer = reason == "server_request";
            float minInterval = fromServer ? ServerRecheckMinIntervalSeconds : BrokerRejectRecheckMinIntervalSeconds;
            if (Time.realtimeSinceStartup - _lastForcedHeartbeatAt < minInterval) return;
            _lastForcedHeartbeatAt = Time.realtimeSinceStartup;
            Debug.LogWarning($"[License] Lisans hemen kontrol ediliyor ({reason}).");
            StartCoroutine(ForcedHeartbeat(fromServer ? ServerRecheckDelaySeconds : 0f));
        }

        private IEnumerator ForcedHeartbeat(float delaySeconds)
        {
            if (delaySeconds > 0f) yield return new WaitForSecondsRealtime(delaySeconds);
            yield return RunHeartbeat();
        }

        private IEnumerator Start()
        {
            var lastKnown = _client.GetLastKnownStatus();

            if (lastKnown == LicenseStatus.Unknown)
            {
                // Ilk kurulum: aktivasyon sonucu gelene kadar oyunu oynatma.
                PauseGame();
                string provisionedKey = TryReadProvisionedActivationKey();
                if (string.IsNullOrEmpty(provisionedKey))
                {
                    Debug.LogWarning("[License] Aktivasyon anahtari bulunamadi (ilk kurulum). Kilitli baslatiliyor.");
                    Lock("not_activated");
                }
                else
                {
                    bool activationOk = false;
                    string activationError = null;
                    yield return _client.Activate(provisionedKey, (ok, error) =>
                    {
                        activationOk = ok;
                        activationError = error;
                    });

                    if (activationOk) Unlock();
                    else Lock(activationError);
                }
            }
            else if (lastKnown == LicenseStatus.Valid)
            {
                Unlock();
            }
            else
            {
                Lock("expired_or_tampered");
            }

            // Ilk karardan sonra sunucudan taze bir dogrulama al (internet varsa).
            yield return RunHeartbeat();

            while (true)
            {
                // Realtime: oyun duraklatildiginda (timeScale=0) heartbeat donmasin.
                yield return new WaitForSecondsRealtime(config.HeartbeatIntervalHours * 3600f);
                yield return RunHeartbeat();
            }
        }

        private IEnumerator RunHeartbeat()
        {
            yield return _client.Heartbeat((status, reason) =>
            {
                if (status == LicenseStatus.Valid) Unlock();
                else Lock(reason);
            });
        }

        // Event'ler sadece durum DEGISTIGINDE tetiklenir. Aksi halde acilista once
        // onbellekteki token'la, hemen ardindan heartbeat basarisiyla iki kez "gecerli"
        // sinyali gidip menu iki kez kurulur (gereksiz ag trafigi + gorunur yeniden cizim).
        private LicenseStatus _lastNotifiedStatus = LicenseStatus.Unknown;

        private void Unlock()
        {
            ResumeGame();
            if (_quitRoutine != null) { StopCoroutine(_quitRoutine); _quitRoutine = null; }

            // Uzaktan kontrol yalnizca lisans GECERLIYKEN acik. Idempotent: zaten calisiyorsa bir sey yapmaz.
            MqttCommandManager.EnsureRunning(config);

            if (_lastNotifiedStatus == LicenseStatus.Valid) return;
            _lastNotifiedStatus = LicenseStatus.Valid;
            if (_lockScreen != null) _lockScreen.Hide();
            OnLicenseValid?.Invoke();
        }

        private void Lock(string reason)
        {
            // Lisans gecersizse panelden kontrol de kapanir (broker da zaten reddederdi).
            MqttCommandManager.Shutdown();

            if (_lastNotifiedStatus == LicenseStatus.Locked) return;
            _lastNotifiedStatus = LicenseStatus.Locked;

            PauseGame();

            if (useBuiltInLockScreen)
            {
                if (_lockScreen == null) _lockScreen = SimpleLockScreen.Create();
                if (allowKeyEntry) _lockScreen.ShowActivation(EntryMessageFor(reason), OnKeySubmitted);
                else _lockScreen.Show(lockScreenMessage);
            }

            Debug.LogWarning($"[License] Kilitlendi: {reason}");
            OnLicenseLocked?.Invoke();
            OnLicenseLockedWithReason?.Invoke(reason ?? "unknown");

            // Anahtar girisi aciksa kapatmiyoruz: kullanici anahtar yazarken uygulama kapanmasin.
            if (!allowKeyEntry && quitAfterSeconds > 0f && _quitRoutine == null)
            {
                _quitRoutine = StartCoroutine(QuitAfterDelay());
            }
        }

        // --- Uygulama icinden anahtar girisi ---

        private bool _submitting;

        private void OnKeySubmitted(string key)
        {
            if (_submitting) return;
            StartCoroutine(SubmitKey(key));
        }

        private IEnumerator SubmitKey(string key)
        {
            _submitting = true;
            _lockScreen.SetStatus(LicenseText.T("Wird geprüft...", "Dogrulaniyor...", "Checking..."), true);

            bool ok = false;
            string error = null;
            yield return _client.Activate(key, (success, err) => { ok = success; error = err; });

            _submitting = false;
            if (ok)
            {
                Debug.Log("[License] Uygulama icinden aktivasyon basarili.");
                Unlock();
            }
            else
            {
                Debug.LogWarning($"[License] Uygulama icinden aktivasyon basarisiz: {error}");
                _lockScreen.SetStatus(ErrorMessageFor(error), false);
            }
        }

        private static string EntryMessageFor(string reason)
        {
            switch (reason)
            {
                case "not_activated":
                    return LicenseText.T("Dieses Gerät ist noch nicht aktiviert.\nBitte geben Sie Ihren Lizenzschlüssel ein.",
                                         "Bu cihaz henuz aktive edilmedi.\nLisans anahtarinizi girin.",
                                         "This device is not activated yet.\nPlease enter your license key.");
                case "license_expired":
                    return LicenseText.T("Die Lizenz ist abgelaufen.\nSie können einen neuen Schlüssel eingeben.",
                                         "Lisans suresi dolmus.\nYeni bir anahtar girebilirsiniz.",
                                         "The license has expired.\nYou can enter a new key.");
                case "license_suspended":
                    return LicenseText.T("Die Lizenz ist gesperrt.\nBitte wenden Sie sich an ExposiaVR oder geben Sie einen neuen Schlüssel ein.",
                                         "Lisans askiya alinmis.\nYoneticinizle iletisime gecin veya yeni anahtar girin.",
                                         "The license is suspended.\nPlease contact ExposiaVR or enter a new key.");
                case "seat_revoked":
                    return LicenseText.T("Die Lizenz wurde von diesem Gerät entfernt.\nSie können einen neuen Schlüssel eingeben.",
                                         "Bu cihazin lisansi kaldirilmis.\nYeni bir anahtar girebilirsiniz.",
                                         "The license was removed from this device.\nYou can enter a new key.");
                default:
                    return LicenseText.T("Die Lizenz ist nicht gültig.\nBitte geben Sie Ihren Lizenzschlüssel ein.",
                                         "Lisans gecerli degil.\nLisans anahtarinizi girin.",
                                         "The license is not valid.\nPlease enter your license key.");
            }
        }

        private static string ErrorMessageFor(string error)
        {
            switch (error)
            {
                case "invalid_activation_key": return LicenseText.T("Schlüssel ungültig. Bitte prüfen und erneut versuchen.", "Anahtar gecersiz. Kontrol edip tekrar deneyin.", "Invalid key. Please check it and try again.");
                case "product_not_licensed": return LicenseText.T("Dieser Schlüssel gilt nicht für diese App.", "Bu anahtarin bu uygulama icin lisansi yok.", "This key is not licensed for this app.");
                case "seat_limit_reached": return LicenseText.T("Gerätelimit erreicht. Bitte wenden Sie sich an ExposiaVR.", "Cihaz limiti dolu. Yoneticinizle gorusun.", "Device limit reached. Please contact ExposiaVR.");
                case "license_expired": return LicenseText.T("Die Lizenz dieses Schlüssels ist abgelaufen.", "Bu anahtarin lisans suresi dolmus.", "This key's license has expired.");
                case "license_suspended": return LicenseText.T("Diese Lizenz ist gesperrt.", "Bu lisans askiya alinmis.", "This license is suspended.");
                case "fingerprint_belongs_to_another_customer": return LicenseText.T("Dieses Gerät ist für einen anderen Kunden registriert.", "Bu cihaz baska bir musteriye kayitli.", "This device is registered to another customer.");
                case "too_many_requests": return LicenseText.T("Zu viele Versuche. Bitte in 15 Minuten erneut versuchen.", "Cok fazla deneme. 15 dakika sonra tekrar deneyin.", "Too many attempts. Please try again in 15 minutes.");
                case "unknown_product": return LicenseText.T("Die App ist auf dem Server nicht eingerichtet. Bitte wenden Sie sich an ExposiaVR.", "Uygulama sunucuda tanimli degil. Yoneticinizle gorusun.", "This app is not set up on the server. Please contact ExposiaVR.");
                default: return LicenseText.T("Server nicht erreichbar. Bitte Internetverbindung prüfen.", "Sunucuya ulasilamadi. Internet baglantisini kontrol edin.", "Cannot reach the server. Please check the internet connection.");
            }
        }

        // --- Sert yaptirim: duraklat + kapat ---

        private Coroutine _quitRoutine;
        private bool _paused;
        private float _timeScaleBeforePause = 1f;

        private void PauseGame()
        {
            if (!pauseGameWhenLocked || _paused) return;
            _paused = true;
            _timeScaleBeforePause = Time.timeScale > 0f ? Time.timeScale : 1f;
            Time.timeScale = 0f;
            AudioListener.pause = true;
        }

        private void ResumeGame()
        {
            if (!_paused) return;
            _paused = false;
            Time.timeScale = _timeScaleBeforePause;
            AudioListener.pause = false;
        }

        private IEnumerator QuitAfterDelay()
        {
            // Realtime: timeScale=0 iken de sayar.
            yield return new WaitForSecondsRealtime(quitAfterSeconds);
            Debug.LogWarning("[License] Lisans gecersiz -- uygulama kapatiliyor.");
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private string TryReadProvisionedActivationKey()
        {
            try
            {
                string path = Path.Combine(Application.persistentDataPath, config.ProvisioningFileName);
                if (!File.Exists(path)) return null;
                var data = JsonUtility.FromJson<ProvisioningFile>(File.ReadAllText(path));
                return data?.activationKey;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[License] Provisioning dosyasi okunamadi: {e.Message}");
                return null;
            }
        }
    }
}
