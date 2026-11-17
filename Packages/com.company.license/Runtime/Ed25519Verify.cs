using System;
using System.Numerics;
using System.Security.Cryptography;

namespace Company.License
{
    // Ed25519 imza DOGRULAMA (yalnizca verify; imzalama sunucuda).
    //
    // Neden kendi uygulamamiz: Unity/IL2CPP'de Ed25519 hazir gelmiyor, Android'in
    // kendi saglayicisi da Ed25519 icin KeyFactory sunmadigindan public key'i
    // yukleyemiyoruz. Ucuncu parti native eklenti de her platform icin ayri binary
    // gerektiriyor.
    //
    // Neden guvenli: dogrulama islemi GIZLI VERI ICERMEZ (sadece public key, imza ve
    // mesaj). Bu yuzden yan-kanal/sabit-zaman gereksinimi yoktur ve BigInteger ile
    // sade, okunabilir aritmetik kullanmak mesrudur. Dogruluk, sunucunun gercek
    // anahtariyla uretilmis test vektorleriyle dogrulanmistir (Ed25519VerifyTests).
    public static class Ed25519Verify
    {
        // p = 2^255 - 19
        private static readonly BigInteger P =
            BigInteger.Pow(2, 255) - 19;

        // Grup mertebesi L = 2^252 + 27742317777372353535851937790883648493
        private static readonly BigInteger L =
            BigInteger.Pow(2, 252) + BigInteger.Parse("27742317777372353535851937790883648493");

        // d = -121665 / 121666 (mod p)
        private static readonly BigInteger D =
            Mod(-121665 * ModInverse(121666, BigInteger.Pow(2, 255) - 19), BigInteger.Pow(2, 255) - 19);

        // sqrt(-1) mod p = 2^((p-1)/4)
        private static readonly BigInteger SqrtM1 =
            BigInteger.ModPow(2, (BigInteger.Pow(2, 255) - 19 - 1) / 4, BigInteger.Pow(2, 255) - 19);

        // Taban nokta B (extended koordinatlarda hesaplanir)
        private static readonly BigInteger ByBase =
            Mod(4 * ModInverse(5, BigInteger.Pow(2, 255) - 19), BigInteger.Pow(2, 255) - 19);

        private struct Point
        {
            public BigInteger X, Y, Z, T;
        }

        private static Point BasePoint()
        {
            BigInteger y = ByBase;
            if (!TryRecoverX(y, 0, out BigInteger x)) throw new InvalidOperationException("taban nokta cozulemedi");
            return new Point { X = x, Y = y, Z = BigInteger.One, T = Mod(x * y, P) };
        }

        /// <summary>
        /// signature (64 byte), message ve publicKey (32 byte RAW Ed25519) ile imzayi dogrular.
        /// </summary>
        public static bool Verify(byte[] signature, byte[] message, byte[] publicKey)
        {
            if (signature == null || message == null || publicKey == null) return false;
            if (signature.Length != 64 || publicKey.Length != 32) return false;

            try
            {
                byte[] rBytes = new byte[32];
                byte[] sBytes = new byte[32];
                Buffer.BlockCopy(signature, 0, rBytes, 0, 32);
                Buffer.BlockCopy(signature, 32, sBytes, 0, 32);

                // S, [0, L) araliginda olmali (malleability'i engeller).
                BigInteger s = LittleEndianToBigInteger(sBytes);
                if (s >= L) return false;

                if (!TryDecodePoint(publicKey, out Point a)) return false;
                if (!TryDecodePoint(rBytes, out Point r)) return false;

                // k = SHA512(R || A || M) mod L
                byte[] hashInput = new byte[64 + message.Length];
                Buffer.BlockCopy(rBytes, 0, hashInput, 0, 32);
                Buffer.BlockCopy(publicKey, 0, hashInput, 32, 32);
                Buffer.BlockCopy(message, 0, hashInput, 64, message.Length);

                byte[] hash;
                using (var sha512 = SHA512.Create())
                {
                    hash = sha512.ComputeHash(hashInput);
                }
                BigInteger k = Mod(LittleEndianToBigInteger(hash), L);

                // Kontrol: [S]B == R + [k]A
                Point left = ScalarMultiply(BasePoint(), s);
                Point right = Add(r, ScalarMultiply(a, k));
                return AreEqual(left, right);
            }
            catch (Exception)
            {
                // Bozuk girdi = gecersiz imza.
                return false;
            }
        }

        // --- Nokta kodlama/cozme ---

        private static bool TryDecodePoint(byte[] encoded, out Point point)
        {
            point = default;

            byte[] yBytes = new byte[32];
            Buffer.BlockCopy(encoded, 0, yBytes, 0, 32);
            int xSign = (yBytes[31] >> 7) & 1;
            yBytes[31] &= 0x7F;

            BigInteger y = LittleEndianToBigInteger(yBytes);
            if (y >= P) return false;

            if (!TryRecoverX(y, xSign, out BigInteger x)) return false;

            point = new Point { X = x, Y = y, Z = BigInteger.One, T = Mod(x * y, P) };
            return true;
        }

        // x^2 = (y^2 - 1) / (d*y^2 + 1)
        private static bool TryRecoverX(BigInteger y, int xSign, out BigInteger x)
        {
            x = BigInteger.Zero;

            BigInteger y2 = Mod(y * y, P);
            BigInteger numerator = Mod(y2 - 1, P);
            BigInteger denominator = Mod(D * y2 + 1, P);
            if (denominator.IsZero) return false;

            BigInteger x2 = Mod(numerator * ModInverse(denominator, P), P);
            if (x2.IsZero)
            {
                // x = 0: yalnizca isaret 0 ise gecerli.
                return xSign == 0;
            }

            // Karekok denemesi: x2^((p+3)/8)
            BigInteger candidate = BigInteger.ModPow(x2, (P + 3) / 8, P);
            if (!Mod(candidate * candidate, P).Equals(x2))
            {
                candidate = Mod(candidate * SqrtM1, P);
                if (!Mod(candidate * candidate, P).Equals(x2)) return false;
            }

            if ((int)(candidate & 1) != xSign)
            {
                candidate = Mod(P - candidate, P);
            }

            x = candidate;
            return true;
        }

        // --- Grup islemleri (extended koordinatlar, a = -1) ---

        private static Point Add(Point p1, Point p2)
        {
            BigInteger a = Mod((p1.Y - p1.X) * (p2.Y - p2.X), P);
            BigInteger b = Mod((p1.Y + p1.X) * (p2.Y + p2.X), P);
            BigInteger c = Mod(p1.T * 2 * D * p2.T, P);
            BigInteger d = Mod(p1.Z * 2 * p2.Z, P);
            BigInteger e = Mod(b - a, P);
            BigInteger f = Mod(d - c, P);
            BigInteger g = Mod(d + c, P);
            BigInteger h = Mod(b + a, P);

            return new Point
            {
                X = Mod(e * f, P),
                Y = Mod(g * h, P),
                T = Mod(e * h, P),
                Z = Mod(f * g, P),
            };
        }

        private static Point Double(Point p)
        {
            BigInteger a = Mod(p.X * p.X, P);
            BigInteger b = Mod(p.Y * p.Y, P);
            BigInteger c = Mod(2 * p.Z * p.Z, P);
            BigInteger d = Mod(-a, P); // a katsayisi -1
            BigInteger xPlusY = Mod(p.X + p.Y, P);
            BigInteger e = Mod(Mod(xPlusY * xPlusY, P) - a - b, P);
            BigInteger g = Mod(d + b, P);
            BigInteger f = Mod(g - c, P);
            BigInteger h = Mod(d - b, P);

            return new Point
            {
                X = Mod(e * f, P),
                Y = Mod(g * h, P),
                T = Mod(e * h, P),
                Z = Mod(f * g, P),
            };
        }

        private static Point ScalarMultiply(Point point, BigInteger scalar)
        {
            // Notr eleman (0 : 1 : 1 : 0)
            var result = new Point { X = BigInteger.Zero, Y = BigInteger.One, Z = BigInteger.One, T = BigInteger.Zero };
            Point addend = point;

            BigInteger k = scalar;
            while (k > 0)
            {
                if (!(k & 1).IsZero)
                {
                    result = Add(result, addend);
                }
                addend = Double(addend);
                k >>= 1;
            }
            return result;
        }

        private static bool AreEqual(Point p1, Point p2)
        {
            // Projektif karsilastirma: X1*Z2 == X2*Z1 ve Y1*Z2 == Y2*Z1
            if (!Mod(p1.X * p2.Z, P).Equals(Mod(p2.X * p1.Z, P))) return false;
            if (!Mod(p1.Y * p2.Z, P).Equals(Mod(p2.Y * p1.Z, P))) return false;
            return true;
        }

        // --- Yardimcilar ---

        private static BigInteger LittleEndianToBigInteger(byte[] bytes)
        {
            // BigInteger(byte[]) little-endian bekler; isaret bitini engellemek icin 0 ekliyoruz.
            byte[] padded = new byte[bytes.Length + 1];
            Buffer.BlockCopy(bytes, 0, padded, 0, bytes.Length);
            return new BigInteger(padded);
        }

        private static BigInteger Mod(BigInteger value, BigInteger modulus)
        {
            BigInteger result = value % modulus;
            return result.Sign < 0 ? result + modulus : result;
        }

        private static BigInteger ModInverse(BigInteger value, BigInteger modulus)
        {
            return BigInteger.ModPow(Mod(value, modulus), modulus - 2, modulus);
        }
    }
}
