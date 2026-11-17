using System;
using System.Text;
using UnityEngine;

namespace Company.License
{
    // Token imzasini (Ed25519) dogrular. Bu, cihaz internete bagli degilken yerel
    // onbellegin elle degistirilip suresinin uzatilmasina karsi asil savunmadir --
    // internet varken ayni kontrolu backend her /validate cagrisinda sunucu tarafinda yapiyor.
    //
    // Dogrulama Ed25519Verify ile yapiliyor (saf C#, harici bagimlilik yok).
    // Uygulamanin dogrulugu, sunucunun gercek anahtariyla uretilmis test
    // vektorleriyle dogrulanmistir.
    public static class LicenseSignatureVerifier
    {
        public static bool Verify(string token, string publicKeyBase64)
        {
            return TryVerifyAndDecode(token, publicKeyBase64, out _);
        }

        // Imzayi dogrular VE payload'i sadece dogrulama basariliysa doner.
        // Onemli: expiresAt gibi alanlar HER ZAMAN buradan (imzalanmis payload'dan) okunmali,
        // ayri/imzasiz bir yerel alandan degil -- aksi halde imzasiz alan degistirilerek
        // sure sinirsizca uzatilabilir.
        public static bool TryVerifyAndDecode(string token, string publicKeyBase64, out LicenseTokenPayload payload)
        {
            payload = null;
            if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(publicKeyBase64)) return false;

            var parts = token.Split('.');
            if (parts.Length != 2) return false;

            byte[] payloadBytes;
            byte[] signatureBytes;
            byte[] publicKeyBytes;
            try
            {
                payloadBytes = FromBase64Url(parts[0]);
                signatureBytes = FromBase64Url(parts[1]);
                publicKeyBytes = ParsePublicKey(publicKeyBase64);
            }
            catch (Exception)
            {
                return false;
            }

            if (publicKeyBytes == null || publicKeyBytes.Length != 32)
            {
                Debug.LogError("[License] Public key 32 byte'lik RAW Ed25519 anahtari olmali (PEM govdesi de kabul edilir).");
                return false;
            }

            if (!Ed25519Verify.Verify(signatureBytes, payloadBytes, publicKeyBytes))
            {
                return false;
            }

            try
            {
                payload = JsonUtility.FromJson<LicenseTokenPayload>(Encoding.UTF8.GetString(payloadBytes));
                return payload != null && !string.IsNullOrEmpty(payload.expiresAt);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[License] Token payload'i ayristirilamadi: {e.Message}");
                return false;
            }
        }

        // Hem 32 byte'lik RAW base64'u hem de PEM/SPKI formatini kabul eder --
        // sunucu iki formati da uretebildigi icin yanlis formatin sessiz hataya
        // yol acmasini engelliyoruz.
        private static byte[] ParsePublicKey(string input)
        {
            string cleaned = input
                .Replace("-----BEGIN PUBLIC KEY-----", string.Empty)
                .Replace("-----END PUBLIC KEY-----", string.Empty)
                .Replace("\r", string.Empty)
                .Replace("\n", string.Empty)
                .Trim();

            byte[] decoded = Convert.FromBase64String(cleaned);
            if (decoded.Length == 32) return decoded;

            // SPKI DER: Ed25519 icin sabit 12 byte'lik ASN.1 basligi + 32 byte anahtar (RFC 8410).
            if (decoded.Length == 44)
            {
                var raw = new byte[32];
                Buffer.BlockCopy(decoded, decoded.Length - 32, raw, 0, 32);
                return raw;
            }

            return null;
        }

        private static byte[] FromBase64Url(string input)
        {
            string base64 = input.Replace('-', '+').Replace('_', '/');
            switch (base64.Length % 4)
            {
                case 2: base64 += "=="; break;
                case 3: base64 += "="; break;
            }
            return Convert.FromBase64String(base64);
        }
    }
}
