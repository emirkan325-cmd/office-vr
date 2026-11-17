using UnityEngine;

namespace Company.License
{
    // Kilit ekrani metinlerinin dili: cihazin sistem dili Almanca ise Almanca, Turkce ise
    // Turkce, aksi halde Ingilizce (musteriler Almanya'daki klinikler; Quest arayuzu Almanca).
    internal static class LicenseText
    {
        private enum Lang { De, Tr, En }

        private static readonly Lang Current =
            Application.systemLanguage == SystemLanguage.German ? Lang.De :
            Application.systemLanguage == SystemLanguage.Turkish ? Lang.Tr : Lang.En;

        internal static string T(string de, string tr, string en) =>
            Current == Lang.De ? de : Current == Lang.Tr ? tr : en;
    }
}
