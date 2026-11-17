using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MQTTnet;
using MQTTnet.Adapter;
using MQTTnet.Client;
using MQTTnet.Protocol;
using UnityEngine;

namespace Company.License
{
    // Bir komut handler'inin sonucu. Cihaz bunu backend'e ack olarak yollar; panelde
    // "Onaylandi" / "Reddedildi: <error>" olarak gorunur.
    public readonly struct CommandResult
    {
        public readonly bool Ok;
        public readonly string Error;

        private CommandResult(bool ok, string error)
        {
            Ok = ok;
            Error = error;
        }

        public static CommandResult Success => new CommandResult(true, null);
        public static CommandResult Fail(string error) => new CommandResult(false, string.IsNullOrEmpty(error) ? "failed" : error);
    }

    // Panelden gelen bir komut. Handler'lar her zaman Unity ANA THREAD'inde cagrilir.
    public sealed class MqttCommand
    {
        public string Id { get; }
        public string Type { get; }
        public string ProductCode { get; }
        // Komut parametreleri, ham JSON nesnesi (ornek: {"to":"inside"}).
        public string PayloadJson { get; }

        internal MqttCommand(string id, string type, string productCode, string payloadJson)
        {
            Id = id;
            Type = type;
            ProductCode = productCode;
            PayloadJson = string.IsNullOrEmpty(payloadJson) ? "{}" : payloadJson;
        }

        // Ornek: [Serializable] class SlidePayload { public string to; }  ->  cmd.PayloadAs<SlidePayload>().to
        public T PayloadAs<T>() => JsonUtility.FromJson<T>(PayloadJson);
    }

    // Panel <-> cihaz uzaktan kontrol baglantisi (MQTT over WSS).
    //
    // Kullanim (uygulama kodunda):
    //   MqttCommandManager.RegisterHandler("mri.slide", cmd => { ...; return CommandResult.Success; });
    //   MqttCommandManager.SetTelemetrySection("mri", () => "{\"sledInside\":true}");
    //
    // Baglantiyi LicenseGateBehaviour yonetir: lisans gecerli olunca EnsureRunning, kilitlenince Shutdown.
    // Handler'lar statik bir kayitta durur; uygulama onlari manager olusmadan once de kaydedebilir.
    //
    // Topic agaci ve guvenlik kurallari: backend/src/mqtt/topics.ts
    public sealed class MqttCommandManager : MonoBehaviour
    {
        private const string LogPrefix = "[RemoteControl]";
        private const string TopicRoot = "exposiavr";
        private const int MaxMessageBytes = 16 * 1024;
        // Ana thread'de bu kadar sure kuyrukta bekleyen komut uygulanmaz (uygulama donmus /
        // arka plandaydi): "masayi kaydir" gibi bir komut beklenmedik bir anda calismasin.
        private const double MaxQueueDelaySeconds = 10;
        // Sunucunun expiresAt'i ile cihaz saati arasindaki kabul edilebilir fark.
        private const double ClockSkewToleranceSeconds = 120;
        private const int RememberedCommandIds = 64;

        // --- Statik kayit (manager olmadan da kullanilabilir) ---

        private static readonly Dictionary<string, Func<MqttCommand, CommandResult>> Handlers =
            new Dictionary<string, Func<MqttCommand, CommandResult>>();
        private static readonly Dictionary<string, Func<string>> TelemetrySections = new Dictionary<string, Func<string>>();

        public static MqttCommandManager Instance { get; private set; }
        public static bool IsConnected => Instance != null && Instance._connected;
        // Ana thread'de tetiklenir.
        public static event Action<bool> ConnectionChanged;

        // Lisansin HEMEN sunucuya sorulmasi gerektigini bildirir (ana thread'de). Iki kaynak:
        //   "server_request"            : backend "license.recheck" gonderdi (admin cihazi kaldirdi,
        //                                 koltugu serbest birakti veya lisansi askiya aldi)
        //   "broker_rejected:<neden>"   : broker baglantiyi yetki nedeniyle reddetti
        // LicenseGateBehaviour bunu dinler. Kilitleme karari YINE sunucunun /validate cevabidir;
        // bu olay sadece 12 saatlik heartbeat'i beklememeyi saglar.
        public static event Action<string> LicenseCheckRequested;

        // Backend'in cihazdaki TUM uygulamalara gonderdigi sistem komutu. Cihazlar komut topic'ine
        // yazamadigi icin (ACL) bunu yalnizca backend gonderebilir.
        private const string LicenseRecheckCommand = "license.recheck";

        public static void RegisterHandler(string commandType, Func<MqttCommand, CommandResult> handler)
        {
            if (string.IsNullOrEmpty(commandType) || handler == null) return;
            Handlers[commandType] = handler;
        }

        public static void RegisterHandler(string commandType, Action<MqttCommand> handler)
        {
            if (handler == null) return;
            RegisterHandler(commandType, cmd =>
            {
                handler(cmd);
                return CommandResult.Success;
            });
        }

        public static void UnregisterHandler(string commandType)
        {
            if (!string.IsNullOrEmpty(commandType)) Handlers.Remove(commandType);
        }

        // Telemetriye eklenecek bolum: provider gecerli bir JSON DEGERI donmeli (nesne, sayi, ...).
        // Panelde app.telemetry[key] olarak okunur.
        public static void SetTelemetrySection(string key, Func<string> jsonProvider)
        {
            if (string.IsNullOrEmpty(key) || jsonProvider == null) return;
            TelemetrySections[key] = jsonProvider;
        }

        public static void RemoveTelemetrySection(string key)
        {
            if (!string.IsNullOrEmpty(key)) TelemetrySections.Remove(key);
        }

        // Durum degistiginde (ornegin masa hareketi bitti) paneli beklemeden guncelle.
        public static void RequestTelemetry()
        {
            if (Instance != null) Instance._telemetryRequested = true;
        }

        public static void PublishEvent(string type, string dataJson = null)
        {
            if (Instance == null || !Instance._connected || Instance._identity == null) return;
            string json = "{\"productCode\":" + JsonString(Instance._identity.ProductCode) +
                          ",\"type\":" + JsonString(type) +
                          ",\"data\":" + (string.IsNullOrEmpty(dataJson) ? "null" : dataJson) + "}";
            Instance.PublishFireAndForget(Instance._identity.EventTopic, json, MqttQualityOfServiceLevel.AtMostOnce, false);
        }

        public static MqttCommandManager EnsureRunning(LicenseConfig config)
        {
            if (config == null || !config.EnableRemoteControl || string.IsNullOrEmpty(config.MqttUrl)) return null;
            if (Instance == null)
            {
                var go = new GameObject("MqttCommandManager");
                DontDestroyOnLoad(go);
                Instance = go.AddComponent<MqttCommandManager>();
            }
            Instance.StartRunning(config);
            return Instance;
        }

        public static void Shutdown()
        {
            if (Instance != null) Instance.StopRunning();
        }

        // --- Ornek durumu ---

        private sealed class Identity
        {
            public string Username;
            public string Token;
            public string ProductCode;
            public string CommandTopic;
            public string BroadcastTopic;
            public string AckTopic;
            public string StatusTopic;
            public string TelemetryTopic;
            public string EventTopic;
        }

        [Serializable]
        private class CommandEnvelope
        {
            public string id;
            public string type;
            public string productCode;
            public string issuedAt;
            public string expiresAt;
        }

        private LicenseConfig _config;
        private IMqttClient _client;
        private Identity _identity;
        private CancellationTokenSource _cts;
        private Coroutine _loop;
        private bool _running;
        private volatile bool _connected;
        private bool _lastReportedConnected;
        private int _failedAttempts;

        private readonly ConcurrentQueue<(string json, DateTime receivedAtUtc)> _inbox =
            new ConcurrentQueue<(string, DateTime)>();
        private readonly Queue<string> _recentIds = new Queue<string>();
        private readonly Dictionary<string, CommandResult> _recentResults = new Dictionary<string, CommandResult>();

        private float _nextTelemetryAt;
        private float _lastTelemetrySentAt = -100f;
        private bool _telemetryRequested;
        private float _smoothedDeltaTime = 1f / 72f;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        private void StartRunning(LicenseConfig config)
        {
            _config = config;
            if (_running) return;
            _running = true;
            _cts = new CancellationTokenSource();
            _loop = StartCoroutine(ConnectionLoop());
        }

        private void StopRunning()
        {
            if (!_running) return;
            _running = false;
            if (_loop != null) StopCoroutine(_loop);
            _loop = null;
            _cts?.Cancel();
            DisconnectGracefully(1000);
        }

        // --- Baglanti dongusu (ana thread'de coroutine; MQTTnet isleri arka planda) ---

        private IEnumerator ConnectionLoop()
        {
            while (_running)
            {
                if (_client == null || !_client.IsConnected)
                {
                    if (_connected) SetConnected(false);

                    if (!TryResolveIdentity(out var identity, out string reason))
                    {
                        Debug.LogWarning($"{LogPrefix} Baglanilmiyor: {reason}. 30 sn sonra tekrar denenecek.");
                        yield return new WaitForSecondsRealtime(30f);
                        continue;
                    }

                    Task<string> attempt = ConnectAsync(identity, _cts.Token);
                    while (!attempt.IsCompleted) yield return null;

                    string error = attempt.IsFaulted ? Describe(attempt.Exception) : attempt.Result;
                    if (error == null)
                    {
                        _failedAttempts = 0;
                        _identity = identity;
                        SetConnected(true);
                        _telemetryRequested = true;
                        Debug.Log($"{LogPrefix} Baglandi: {_config.MqttUrl} ({identity.Username})");
                    }
                    else
                    {
                        if (error.StartsWith("connect_refused:NotAuthorized") || error.StartsWith("connect_refused:BadUserNameOrPassword"))
                        {
                            LicenseCheckRequested?.Invoke("broker_rejected:" + error);
                            if (!_running) yield break; // dinleyici lisansi kilitleyip bizi kapatmis olabilir
                        }
                        _failedAttempts++;
                        // Ustel geri cekilme + rastgelelik: sunucu yeniden basladiginda binlerce
                        // cihaz ayni saniyede saldirmasin.
                        float delay = Mathf.Min(60f, Mathf.Pow(2f, Mathf.Min(_failedAttempts, 6))) * UnityEngine.Random.Range(0.75f, 1.25f);
                        Debug.LogWarning($"{LogPrefix} Baglanti basarisiz ({error}). {delay:0} sn sonra tekrar denenecek.");
                        yield return new WaitForSecondsRealtime(delay);
                        continue;
                    }
                }
                yield return new WaitForSecondsRealtime(1f);
            }
        }

        private bool TryResolveIdentity(out Identity identity, out string reason)
        {
            identity = null;
            var stored = TokenStorage.Load();
            if (stored == null || string.IsNullOrEmpty(stored.token))
            {
                reason = "not_activated";
                return false;
            }
            // Kimlik (musteri/cihaz/urun) SADECE imzasi dogrulanmis token'dan okunur.
            if (!LicenseSignatureVerifier.TryVerifyAndDecode(stored.token, _config.PublicKeyBase64, out var payload))
            {
                reason = "invalid_token";
                return false;
            }
            if (payload.productCode != _config.ProductCode)
            {
                reason = "product_mismatch";
                return false;
            }

            string baseTopic = $"{TopicRoot}/{payload.customerId}/{payload.deviceId}";
            identity = new Identity
            {
                Username = $"d:{payload.customerId}:{payload.deviceId}:{payload.productCode}",
                Token = stored.token,
                ProductCode = payload.productCode,
                CommandTopic = baseTopic + "/cmd",
                BroadcastTopic = $"{TopicRoot}/{payload.customerId}/all/cmd",
                AckTopic = baseTopic + "/cmd/ack",
                StatusTopic = baseTopic + "/status/" + payload.productCode,
                TelemetryTopic = baseTopic + "/telemetry",
                EventTopic = baseTopic + "/event",
            };
            reason = null;
            return true;
        }

        // Basariliysa null, degilse hata nedeni doner.
        private async Task<string> ConnectAsync(Identity id, CancellationToken ct)
        {
            if (_client == null)
            {
                _client = new MqttFactory().CreateMqttClient();
                _client.ApplicationMessageReceivedAsync += OnMessageReceived;
                _client.DisconnectedAsync += OnDisconnected;
            }

            var options = new MqttClientOptionsBuilder()
                .WithWebSocketServer(o => o.WithUri(_config.MqttUrl))
                .WithProtocolVersion(MQTTnet.Formatter.MqttProtocolVersion.V311)
                .WithCredentials(id.Username, id.Token)
                // Broker zaten dogrulanmis kullanici adini clientid yapar (use_username_as_clientid).
                .WithClientId(id.Username)
                .WithCleanSession(true)
                // Cloudflare ~100 sn bosta kalan WebSocket'i kapatir; 30 sn ping bunu onler.
                .WithKeepAlivePeriod(TimeSpan.FromSeconds(30))
                .WithTimeout(TimeSpan.FromSeconds(15))
                // LWT: baglanti beklenmedik koparsa broker panele "kapali" bilgisini yayinlar.
                .WithWillTopic(id.StatusTopic)
                .WithWillPayload(StatusJson(id.ProductCode, false))
                .WithWillRetain(true)
                .WithWillQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
                .Build();

            try
            {
                await _client.ConnectAsync(options, ct);
            }
            catch (MqttConnectingFailedException e)
            {
                // NotAuthorized: lisans askiya alinmis / koltuk serbest birakilmis / cihaz engellenmis olabilir.
                return "connect_refused:" + e.ResultCode;
            }

            await PublishAsync(id.StatusTopic, StatusJson(id.ProductCode, true), MqttQualityOfServiceLevel.AtLeastOnce, true, ct);

            var subscribe = new MqttClientSubscribeOptionsBuilder()
                .WithTopicFilter(f => f.WithTopic(id.CommandTopic).WithAtLeastOnceQoS())
                .WithTopicFilter(f => f.WithTopic(id.BroadcastTopic).WithAtLeastOnceQoS())
                .Build();
            var result = await _client.SubscribeAsync(subscribe, ct);
            foreach (var item in result.Items)
            {
                if (item.ResultCode != MqttClientSubscribeResultCode.GrantedQoS0 &&
                    item.ResultCode != MqttClientSubscribeResultCode.GrantedQoS1 &&
                    item.ResultCode != MqttClientSubscribeResultCode.GrantedQoS2)
                {
                    await _client.DisconnectAsync(new MqttClientDisconnectOptionsBuilder().Build(), CancellationToken.None);
                    return "subscribe_denied:" + item.TopicFilter.Topic;
                }
            }
            return null;
        }

        // MQTTnet arka plan thread'i: burada Unity API'si KULLANILMAZ, sadece kuyruga atilir.
        private Task OnMessageReceived(MqttApplicationMessageReceivedEventArgs e)
        {
            var segment = e.ApplicationMessage.PayloadSegment;
            if (segment.Count > 0 && segment.Count <= MaxMessageBytes && segment.Array != null)
            {
                _inbox.Enqueue((Encoding.UTF8.GetString(segment.Array, segment.Offset, segment.Count), DateTime.UtcNow));
            }
            return Task.CompletedTask;
        }

        private Task OnDisconnected(MqttClientDisconnectedEventArgs e)
        {
            _connected = false; // ConnectionLoop bir sonraki turda fark edip yeniden baglanir.
            return Task.CompletedTask;
        }

        private void SetConnected(bool value)
        {
            _connected = value;
        }

        // --- Ana thread ---

        private void Update()
        {
            _smoothedDeltaTime = Mathf.Lerp(_smoothedDeltaTime, Time.unscaledDeltaTime, 0.05f);

            if (_lastReportedConnected != _connected)
            {
                _lastReportedConnected = _connected;
                ConnectionChanged?.Invoke(_connected);
            }

            // Kare basina sinirli sayida komut (bir patlama kareyi dondurmesin).
            for (int i = 0; i < 16 && _inbox.TryDequeue(out var message); i++)
            {
                HandleCommand(message.json, message.receivedAtUtc);
            }

            if (_connected && _identity != null)
            {
                float now = Time.realtimeSinceStartup;
                bool due = now >= _nextTelemetryAt;
                // Istenen ek telemetri: en fazla 4/sn (durum degisikliklerinde panel hizli guncellensin).
                bool requested = _telemetryRequested && now - _lastTelemetrySentAt >= 0.25f;
                if (due || requested)
                {
                    _telemetryRequested = false;
                    _lastTelemetrySentAt = now;
                    _nextTelemetryAt = now + Mathf.Max(2f, _config.TelemetryIntervalSeconds);
                    PublishFireAndForget(_identity.TelemetryTopic, BuildTelemetryJson(), MqttQualityOfServiceLevel.AtMostOnce, false);
                }
            }
        }

        private void HandleCommand(string json, DateTime receivedAtUtc)
        {
            if (_identity == null) return;

            CommandEnvelope envelope;
            try
            {
                envelope = JsonUtility.FromJson<CommandEnvelope>(json);
            }
            catch (Exception)
            {
                return;
            }
            if (envelope == null || string.IsNullOrEmpty(envelope.id) || string.IsNullOrEmpty(envelope.type)) return;

            // Sistem komutu: cihazdaki her uygulama icin gecerli (urun kodundan bagimsiz), ack beklenmez.
            if (envelope.type == LicenseRecheckCommand)
            {
                Debug.Log($"{LogPrefix} Sunucu lisans kontrolu istedi.");
                LicenseCheckRequested?.Invoke("server_request");
                return;
            }

            // Ayni cihazdaki baska bir uygulamaya (Hub, baska oyun) gonderilmis komut: sessizce yok say.
            if (envelope.productCode != _identity.ProductCode) return;

            // QoS1 ayni mesaji iki kez teslim edebilir: komut iki kez CALISTIRILMAZ, ilk sonuc tekrar bildirilir.
            if (_recentResults.TryGetValue(envelope.id, out var previous))
            {
                SendAck(envelope.id, previous);
                return;
            }

            CommandResult result;
            DateTime now = DateTime.UtcNow;
            if ((now - receivedAtUtc).TotalSeconds > MaxQueueDelaySeconds || IsExpired(envelope.expiresAt, now))
            {
                result = CommandResult.Fail("expired");
            }
            else if (envelope.type == "app.ping")
            {
                result = CommandResult.Success;
            }
            else if (Handlers.TryGetValue(envelope.type, out var handler))
            {
                var command = new MqttCommand(envelope.id, envelope.type, envelope.productCode, ExtractObjectField(json, "payload"));
                try
                {
                    result = handler(command);
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                    result = CommandResult.Fail("handler_exception");
                }
            }
            else
            {
                result = CommandResult.Fail("unknown_command");
            }

            Remember(envelope.id, result);
            SendAck(envelope.id, result);
            Debug.Log($"{LogPrefix} {envelope.type} -> {(result.Ok ? "ok" : result.Error)}");
            // Komut buyuk ihtimalle durumu degistirdi: paneli hemen guncelle.
            _telemetryRequested = true;
        }

        private static bool IsExpired(string expiresAt, DateTime nowUtc)
        {
            if (string.IsNullOrEmpty(expiresAt)) return false;
            if (!DateTime.TryParse(expiresAt, CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var expiry))
            {
                return false;
            }
            return expiry.AddSeconds(ClockSkewToleranceSeconds) < nowUtc;
        }

        private void Remember(string id, CommandResult result)
        {
            _recentResults[id] = result;
            _recentIds.Enqueue(id);
            while (_recentIds.Count > RememberedCommandIds) _recentResults.Remove(_recentIds.Dequeue());
        }

        private void SendAck(string id, CommandResult result)
        {
            string json = "{\"id\":" + JsonString(id) +
                          ",\"ok\":" + (result.Ok ? "true" : "false") +
                          ",\"error\":" + (result.Ok ? "null" : JsonString(result.Error)) +
                          ",\"productCode\":" + JsonString(_identity.ProductCode) + "}";
            PublishFireAndForget(_identity.AckTopic, json, MqttQualityOfServiceLevel.AtLeastOnce, false);
        }

        private string BuildTelemetryJson()
        {
            var sb = new StringBuilder(256);
            sb.Append("{\"productCode\":").Append(JsonString(_identity.ProductCode));
            sb.Append(",\"appVersion\":").Append(JsonString(Application.version));
            sb.Append(",\"fps\":").Append(Mathf.RoundToInt(1f / Mathf.Max(0.0001f, _smoothedDeltaTime)).ToString(CultureInfo.InvariantCulture));
            float battery = SystemInfo.batteryLevel; // bilinmiyorsa -1
            sb.Append(",\"battery\":").Append(battery < 0f ? "null" : battery.ToString("0.00", CultureInfo.InvariantCulture));

            foreach (var section in TelemetrySections)
            {
                string value;
                try
                {
                    value = section.Value();
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                    continue;
                }
                if (string.IsNullOrEmpty(value)) continue;
                sb.Append(',').Append(JsonString(section.Key)).Append(':').Append(value);
            }
            sb.Append('}');
            return sb.ToString();
        }

        // --- Yayin ---

        private async Task PublishAsync(string topic, string json, MqttQualityOfServiceLevel qos, bool retain, CancellationToken ct)
        {
            var message = new MqttApplicationMessageBuilder()
                .WithTopic(topic)
                .WithPayload(Encoding.UTF8.GetBytes(json))
                .WithQualityOfServiceLevel(qos)
                .WithRetainFlag(retain)
                .Build();
            await _client.PublishAsync(message, ct);
        }

        private void PublishFireAndForget(string topic, string json, MqttQualityOfServiceLevel qos, bool retain)
        {
            if (_client == null || !_client.IsConnected) return;
            PublishAsync(topic, json, qos, retain, CancellationToken.None).ContinueWith(t =>
            {
                if (t.IsFaulted) Debug.LogWarning($"{LogPrefix} Yayin basarisiz ({topic}): {Describe(t.Exception)}");
            }, TaskContinuationOptions.OnlyOnFaulted);
        }

        // Duzgun kapanista LWT TETIKLENMEZ: "kapali" bilgisini kendimiz yayinlariz.
        private void DisconnectGracefully(int timeoutMs)
        {
            var client = _client;
            var identity = _identity;
            _connected = false;
            if (client == null || !client.IsConnected || identity == null) return;

            var task = Task.Run(async () =>
            {
                var offline = new MqttApplicationMessageBuilder()
                    .WithTopic(identity.StatusTopic)
                    .WithPayload(Encoding.UTF8.GetBytes(StatusJson(identity.ProductCode, false)))
                    .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
                    .WithRetainFlag(true)
                    .Build();
                await client.PublishAsync(offline, CancellationToken.None);
                await client.DisconnectAsync(new MqttClientDisconnectOptionsBuilder().Build(), CancellationToken.None);
            });
            try
            {
                task.Wait(timeoutMs);
            }
            catch (Exception)
            {
                // Kapanista ag hatasi: LWT zaten devreye girer.
            }
        }

        // Quest'te uygulama arka plana gecince (ya da kulaklik cikarilinca) ana thread durur: komutlar
        // calistirilamaz ama baglanti acik kalirsa panel cihazi "acik" sanip komut gonderir.
        // Bu yuzden duraklarken baglantiyi duzgunce kapatiyoruz; donunce dongu yeniden baglanir.
        private void OnApplicationPause(bool paused)
        {
            if (!_running) return;
            if (paused) DisconnectGracefully(800);
        }

        private void OnApplicationQuit()
        {
            _running = false;
            _cts?.Cancel();
            DisconnectGracefully(800);
        }

        private void OnDestroy()
        {
            if (Instance != this) return;
            Instance = null;
            _running = false;
            _cts?.Cancel();
            DisconnectGracefully(500);
            _client?.Dispose();
        }

        // --- JSON yardimcilari (JsonUtility ic ice ham nesne okuyamadigi icin) ---

        private static string StatusJson(string productCode, bool online)
        {
            return "{\"online\":" + (online ? "true" : "false") +
                   ",\"productCode\":" + JsonString(productCode) +
                   ",\"appVersion\":" + JsonString(Application.version) + "}";
        }

        internal static string JsonString(string value)
        {
            if (value == null) return "null";
            var sb = new StringBuilder(value.Length + 2);
            sb.Append('"');
            foreach (char c in value)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
            return sb.ToString();
        }

        // Ust seviyedeki bir alanin ham JSON nesnesini doner: {"payload":{"to":"inside"}} -> {"to":"inside"}
        internal static string ExtractObjectField(string json, string field)
        {
            string key = "\"" + field + "\"";
            int i = json.IndexOf(key, StringComparison.Ordinal);
            if (i < 0) return "{}";
            i = json.IndexOf(':', i + key.Length);
            if (i < 0) return "{}";
            i++;
            while (i < json.Length && char.IsWhiteSpace(json[i])) i++;
            if (i >= json.Length || json[i] != '{') return "{}";

            int depth = 0;
            bool inString = false;
            for (int j = i; j < json.Length; j++)
            {
                char c = json[j];
                if (inString)
                {
                    if (c == '\\') j++;
                    else if (c == '"') inString = false;
                    continue;
                }
                if (c == '"') inString = true;
                else if (c == '{') depth++;
                else if (c == '}' && --depth == 0) return json.Substring(i, j - i + 1);
            }
            return "{}";
        }

        private static string Describe(Exception e)
        {
            if (e is AggregateException agg && agg.InnerExceptions.Count == 1) e = agg.InnerExceptions[0];
            return e == null ? "unknown" : $"{e.GetType().Name}: {e.Message}";
        }
    }
}
