# com.company.license

Her projede tekrar yazilmasi gerekmeyen, paylasimli lisans aktivasyon/dogrulama SDK'si.

## Kurulum (her proje icin)

1. Package Manager > "Install package from disk" ile bu klasordeki `package.json`'i secin (veya UPM git URL'i ile ekleyin).
2. **Ed25519 bagimliligi**: `Runtime/LicenseSignatureVerifier.cs` basindaki notu okuyun — offline imza dogrulamasi icin pure-C# bir Ed25519 kutuphanesi (onerilen: Chaos.NaCl) eklenmeli ve Player Settings > Scripting Define Symbols'a `LICENSE_SDK_HAS_ED25519` yazilmali. Bu adim atlanirsa SDK yine calisir ama offline grace period sirasinda yerel dosya tahrifatina karsi korumasiz olur.
3. `Create > License > License Config` ile bir `LicenseConfig` asset'i olusturun:
   - `ApiBaseUrl`: backend'in adresi.
   - `PublicKeyBase64`: backend'de `npm run keys:generate` ile uretilen `license_ed25519_public.pem` dosyasinin SPKI govdesi (PEM basliklari/satir sonlari cikarilmis base64).
4. Baslangic sahnenize bos bir GameObject'e `LicenseGateBehaviour` ekleyin, `config` alanina yukaridaki asset'i atayin.
5. `OnLicenseValid` / `OnLicenseLocked` / `OnLicenseLockedWithReason` event'lerini kendi UI'inize (kilit ekraninizi acip kapatan fonksiyonlara) baglayin — SDK bir UI dayatmaz.

## Aktivasyon anahtari nasil cihaza ulasir

`LicenseGateBehaviour`, `Application.persistentDataPath/<ProvisioningFileName>` (varsayilan `activation.json`) dosyasini arar:

```json
{ "activationKey": "LIC-XXXXXXXX-XXXXXXXX" }
```

Bu dosya ManageXR uzerinden cihaza/cihaz grubuna push edilerek tek tek elle giris ihtiyaci ortadan kaldirilir. Aktivasyon bir kere basarili olduktan sonra SDK kendi sifreli onbellegini kullanir; dosyanin cihazda kalmasi sorun degildir.

## Uzaktan kontrol (web paneli -> cihaz, MQTT)

Lisans gecerli oldugu surece `LicenseGateBehaviour` cihazi otomatik olarak MQTT broker'a
baglar (`wss://api.exposiavr.com/mqtt`). Kimlik, cihazin imzali lisans token'idir: ayri
kullanici/sifre yoktur ve lisansi olmayan uygulama baglanamaz. Musteri paneli (`/portal/`)
yalnizca o musterinin cihazlarina komut gonderebilir.

`LicenseConfig` alanlari: `EnableRemoteControl` (varsayilan acik), `MqttUrl`,
`TelemetryIntervalSeconds` (varsayilan 10; 30'un ustune cikarmayin, backend 45 sn sinyal
gormezse uygulamayi cevrimdisi sayar).

### Uygulamaya komut eklemek

1. **Backend**: komutu `backend/src/mqtt/commands.ts` icindeki listeye, urun koduyla ve
   parametre semasiyla ekleyin. Listede olmayan komut cihaza HIC gonderilmez.
2. **Unity** (kodla):

   ```csharp
   [System.Serializable] class SlidePayload { public string to; }

   MqttCommandManager.RegisterHandler("mri.slide", cmd =>
   {
       if (isMoving) return CommandResult.Fail("busy");   // panelde "Reddedildi: busy"
       var to = cmd.PayloadAs<SlidePayload>().to;
       StartCoroutine(Slide(to));
       return CommandResult.Success;                       // panelde "Onaylandi"
   });

   // Panelde gosterilecek durum (gecerli bir JSON degeri donmeli):
   MqttCommandManager.SetTelemetrySection("mri", () => "{\"sledInside\":true}");
   // Durum degisince paneli beklemeden guncelle:
   MqttCommandManager.RequestTelemetry();
   ```

   veya **kodsuz**: sahneye `MqttCommandBindings` ekleyip Inspector'dan komut tipini bir
   metoda baglayin (metot payload'i ham JSON string olarak alir).

Handler'lar her zaman Unity ana thread'inde calisir. 10 sn'den uzun kuyrukta bekleyen
(uygulama donmus/arka plandaydi) veya suresi gecmis komut CALISTIRILMAZ, "expired" doner;
QoS1'in tekrar teslim ettigi ayni komut iki kez calismaz. Uygulama duraklarsa (Quest'te
arka plana gecince) baglanti duzgunce kapatilir, donunce yeniden kurulur.

### IL2CPP / Quest

`Runtime/Plugins/MQTTnet.dll` (MQTTnet 4.3.7, netstandard2.1, MIT, bagimliliksiz) pakete
dahildir. Kod budamasinin onu silmemesi icin `Runtime/link.xml`'in bir kopyasini uygulama
projesinin `Assets/` klasorune de koyun (Unity bazi surumlerde paketteki link.xml'i islemez).

### Lisans degisikliginin aninda etkisi

Admin bir cihazi kaldirdiginda, koltugunu serbest biraktiginda veya lisansi askiya aldiginda
backend cihaza `license.recheck` sistem mesajini gonderir (cihazlar komut topic'ine yazamadigi
icin bunu yalnizca backend gonderebilir). `LicenseGateBehaviour` ~3 sn sonra lisansi sunucuya
sorar ve gerekiyorsa kilitler; broker baglantiyi yetki nedeniyle reddederse de ayni kontrol
yapilir (en fazla 5 dakikada bir). Kilitleme karari her zaman `/validate` cevabidir: sunucuya
ulasilamazsa uygulama yerel token'a gore acik kalir. `license.recheck` adiyla handler kaydetmeyin.
