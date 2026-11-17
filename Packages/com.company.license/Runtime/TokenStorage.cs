using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace Company.License
{
    // Son alinan token'i cihazda saklar. Buradaki sifreleme rastgele kullanicilarin
    // dosyayi text editorde acip degistirmesini zorlastiran bir katmandir -- asil
    // guvenlik siniri LicenseSignatureVerifier'daki Ed25519 imza kontroludur (dosyanin
    // icerigi degistirilse bile imza gecersiz kalir).
    internal static class TokenStorage
    {
        private const string FileName = "license_cache.dat";

        private static string FilePath => Path.Combine(Application.persistentDataPath, FileName);

        public static void Save(StoredLicenseToken data)
        {
            string json = JsonUtility.ToJson(data);
            byte[] encrypted = Encrypt(json);
            File.WriteAllBytes(FilePath, encrypted);
        }

        public static StoredLicenseToken Load()
        {
            try
            {
                if (!File.Exists(FilePath)) return null;
                byte[] encrypted = File.ReadAllBytes(FilePath);
                string json = Decrypt(encrypted);
                return JsonUtility.FromJson<StoredLicenseToken>(json);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[License] Yerel token okunamadi: {e.Message}");
                return null;
            }
        }

        public static void Clear()
        {
            if (File.Exists(FilePath)) File.Delete(FilePath);
        }

        private static byte[] DeriveKey()
        {
            // Cihaza ozgu bir anahtar -- token dosyasinin baska bir cihaza kopyalanip
            // dogrudan okunmasini da zorlastirir.
            using var sha256 = SHA256.Create();
            string material = "company-license-v1:" + DeviceFingerprint.Get();
            return sha256.ComputeHash(Encoding.UTF8.GetBytes(material));
        }

        private static byte[] Encrypt(string plainText)
        {
            using var aes = Aes.Create();
            aes.Key = DeriveKey();
            aes.GenerateIV();
            using var encryptor = aes.CreateEncryptor();
            byte[] plainBytes = Encoding.UTF8.GetBytes(plainText);
            byte[] cipherBytes = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);

            byte[] result = new byte[aes.IV.Length + cipherBytes.Length];
            Buffer.BlockCopy(aes.IV, 0, result, 0, aes.IV.Length);
            Buffer.BlockCopy(cipherBytes, 0, result, aes.IV.Length, cipherBytes.Length);
            return result;
        }

        private static string Decrypt(byte[] data)
        {
            using var aes = Aes.Create();
            aes.Key = DeriveKey();
            int ivLength = aes.BlockSize / 8;

            byte[] iv = new byte[ivLength];
            Buffer.BlockCopy(data, 0, iv, 0, ivLength);
            aes.IV = iv;

            using var decryptor = aes.CreateDecryptor();
            byte[] cipherBytes = new byte[data.Length - ivLength];
            Buffer.BlockCopy(data, ivLength, cipherBytes, 0, cipherBytes.Length);
            byte[] plainBytes = decryptor.TransformFinalBlock(cipherBytes, 0, cipherBytes.Length);
            return Encoding.UTF8.GetString(plainBytes);
        }
    }
}
