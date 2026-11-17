using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace Company.License
{
    // Backend'deki /activate ve /validate endpoint'lerine karsi calisan istemci.
    // Coroutine tabanlidir; her projeye (farkli async/await tercihleri olsa bile) sorunsuz gomulebilsin diye.
    public class LicenseClient
    {
        private readonly LicenseConfig _config;

        public LicenseClient(LicenseConfig config)
        {
            _config = config;
        }

        // Ilk kurulumda bir kere cagrilir (ManageXR ile pushlanan aktivasyon anahtariyla).
        public IEnumerator Activate(string activationKey, Action<bool, string> onComplete)
        {
            if (string.IsNullOrEmpty(_config.ProductCode))
            {
                Debug.LogError("[License] LicenseConfig.ProductCode bos -- aktivasyon yapilamaz.");
                onComplete(false, "missing_product_code");
                yield break;
            }

            var body = new ActivateRequest
            {
                activationKey = activationKey,
                productCode = _config.ProductCode,
                fingerprint = DeviceFingerprint.Get(),
                managexrDeviceId = SystemInfo.deviceUniqueIdentifier,
                label = SystemInfo.deviceModel,
            };

            yield return Post<ActivateResponse>("/activate", body, (success, response, error) =>
            {
                if (success && response != null && !string.IsNullOrEmpty(response.token))
                {
                    TokenStorage.Save(new StoredLicenseToken
                    {
                        activationKey = activationKey,
                        token = response.token,
                        expiresAt = response.expiresAt,
                    });
                    onComplete(true, null);
                }
                else
                {
                    onComplete(false, response?.error ?? error ?? "unknown_error");
                }
            });
        }

        // Periyodik heartbeat: basariliysa token'i yeniler. Sunucuya ulasilamazsa
        // yerel token'in (imza + sure) hala gecerli olup olmadigina gore karar verir (offline grace).
        public IEnumerator Heartbeat(Action<LicenseStatus, string> onComplete)
        {
            var stored = TokenStorage.Load();
            if (stored == null || string.IsNullOrEmpty(stored.token))
            {
                onComplete(LicenseStatus.Locked, "not_activated");
                yield break;
            }

            // productCode ZORUNLU: sunucu, token'in bu uygulamanin urunune ait oldugunu dogrular.
            // Gonderilmezse sunucu 400 doner ve cihaz kilitlenir.
            var body = new ValidateRequest
            {
                token = stored.token,
                productCode = _config.ProductCode,
                appVersion = Application.version,
            };
            LicenseStatus resultStatus = LicenseStatus.Locked;
            string resultReason = "unknown_error";

            // Not: Post(...) IEnumerator'i tamamlanana kadar onComplete zaten senkron olarak
            // cagrilmis olur, bu yuzden buradan sonra resultStatus/resultReason kullanima hazirdir.
            yield return Post<ValidateResponse>("/validate", body, (success, response, error) =>
            {
                if (success && response != null && !string.IsNullOrEmpty(response.token))
                {
                    TokenStorage.Save(new StoredLicenseToken
                    {
                        activationKey = stored.activationKey,
                        token = response.token,
                        expiresAt = response.expiresAt,
                    });
                    resultStatus = LicenseStatus.Valid;
                    resultReason = "ok";
                }
                else if (response?.error != null)
                {
                    // Sunucuya ulasildi ama reddetti (suspended/expired/seat_revoked) -- kesin kilitle.
                    TokenStorage.Clear();
                    resultStatus = LicenseStatus.Locked;
                    resultReason = response.error;
                }
                else
                {
                    // Sunucuya ulasilamadi (offline). Yerel token'a gore karar ver.
                    resultStatus = EvaluateLocalToken(stored);
                    resultReason = resultStatus == LicenseStatus.Valid ? "offline_grace" : "offline_expired";
                }
            });

            onComplete(resultStatus, resultReason);
        }

        // Sadece "HUB" urunune aktive olmus cihazlarda cagrilir: musterinin lisansli oldugu
        // (ve Hub'in baslatabilmesi icin androidPackageName'i tanimli) icerik urunlerini doner.
        public IEnumerator GetLicensedProducts(Action<bool, LicensedProduct[]> onComplete)
        {
            var stored = TokenStorage.Load();
            if (stored == null || string.IsNullOrEmpty(stored.token))
            {
                onComplete(false, null);
                yield break;
            }

            var body = new MyProductsRequest { token = stored.token };
            bool ok = false;
            LicensedProduct[] products = null;

            yield return Post<MyProductsResponse>("/my-products", body, (success, response, error) =>
            {
                ok = success && response?.products != null;
                products = response?.products;
            });

            onComplete(ok, products);
        }

        // Ag cagrisi yapmadan, sadece yerel onbellege bakarak anlik durumu dondurur.
        // Uygulama acilisinda ilk heartbeat sonucunu beklemeden ekrani kilitleyip kilitlememeyi belirlemek icin kullanilir.
        public LicenseStatus GetLastKnownStatus()
        {
            var stored = TokenStorage.Load();
            if (stored == null) return LicenseStatus.Unknown;
            return EvaluateLocalToken(stored);
        }

        // Cihaz saatinin geriye alinmasina karsi tolerans (NTP kaymasi, zaman dilimi vb.).
        private static readonly TimeSpan ClockSkewTolerance = TimeSpan.FromHours(6);

        private LicenseStatus EvaluateLocalToken(StoredLicenseToken stored)
        {
            // expiresAt kasitli olarak stored.expiresAt (imzasiz yerel alan) yerine
            // imzasi az once dogrulanan payload'dan okunuyor -- aksi halde biri sadece
            // yerel dosyadaki duz metin expiresAt alanini degistirerek sureyi uzatabilirdi.
            if (!LicenseSignatureVerifier.TryVerifyAndDecode(stored.token, _config.PublicKeyBase64, out var payload))
            {
                Debug.LogWarning("[License] Yerel token imzasi gecersiz -- kilitleniyor.");
                return LicenseStatus.Locked;
            }

            if (!TryParseUtc(payload.expiresAt, out DateTime expiresAt)) return LicenseStatus.Locked;
            if (!TryParseUtc(payload.issuedAt, out DateTime issuedAt)) return LicenseStatus.Locked;

            // PRODUCT CODE CHECK (Kopya lisans dosyasina karsi koruma)
            // Baska bir uygulamadan kopyalanmis bir lisans ise, imzasi dogru olsa da productCode farkli olacagi icin reddedilir.
            if (string.IsNullOrEmpty(payload.productCode) || payload.productCode != _config.ProductCode)
            {
                Debug.LogWarning($"[License] Urun kodu uyusmazligi! Token kodu: {payload.productCode}, Beklenen: {_config.ProductCode}. Baska bir uygulamanin lisansi kopyalanmis olabilir.");
                return LicenseStatus.Locked;
            }

            DateTime now = DateTime.UtcNow;

            // SAAT MANIPULASYONU KORUMASI:
            // issuedAt sunucu tarafindan imzalandigi icin degistirilemez. Cihaz saati
            // token'in uretildigi andan DAHA ERKEN bir zamani gosteriyorsa, saat geriye
            // alinmis demektir (mesru bir senaryoda bu imkansiz). Bu durumda offline
            // dogrulamaya guvenmiyoruz ve sunucudan taze onay talep ediyoruz.
            if (now < issuedAt - ClockSkewTolerance)
            {
                Debug.LogWarning(
                    $"[License] Cihaz saati token uretim zamanindan geride (now={now:o}, issuedAt={issuedAt:o}) " +
                    "-- saat manipulasyonu suphesi, offline dogrulama reddedildi.");
                return LicenseStatus.Locked;
            }

            return now <= expiresAt ? LicenseStatus.Valid : LicenseStatus.Locked;
        }

        private static bool TryParseUtc(string value, out DateTime utc)
        {
            utc = default;
            if (string.IsNullOrEmpty(value)) return false;
            if (!DateTime.TryParse(value, null, System.Globalization.DateTimeStyles.RoundtripKind, out var parsed)) return false;
            utc = parsed.ToUniversalTime();
            return true;
        }

        private IEnumerator Post<TResponse>(string path, object body, Action<bool, TResponse, string> onComplete) where TResponse : class
        {
            string json = JsonUtility.ToJson(body);
            byte[] bodyBytes = Encoding.UTF8.GetBytes(json);

            using var request = new UnityWebRequest(_config.ApiBaseUrl.TrimEnd('/') + path, "POST");
            request.uploadHandler = new UploadHandlerRaw(bodyBytes);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");

            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success && string.IsNullOrEmpty(request.downloadHandler.text))
            {
                // Ag hatasi (sunucuya hic ulasilamadi) -- response null donuyoruz, cagiran yeri offline olarak yorumlar.
                onComplete(false, null, request.error);
                yield break;
            }

            TResponse parsed = null;
            try
            {
                parsed = JsonUtility.FromJson<TResponse>(request.downloadHandler.text);
            }
            catch (Exception e)
            {
                onComplete(false, null, $"parse_error: {e.Message}");
                yield break;
            }

            bool httpSuccess = request.result == UnityWebRequest.Result.Success;
            onComplete(httpSuccess, parsed, httpSuccess ? null : request.error);
        }
    }
}
