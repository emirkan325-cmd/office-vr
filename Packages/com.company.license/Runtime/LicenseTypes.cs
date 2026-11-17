using System;

namespace Company.License
{
    public enum LicenseStatus
    {
        Unknown,
        Valid,
        Locked,
    }

    // Backend'in imzaladigi payload ile bire bir eslesir (bkz. backend/src/lib/tokenCrypto.ts).
    [Serializable]
    public class LicenseTokenPayload
    {
        public string activationId;
        public string deviceId;
        public string customerId;
        public string productId;
        public string productCode;
        public string licenseId;
        public string issuedAt;
        public string expiresAt;
    }

    [Serializable]
    public class StoredLicenseToken
    {
        public string activationKey;
        public string token;
        public string expiresAt;
    }

    [Serializable]
    internal class ActivateRequest
    {
        public string activationKey;
        public string productCode;
        public string fingerprint;
        public string managexrDeviceId;
        public string label;
    }

    [Serializable]
    internal class ValidateRequest
    {
        public string token;
        public string productCode;
        public string appVersion;
    }

    [Serializable]
    internal class ActivateResponse
    {
        public string token;
        public string expiresAt;
        public string error;
    }

    [Serializable]
    internal class ValidateResponse
    {
        public string token;
        public string expiresAt;
        public string error;
    }

    // Sadece "HUB" urunune aktive olmus cihazlarda anlamlidir -- bkz. LicenseClient.GetLicensedProducts.
    [Serializable]
    public class LicensedProduct
    {
        public string code;
        public string name;
        public string androidPackageName;
        public string coverImageUrl;
        public string expiresAt;
    }

    [Serializable]
    internal class MyProductsRequest
    {
        public string token;
    }

    [Serializable]
    internal class MyProductsResponse
    {
        public LicensedProduct[] products;
        public string error;
    }
}
