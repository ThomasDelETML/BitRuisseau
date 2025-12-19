using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace BitRuisseau
{
    internal class RemoteSong : ISong
    {
        public string Path { get; set; } = "";

        public string Title { get; set; } = "";
        public string Artist { get; set; } = "";
        public int Year { get; set; }
        public TimeSpan Duration { get; set; }
        public int Size { get; set; }
        public string[] Featuring { get; set; } = Array.Empty<string>();
        public string Hash { get; private set; } = "";
        public string Extension { get; private set; } = "";

        public RemoteSong() { }

        public RemoteSong(SongDto dto)
        {
            Path = dto.Path ?? "";
            Title = dto.Title ?? "";
            Artist = dto.Artist ?? "";
            Year = dto.Year;
            Duration = dto.Duration;
            Size = dto.Size;
            Featuring = dto.Featuring ?? Array.Empty<string>();
            Hash = dto.Hash ?? "";
            Extension = dto.Extension ?? "";
        }
    }

    internal class Protocole : IProtocol, IDisposable
    {
        private const string Topic = "BitRuisseau";
        private const string BroadcastRecipient = "0.0.0.0";

        // MQTT payloads: rester raisonnable => chunk 24KB (base64 grossit ~33%)
        private const int ChunkBytes = 24 * 1024;

        private readonly MqttCommunicator _mqtt;
        private readonly string _selfName;
        private readonly LocalMediaLibrary _localLibrary;

        private readonly HashSet<string> _online =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private readonly Dictionary<string, List<RemoteSong>> _catalogs =
            new Dictionary<string, List<RemoteSong>>(StringComparer.OrdinalIgnoreCase);

        private readonly Dictionary<string, ManualResetEventSlim> _catalogWaiters =
            new Dictionary<string, ManualResetEventSlim>(StringComparer.OrdinalIgnoreCase);

        private readonly object _lock = new object();

        // RequestId -> TCS qui reçoit un chunk (sendMedia)
        private readonly ConcurrentDictionary<string, TaskCompletionSource<Message>> _pendingMedia =
            new ConcurrentDictionary<string, TaskCompletionSource<Message>>(StringComparer.OrdinalIgnoreCase);

        public Protocole(LocalMediaLibrary localLibrary, string username, string password, int port = 1883)
        {
            _localLibrary = localLibrary;
            _selfName = Dns.GetHostName();

            _mqtt = new MqttCommunicator(
                brokerHost: "mqtt.blue.section-inf.ch",
                nodeId: $"{_selfName}-T",
                topic: Topic,
                brokerPort: port,
                username: username,
                password: password
            );

            _mqtt.OnMessageReceived = HandleIncoming;
            _mqtt.Start();

            SayOnline();
            AskOnline();
        }

        private void HandleIncoming(Message msg)
        {
            if (msg == null) return;

            // broadcast ou message pour moi
            if (!string.Equals(msg.Recipient, BroadcastRecipient, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(msg.Recipient, _selfName, StringComparison.OrdinalIgnoreCase))
                return;

            switch (msg.Action)
            {
                case "askOnline":
                    SayOnline();
                    break;

                case "online":
                    if (!string.IsNullOrWhiteSpace(msg.Sender))
                        lock (_lock) _online.Add(msg.Sender);
                    break;

                case "askCatalog":
                    if (!string.IsNullOrWhiteSpace(msg.Sender))
                        SendCatalog(msg.Sender);
                    break;

                case "sendCatalog":
                    if (!string.IsNullOrWhiteSpace(msg.Sender))
                        ReceiveCatalog(msg.Sender, msg.SongList);
                    break;

                case "askMedia":
                    // quelqu’un me demande un chunk
                    HandleAskMedia(msg);
                    break;

                case "sendMedia":
                    // je reçois un chunk
                    HandleSendMedia(msg);
                    break;
            }
        }

        private void AskOnline()
        {
            _mqtt.Send(new Message
            {
                Recipient = BroadcastRecipient,
                Sender = _selfName,
                Action = "askOnline"
            });
        }

        public void SayOnline()
        {
            _mqtt.Send(new Message
            {
                Recipient = BroadcastRecipient,
                Sender = _selfName,
                Action = "online"
            });
        }

        public string[] GetOnlineMediatheque()
        {
            lock (_lock)
            {
                return _online
                    .Where(x => !string.Equals(x, _selfName, StringComparison.OrdinalIgnoreCase))
                    .ToArray();
            }
        }

        // ---------- CATALOG ----------

        private void ReceiveCatalog(string sender, List<SongDto>? list)
        {
            var converted = (list ?? new List<SongDto>())
                .Select(dto => new RemoteSong(dto))
                .ToList();

            ManualResetEventSlim? waiter = null;

            lock (_lock)
            {
                _catalogs[sender] = converted;
                _catalogWaiters.TryGetValue(sender, out waiter);
            }

            waiter?.Set();
        }

        public List<ISong> AskCatalog(string name)
        {
            ManualResetEventSlim waiter;

            lock (_lock)
            {
                if (_catalogWaiters.TryGetValue(name, out var old))
                    old.Dispose();

                waiter = new ManualResetEventSlim(false);
                _catalogWaiters[name] = waiter;
            }

            _mqtt.Send(new Message
            {
                Recipient = name,
                Sender = _selfName,
                Action = "askCatalog"
            });

            var ok = waiter.Wait(TimeSpan.FromSeconds(3));

            lock (_lock)
            {
                if (_catalogWaiters.TryGetValue(name, out var w) && ReferenceEquals(w, waiter))
                    _catalogWaiters.Remove(name);
            }
            waiter.Dispose();

            if (!ok) return new List<ISong>();

            lock (_lock)
            {
                if (_catalogs.TryGetValue(name, out var songs))
                    return songs.Cast<ISong>().ToList();
            }

            return new List<ISong>();
        }

        public void SendCatalog(string name)
        {
            var catalog = (_localLibrary.Songs ?? new List<Song>())
                .Select(s => new SongDto
                {
                    Path = s.FilePath ?? "",
                    Title = s.Title ?? "",
                    Artist = s.Artist ?? "",
                    Year = s.Year,
                    Size = s.Size,
                    Featuring = s.Featuring ?? Array.Empty<string>(),
                    Hash = s.Hash ?? "",
                    Duration = s.Duration,
                    Extension = s.Extension ?? ""
                })
                .ToList();

            _mqtt.Send(new Message
            {
                Recipient = name,
                Sender = _selfName,
                Action = "sendCatalog",
                SongList = catalog
            });
        }

        // ---------- MEDIA (import) ----------

        public void AskMedia(ISong song, string name, int startByte, int endByte)
        {
            // requis par IProtocol, mais l’import “propre” passe par AskMediaAsync
            _mqtt.Send(new Message
            {
                Recipient = name,
                Sender = _selfName,
                Action = "askMedia",
                StartByte = startByte,
                EndByte = endByte,
                Hash = song.Hash,
                RequestId = Guid.NewGuid().ToString("N")
            });
        }

        public void SendMedia(ISong song, string name, int startByte, int endByte)
        {
            // requis par IProtocol, mais en pratique on répond via HandleAskMedia
            throw new NotImplementedException();
        }

        private void HandleAskMedia(Message msg)
        {
            if (string.IsNullOrWhiteSpace(msg.Sender) ||
                string.IsNullOrWhiteSpace(msg.Hash) ||
                string.IsNullOrWhiteSpace(msg.RequestId) ||
                msg.StartByte == null ||
                msg.EndByte == null)
                return;

            var hash = NormalizeHash(msg.Hash);
            var start = msg.StartByte.Value;
            var end = msg.EndByte.Value;

            var local = _localLibrary.Songs.FirstOrDefault(s => NormalizeHash(s.Hash) == hash);
            if (local == null || string.IsNullOrWhiteSpace(local.FilePath) || !File.Exists(local.FilePath))
                return;

            long fileLen = new FileInfo(local.FilePath).Length;

            if (start < 0) start = 0;
            if (start >= fileLen) return;
            if (end >= fileLen) end = (int)fileLen - 1;
            if (end < start) return;

            int count = end - start + 1;
            var buffer = new byte[count];

            try
            {
                using (var fs = new FileStream(local.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    fs.Position = start;
                    int read = 0;
                    while (read < count)
                    {
                        int r = fs.Read(buffer, read, count - read);
                        if (r <= 0) break;
                        read += r;
                    }

                    if (read <= 0) return;

                    if (read != count)
                    {
                        Array.Resize(ref buffer, read);
                        end = start + read - 1;
                    }
                }

                var base64 = Convert.ToBase64String(buffer);

                _mqtt.Send(new Message
                {
                    Recipient = msg.Sender,
                    Sender = _selfName,
                    Action = "sendMedia",
                    StartByte = start,
                    EndByte = end,
                    Hash = msg.Hash,
                    SongData = base64,
                    RequestId = msg.RequestId
                });
            }
            catch
            {
                // ignore
            }
        }

        private void HandleSendMedia(Message msg)
        {
            if (string.IsNullOrWhiteSpace(msg.RequestId))
                return;

            if (_pendingMedia.TryRemove(msg.RequestId, out var tcs))
            {
                tcs.TrySetResult(msg);
            }
        }

        private async Task<Message> AskMediaAsync(string remoteHost, string hash, int start, int end, CancellationToken ct)
        {
            var requestId = Guid.NewGuid().ToString("N");
            var tcs = new TaskCompletionSource<Message>(TaskCreationOptions.RunContinuationsAsynchronously);

            if (!_pendingMedia.TryAdd(requestId, tcs))
                throw new InvalidOperationException("Impossible de créer la requête askMedia.");

            _mqtt.Send(new Message
            {
                Recipient = remoteHost,
                Sender = _selfName,
                Action = "askMedia",
                StartByte = start,
                EndByte = end,
                Hash = hash,
                RequestId = requestId
            });

            using var reg = ct.Register(() =>
            {
                if (_pendingMedia.TryRemove(requestId, out var t))
                    t.TrySetCanceled(ct);
            });

            // timeout simple
            var completed = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(5), ct));
            if (completed != tcs.Task)
            {
                _pendingMedia.TryRemove(requestId, out _);
                throw new TimeoutException("Timeout: sendMedia non reçu.");
            }

            return await tcs.Task;
        }

        public async Task<string> ImportRemoteSongAsync(
            RemoteSong remoteSong,
            string remoteHost,
            string destFolder,
            IProgress<int>? progress = null,
            CancellationToken ct = default)
        {
            if (remoteSong == null) throw new ArgumentNullException(nameof(remoteSong));
            if (string.IsNullOrWhiteSpace(remoteHost)) throw new ArgumentNullException(nameof(remoteHost));
            if (string.IsNullOrWhiteSpace(destFolder) || !Directory.Exists(destFolder))
                throw new InvalidOperationException("Dossier local invalide.");
            if (remoteSong.Size <= 0) throw new InvalidOperationException("Size manquant.");
            if (string.IsNullOrWhiteSpace(remoteSong.Hash)) throw new InvalidOperationException("Hash manquant.");

            var safeTitle = string.Join("_", (remoteSong.Title ?? "song").Split(Path.GetInvalidFileNameChars()));
            var ext = string.IsNullOrWhiteSpace(remoteSong.Extension) ? ".bin" : remoteSong.Extension;
            var finalPath = Path.Combine(destFolder, safeTitle + ext);
            var tempPath = finalPath + ".part";

            if (File.Exists(finalPath))
                return finalPath;

            int downloaded = 0;
            progress?.Report(0);

            try
            {
                using (var fs = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    while (downloaded < remoteSong.Size)
                    {
                        ct.ThrowIfCancellationRequested();

                        int start = downloaded;
                        int end = Math.Min(start + ChunkBytes - 1, remoteSong.Size - 1);

                        var msg = await AskMediaAsync(remoteHost, remoteSong.Hash, start, end, ct);

                        // validation
                        if (msg.StartByte != start)
                            throw new InvalidOperationException("Chunk hors séquence.");
                        if (string.IsNullOrWhiteSpace(msg.SongData))
                            throw new InvalidOperationException("Chunk vide.");

                        var bytes = Convert.FromBase64String(msg.SongData);

                        await fs.WriteAsync(bytes, 0, bytes.Length, ct);
                        downloaded += bytes.Length;

                        int pct = (int)Math.Round(downloaded * 100.0 / remoteSong.Size);
                        if (pct > 100) pct = 100;
                        progress?.Report(pct);
                    }
                }

                // vérif hash (important pour garantir “écouter même si elle s’arrête”)
                var computed = NormalizeHash(ComputeSha256Hex(tempPath));
                var expected = NormalizeHash(remoteSong.Hash);

                if (!string.Equals(computed, expected, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("SHA256 invalide après import.");

                File.Move(tempPath, finalPath);
                progress?.Report(100);
                return finalPath;
            }
            finally
            {
                if (File.Exists(tempPath))
                {
                    try { File.Delete(tempPath); } catch { }
                }
            }
        }

        private static string ComputeSha256Hex(string filePath)
        {
            using var stream = System.IO.File.OpenRead(filePath);
            using var sha = SHA256.Create();
            var hash = sha.ComputeHash(stream);
            var sb = new StringBuilder(hash.Length * 2);
            foreach (var b in hash) sb.Append(b.ToString("X2")); // majuscules
            return sb.ToString();
        }

        private static string NormalizeHash(string? h)
            => (h ?? "").Replace("-", "").Trim().ToUpperInvariant();

        public void Dispose() => _mqtt.Dispose();
    }
}
