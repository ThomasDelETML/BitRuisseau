using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;

namespace BitRuisseau
{
    // Morceau distant (affichage + futur AskMedia)
    internal class RemoteSong : ISong
    {
        // Pour matcher ton JSON ("Path")
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

        private readonly MqttCommunicator _mqtt;
        private readonly string _selfName;
        private readonly LocalMediaLibrary _localLibrary;

        private readonly HashSet<string> _online =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Catalogues reçus : sender -> list
        private readonly Dictionary<string, List<RemoteSong>> _catalogs =
            new Dictionary<string, List<RemoteSong>>(StringComparer.OrdinalIgnoreCase);

        // Attentes en cours : sender -> wait handle (AskCatalog)
        private readonly Dictionary<string, ManualResetEventSlim> _catalogWaiters =
            new Dictionary<string, ManualResetEventSlim>(StringComparer.OrdinalIgnoreCase);

        private readonly object _lock = new object();

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

            // Annonce + découverte
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
                    {
                        lock (_lock) _online.Add(msg.Sender);
                    }
                    break;

                case "askCatalog":
                    // Quelqu'un demande mon catalogue -> je lui envoie
                    if (!string.IsNullOrWhiteSpace(msg.Sender))
                        SendCatalog(msg.Sender);
                    break;

                case "sendCatalog":
                    // Réception d’un catalogue
                    if (!string.IsNullOrWhiteSpace(msg.Sender))
                        ReceiveCatalog(msg.Sender, msg.SongList);
                    break;

                default:
                    break;
            }
        }

        private void ReceiveCatalog(string sender, List<SongDto>? list)
        {
            var converted = (list ?? new List<SongDto>())
                .Select(dto => new RemoteSong(dto))
                .ToList();

            ManualResetEventSlim? waiter = null;

            lock (_lock)
            {
                _catalogs[sender] = converted;
                if (_catalogWaiters.TryGetValue(sender, out waiter))
                {
                    // signale AskCatalog()
                }
            }

            waiter?.Set();
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

        public List<ISong> AskCatalog(string name)
        {
            // 1) crée/replace un waiter
            ManualResetEventSlim waiter;
            lock (_lock)
            {
                if (_catalogWaiters.TryGetValue(name, out var old))
                {
                    old.Dispose();
                }

                waiter = new ManualResetEventSlim(false);
                _catalogWaiters[name] = waiter;
            }

            // 2) envoie la demande
            _mqtt.Send(new Message
            {
                Recipient = name, // destinataire = hostname distant
                Sender = _selfName,
                Action = "askCatalog"
            });

            // 3) attend la réponse (timeout)
            var ok = waiter.Wait(TimeSpan.FromSeconds(3));

            // 4) cleanup waiter
            lock (_lock)
            {
                if (_catalogWaiters.TryGetValue(name, out var w) && ReferenceEquals(w, waiter))
                {
                    _catalogWaiters.Remove(name);
                }
            }
            waiter.Dispose();

            if (!ok)
            {
                // pas reçu -> retourne vide
                return new List<ISong>();
            }

            lock (_lock)
            {
                if (_catalogs.TryGetValue(name, out var songs))
                    return songs.Cast<ISong>().ToList();
            }

            return new List<ISong>();
        }

        public void SendCatalog(string name)
        {
            // Construit un catalogue sérialisable (SongDto)
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
                SongList = catalog,
                StartByte = null,
                EndByte = null,
                SongData = null,
                Hash = null
            });
        }

        public void AskMedia(ISong song, string name, int startByte, int endByte)
            => throw new NotImplementedException();

        public void SendMedia(ISong song, string name, int startByte, int endByte)
            => throw new NotImplementedException();

        public void Dispose() => _mqtt.Dispose();
    }
}
