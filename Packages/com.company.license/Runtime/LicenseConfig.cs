using UnityEngine;

namespace Company.License
{
    // Her proje kendi ProductCode'unu ve backend URL'sini burada tanimlar.
    // PublicKeyBase64, backend'de "npm run keys:generate" ile uretilen
    // license_ed25519_public.pem dosyasinin SPKI govdesinin base64'u (PEM basliklari haric).
    [CreateAssetMenu(menuName = "License/License Config", fileName = "LicenseConfig")]
    public class LicenseConfig : ScriptableObject
    {
        [Tooltip("Ornek: https://license.sirketiniz.com")]
        public string ApiBaseUrl = "https://license.example.com";

        [Tooltip("Bu uygulamanin backend'deki urun kodu (ornek: DOG_MIXED_REALITY). " +
                 "Her musteride ayni kalir; aktivasyonda uygulama kendini bununla tanitir.")]
        public string ProductCode = "";

        [Tooltip("Backend'deki Ed25519 public key'in SPKI DER govdesi, base64 (PEM basliklari/satir sonlari olmadan).")]
        [TextArea(2, 4)]
        public string PublicKeyBase64 = "";

        [Tooltip("Heartbeat cagrisi arasindaki sure (saat). Sunucu offline grace suresini token'in expiresAt alaniyla zaten belirliyor; bu sadece ne siklikta yenileme denenecegini kontrol eder.")]
        public float HeartbeatIntervalHours = 12f;

        [Tooltip("ManageXR uzerinden cihaza push edilen ilk aktivasyon dosyasinin cihaz uzerindeki yolu (Application.persistentDataPath'e gore veya mutlak).")]
        public string ProvisioningFileName = "activation.json";

        [Header("Uzaktan Kontrol (MQTT)")]
        [Tooltip("Acikken, lisans gecerli oldugu surece uygulama MQTT broker'a baglanir ve panelden " +
                 "gelen komutlari (MqttCommandManager.RegisterHandler ile kaydedilenler) calistirir.")]
        public bool EnableRemoteControl = true;

        [Tooltip("Broker adresi. Production: wss://api.exposiavr.com/mqtt  (yerel test: ws://localhost:8080/mqtt)")]
        public string MqttUrl = "wss://api.exposiavr.com/mqtt";

        [Tooltip("Panelde cihaz durumunun (masa konumu, ses vb.) ne siklikla guncellenecegi (saniye). " +
                 "Backend 45 sn sinyal alamazsa uygulamayi cevrimdisi sayar; bunu 30'un ustune cikarmayin.")]
        [Range(2f, 30f)]
        public float TelemetryIntervalSeconds = 10f;
    }
}
