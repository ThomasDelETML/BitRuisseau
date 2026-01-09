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
    // Représente un morceau "distant" (reçu via MQTT) :
    // - Sert à afficher un catalogue distant dans l'UI, sans dépendre des fichiers locaux du poste distant.
    // - Sert ensuite pour l'import via AskMedia (téléchargement en fragments / chunks).
    internal class RemoteSong : ISong
    {
        // Nom "Path" conservé pour correspondre au JSON reçu ("Path": "...").
        // Ce chemin est celui de la machine distante, il n'est pas exploitable localement.
        public string Path { get; set; } = "";

        public string Title { get; set; } = "";
        public string Artist { get; set; } = "";
        public int Year { get; set; }
        public TimeSpan Duration { get; set; }
        public int Size { get; set; }
        public string[] Featuring { get; set; } = Array.Empty<string>();

        // Hash / Extension : valeurs attendues du catalogue distant, pas de modification via l'UI.
        public string Hash { get; private set; } = "";
        public string Extension { get; private set; } = "";

        public RemoteSong() { }

        // Conversion DTO (transportable / sérialisable) -> RemoteSong (objet pour l'affichage / import).
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

        // Taille des chunks envoyés via MQTT.
        // Base64 augmente la taille (~33%), donc rester raisonnable réduit les risques de limite de payload.
        private const int ChunkBytes = 8 * 1024; // 8KB

        private readonly MqttCommunicator _mqtt;
        private readonly string _selfName;
        private readonly LocalMediaLibrary _localLibrary;

        // Verrou : protège les collections non thread-safe manipulées par le thread MQTT et l'UI.
        private readonly object _lock = new object();

        // Ensemble des médiathèques annoncées comme "online".
        private readonly HashSet<string> _online =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Cache des catalogues reçus : sender -> liste RemoteSong.
        private readonly Dictionary<string, List<RemoteSong>> _catalogs =
            new Dictionary<string, List<RemoteSong>>(StringComparer.OrdinalIgnoreCase);

        // Synchronisation AskCatalog : permet à AskCatalog (synchrone) d'attendre un sendCatalog.
        private readonly Dictionary<string, ManualResetEventSlim> _catalogWaiters =
            new Dictionary<string, ManualResetEventSlim>(StringComparer.OrdinalIgnoreCase);

        // Import média :
        // Corrélation des chunks reçus avec une clé stable basée sur (sender + hash + startByte).
        // Cela évite de dépendre d'un RequestId si les implémentations distantes ne l'utilisent pas.
        private readonly ConcurrentDictionary<string, TaskCompletionSource<Message>> _pendingMedia =
            new ConcurrentDictionary<string, TaskCompletionSource<Message>>(StringComparer.OrdinalIgnoreCase);

        public Protocole(LocalMediaLibrary localLibrary, string username, string password, int port = 1883)
        {
            _localLibrary = localLibrary;
            _selfName = Dns.GetHostName();

            // Client MQTT : envoi/réception de Message JSON sur le topic BitRuisseau.
            _mqtt = new MqttCommunicator(
                brokerHost: "mqtt.blue.section-inf.ch",
                nodeId: $"{_selfName}-T",
                topic: Topic,
                brokerPort: port,
                username: username,
                password: password
            );

            // Tous les messages reçus arrivent dans HandleIncoming.
            _mqtt.OnMessageReceived = HandleIncoming;
            _mqtt.Start();

            // Au démarrage :
            // - annonce de présence (online)
            // - découverte des autres médiathèques (askOnline)
            SayOnline();
            AskOnline();
        }

        private void HandleIncoming(Message msg)
        {
            if (msg == null) return;

            // Filtrage destinataire :
            // - accepter les broadcast
            // - ou les messages adressés au hostname local
            if (!string.Equals(msg.Recipient, BroadcastRecipient, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(msg.Recipient, _selfName, StringComparison.OrdinalIgnoreCase))
                return;

            // Routage en fonction de l'action (protocole applicatif).
            switch (msg.Action)
            {
                case "askOnline":
                    // Répondre à une demande de présence.
                    SayOnline();
                    break;

                case "online":
                    // Enregistrer une médiathèque comme "en ligne".
                    if (!string.IsNullOrWhiteSpace(msg.Sender))
                        lock (_lock) _online.Add(msg.Sender);
                    break;

                case "askCatalog":
                    // Une médiathèque demande le catalogue local.
                    if (!string.IsNullOrWhiteSpace(msg.Sender))
                        SendCatalog(msg.Sender);
                    break;

                case "sendCatalog":
                    // Catalogue reçu d'une médiathèque distante.
                    if (!string.IsNullOrWhiteSpace(msg.Sender))
                        ReceiveCatalog(msg.Sender, msg.SongList);
                    break;

                case "askMedia":
                    // Demande de fragment (chunk) d'un fichier local.
                    HandleAskMedia(msg);
                    break;

                case "sendMedia":
                    // Fragment (chunk) reçu suite à une demande.
                    HandleSendMedia(msg);
                    break;
            }
        }

        private void AskOnline()
        {
            // Broadcast : demande la liste des médiathèques en ligne.
            _mqtt.Send(new Message
            {
                Recipient = BroadcastRecipient,
                Sender = _selfName,
                Action = "askOnline"
            });
        }

        public void SayOnline()
        {
            // Broadcast : annonce la présence.
            _mqtt.Send(new Message
            {
                Recipient = BroadcastRecipient,
                Sender = _selfName,
                Action = "online"
            });
        }

        public string[] GetOnlineMediatheque()
        {
            // Retourne la liste connue, sans inclure l'hôte local.
            lock (_lock)
            {
                return _online
                    .Where(x => !string.Equals(x, _selfName, StringComparison.OrdinalIgnoreCase))
                    .ToArray();
            }
        }

        // ---------------- CATALOG ----------------

        private void ReceiveCatalog(string sender, List<SongDto>? list)
        {
            // Convertit le catalogue transportable (SongDto) en objets destinés à l'UI (RemoteSong).
            var converted = (list ?? new List<SongDto>())
                .Select(dto => new RemoteSong(dto))
                .ToList();

            // Si un appel AskCatalog attendait ce catalogue, il faut le réveiller.
            ManualResetEventSlim? waiter;
            lock (_lock)
            {
                _catalogs[sender] = converted;
                _catalogWaiters.TryGetValue(sender, out waiter);
            }
            waiter?.Set();
        }

        public List<ISong> AskCatalog(string name)
        {
            // 1) Installer un waiter (synchronisation) pour ce remote host.
            ManualResetEventSlim waiter;

            lock (_lock)
            {
                if (_catalogWaiters.TryGetValue(name, out var old))
                    old.Dispose();

                waiter = new ManualResetEventSlim(false);
                _catalogWaiters[name] = waiter;
            }

            // 2) Envoyer la demande de catalogue.
            _mqtt.Send(new Message
            {
                Recipient = name,
                Sender = _selfName,
                Action = "askCatalog"
            });

            // 3) Attendre la réponse (sendCatalog) pendant un délai fixe.
            var ok = waiter.Wait(TimeSpan.FromSeconds(3));

            // 4) Nettoyage du waiter (évite fuite d'objets/handles).
            lock (_lock)
            {
                if (_catalogWaiters.TryGetValue(name, out var w) && ReferenceEquals(w, waiter))
                    _catalogWaiters.Remove(name);
            }
            waiter.Dispose();

            if (!ok) return new List<ISong>();

            // 5) Retourner le catalogue mis en cache à la réception.
            lock (_lock)
            {
                if (_catalogs.TryGetValue(name, out var songs))
                    return songs.Cast<ISong>().ToList();
            }

            return new List<ISong>();
        }

        public void SendCatalog(string name)
        {
            // Construire un catalogue sérialisable (SongDto) à partir de la médiathèque locale.
            // Remarque : les chemins (Path) sont locaux à la machine émettrice.
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

            // Envoi direct vers l'hôte demandé (hostname).
            _mqtt.Send(new Message
            {
                Recipient = name,
                Sender = _selfName,
                Action = "sendCatalog",
                SongList = catalog
            });
        }

        // ---------------- MEDIA (import) ----------------

        public void AskMedia(ISong song, string name, int startByte, int endByte)
        {
            // Méthode imposée par IProtocol : envoie askMedia pour un fragment.
            // Le téléchargement complet est géré par ImportRemoteSongAsync().
            _mqtt.Send(new Message
            {
                Recipient = name,
                Sender = _selfName,
                Action = "askMedia",
                StartByte = startByte,
                EndByte = endByte,
                Hash = song.Hash,
                RequestId = Guid.NewGuid().ToString("N") // champ optionnel
            });
        }

        public void SendMedia(ISong song, string name, int startByte, int endByte)
            => throw new NotImplementedException("Réponse gérée par HandleAskMedia().");

        private void HandleAskMedia(Message msg)
        {
            // Pour servir un chunk il faut : sender, hash, startByte, endByte.
            if (string.IsNullOrWhiteSpace(msg.Sender) ||
                string.IsNullOrWhiteSpace(msg.Hash) ||
                msg.StartByte == null ||
                msg.EndByte == null)
                return;

            var hash = NormalizeHash(msg.Hash);
            var start = msg.StartByte.Value;
            var end = msg.EndByte.Value;

            // Recherche du fichier local correspondant au hash demandé.
            var local = _localLibrary.Songs.FirstOrDefault(s => NormalizeHash(s.Hash) == hash);
            if (local == null || string.IsNullOrWhiteSpace(local.FilePath) || !File.Exists(local.FilePath))
                return;

            long fileLen = new FileInfo(local.FilePath).Length;

            // Sécurisation des bornes.
            if (start < 0) start = 0;
            if (start >= fileLen) return;
            if (end >= fileLen) end = (int)fileLen - 1;
            if (end < start) return;

            int count = end - start + 1;

            try
            {
                // Lecture du fragment [start..end] depuis le fichier.
                var buffer = new byte[count];

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

                    // Ajuster si moins de données que prévu.
                    if (read != count)
                    {
                        Array.Resize(ref buffer, read);
                        end = start + read - 1;
                    }
                }

                // Envoyer le chunk en base64 au demandeur.
                _mqtt.Send(new Message
                {
                    Recipient = msg.Sender,
                    Sender = _selfName,
                    Action = "sendMedia",
                    StartByte = start,
                    EndByte = end,
                    Hash = msg.Hash,
                    SongData = Convert.ToBase64String(buffer),

                    // Renvoi de RequestId si présent, utile pour certaines implémentations.
                    RequestId = msg.RequestId
                });
            }
            catch
            {
                // Erreurs de lecture/IO ignorées : aucun chunk n'est envoyé.
            }
        }

        // Clé stable d'un chunk en attente : (sender, hash, startByte).
        private static string PendingKey(string sender, string hash, int startByte)
            => $"{sender}::{NormalizeHash(hash)}::{startByte}";

        private void HandleSendMedia(Message msg)
        {
            // Pour associer une réponse à une requête, il faut au minimum :
            // - Sender (source attendue)
            // - Hash (fichier attendu)
            // - StartByte (fragment attendu)
            if (string.IsNullOrWhiteSpace(msg.Sender) ||
                string.IsNullOrWhiteSpace(msg.Hash) ||
                msg.StartByte == null)
                return;

            var key = PendingKey(msg.Sender, msg.Hash, msg.StartByte.Value);

            // Si une requête attendait ce chunk, elle est complétée ici.
            if (_pendingMedia.TryRemove(key, out var tcs))
                tcs.TrySetResult(msg);
        }

        private async Task<Message> AskMediaAsync(string remoteHost, string hash, int start, int end, CancellationToken ct)
        {
            // La clé attendue est construite à partir du sender attendu (remoteHost).
            var key = PendingKey(remoteHost, hash, start);

            // TCS : représente l'attente du chunk correspondant (sendMedia).
            var tcs = new TaskCompletionSource<Message>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (!_pendingMedia.TryAdd(key, tcs))
                throw new InvalidOperationException("Chunk déjà en cours.");

            // Envoi de la demande askMedia.
            _mqtt.Send(new Message
            {
                Recipient = remoteHost,
                Sender = _selfName,
                Action = "askMedia",
                StartByte = start,
                EndByte = end,
                Hash = hash,
                RequestId = Guid.NewGuid().ToString("N")
            });

            // Annulation : retirer la requête si cancellation demandée.
            using var reg = ct.Register(() =>
            {
                if (_pendingMedia.TryRemove(key, out var t))
                    t.TrySetCanceled(ct);
            });

            // Timeout : si le chunk n'arrive pas, échec.
            var completed = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(10), ct));
            if (completed != tcs.Task)
            {
                _pendingMedia.TryRemove(key, out _);
                throw new TimeoutException("Timeout: SendMedia non reçu.");
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
            // Validations de base pour éviter des imports incohérents.
            if (remoteSong == null) throw new ArgumentNullException(nameof(remoteSong));
            if (string.IsNullOrWhiteSpace(remoteHost)) throw new ArgumentNullException(nameof(remoteHost));
            if (string.IsNullOrWhiteSpace(destFolder) || !Directory.Exists(destFolder))
                throw new InvalidOperationException("Dossier local invalide.");
            if (remoteSong.Size <= 0) throw new InvalidOperationException("Size manquant.");
            if (string.IsNullOrWhiteSpace(remoteSong.Hash)) throw new InvalidOperationException("Hash manquant.");

            // Nom de fichier safe (caractères Windows interdits remplacés).
            var safeTitle = string.Join("_", (remoteSong.Title ?? "song").Split(Path.GetInvalidFileNameChars()));
            if (string.IsNullOrWhiteSpace(safeTitle)) safeTitle = "song";

            // Extension : si absente, valeur de secours.
            var ext = string.IsNullOrWhiteSpace(remoteSong.Extension) ? ".bin" : remoteSong.Extension.Trim();
            if (!ext.StartsWith(".")) ext = "." + ext;

            // Suffix hash : évite collision si plusieurs morceaux ont le même titre.
            var h = NormalizeHash(remoteSong.Hash);
            var suffix = h.Length >= 8 ? h.Substring(0, 8) : "unknown";

            // Chemin final et chemin temporaire.
            // Le fichier ".part" est un fichier incomplet : il ne doit pas être utilisé comme un média final.
            var finalPath = Path.Combine(destFolder, $"{safeTitle}_{suffix}{ext}");
            var tempPath = finalPath + ".part";

            // Déjà importé => ne rien faire.
            if (File.Exists(finalPath))
                return finalPath;

            int downloaded = 0;
            progress?.Report(0);

            // Téléchargement chunk par chunk dans le fichier temporaire.
            using (var fs = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                while (downloaded < remoteSong.Size)
                {
                    ct.ThrowIfCancellationRequested();

                    // Déterminer le segment [start..end] à télécharger.
                    int start = downloaded;
                    int end = Math.Min(start + ChunkBytes - 1, remoteSong.Size - 1);

                    // Demande du chunk au host distant.
                    var msg = await AskMediaAsync(remoteHost, remoteSong.Hash, start, end, ct);

                    // Vérifications de cohérence.
                    if (msg.StartByte != start)
                        throw new InvalidOperationException("Chunk hors séquence.");

                    if (string.IsNullOrWhiteSpace(msg.SongData))
                        throw new InvalidOperationException("Chunk vide.");

                    // Décoder base64 -> octets bruts.
                    var bytes = Convert.FromBase64String(msg.SongData);

                    // Écrire le fragment dans le fichier ".part".
                    await fs.WriteAsync(bytes, 0, bytes.Length, ct);
                    downloaded += bytes.Length;

                    // Progression en pourcentage.
                    int pct = (int)Math.Round(downloaded * 100.0 / remoteSong.Size);
                    if (pct > 100) pct = 100;
                    progress?.Report(pct);
                }
            }

            // Vérification d'intégrité : SHA256 du fichier téléchargé vs hash attendu.
            var computed = NormalizeHash(ComputeSha256Hex(tempPath));
            var expected = NormalizeHash(remoteSong.Hash);

            if (!string.Equals(computed, expected, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("SHA256 invalide après import.");

            // Import OK : renommer le fichier ".part" en fichier final utilisable.
            File.Move(tempPath, finalPath);
            progress?.Report(100);
            return finalPath;
        }

        private static string ComputeSha256Hex(string filePath)
        {
            // Calcul SHA256 sur le fichier complet sur disque.
            using var stream = System.IO.File.OpenRead(filePath);
            using var sha = SHA256.Create();
            var hash = sha.ComputeHash(stream);
            var sb = new StringBuilder(hash.Length * 2);
            foreach (var b in hash) sb.Append(b.ToString("X2"));
            return sb.ToString();
        }

        private static string NormalizeHash(string? h)
            => (h ?? "").Replace("-", "").Trim().ToUpperInvariant();

        public void Dispose() => _mqtt.Dispose();
    }
}
