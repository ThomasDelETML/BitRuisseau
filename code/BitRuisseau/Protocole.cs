using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;

namespace BitRuisseau
{
    internal class Protocole : IProtocol, IDisposable
    {
        private const string Topic = "BitRuisseau";
        private const string BroadcastRecipient = "0.0.0.0";

        private readonly MqttCommunicator _mqtt;
        private readonly string _selfName;

        // On garde un accès à la médiathèque locale (définie dans MainForm.cs)
        private readonly LocalMediaLibrary _localLibrary;

        private readonly HashSet<string> _online =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public Protocole(LocalMediaLibrary localLibrary)
        {
            _localLibrary = localLibrary;

            _selfName = Dns.GetHostName();

            _mqtt = new MqttCommunicator(
                brokerHost: "mqtt.blue.section-inf.ch",
                nodeId: $"{_selfName}-T",
                topic: Topic,
                brokerPort: 1883,
                username: "ict",
                password: "321"
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
                    {
                        lock (_online) _online.Add(msg.Sender);
                    }
                    break;

                case "askCatalog":
                    // Quelqu'un demande mon catalogue => je l'envoie au sender
                    if (!string.IsNullOrWhiteSpace(msg.Sender))
                        SendCatalog(msg.Sender);
                    break;

                case "sendCatalog":
                    // Réception du catalogue (tu l'exploiteras ensuite côté UI)
                    // msg.SongList contient la liste de SongDto.
                    break;

                default:
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
            lock (_online)
            {
                return _online
                    .Where(x => !string.Equals(x, _selfName, StringComparison.OrdinalIgnoreCase))
                    .ToArray();
            }
        }

        public List<ISong> AskCatalog(string name)
        {
            // Envoi uniquement (la réception/attente sera faite après si tu veux)
            _mqtt.Send(new Message
            {
                Recipient = name,      // destinataire précis
                Sender = _selfName,
                Action = "askCatalog"
            });

            return new List<ISong>();
        }

        public void SendCatalog(string name)
        {
            // Construit un catalogue sérialisable
            var catalog = (_localLibrary.Songs ?? new List<Song>())
                .Select(s => new SongDto
                {
                    Path = s.FilePath ?? "",          // "Path" comme dans ton exemple
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

            var msg = new Message
            {
                Recipient = name,        // ex: "INF-B21-M209"
                Sender = _selfName,      // hostname (comme ton message online)
                Action = "sendCatalog",
                SongList = catalog,
                StartByte = null,
                EndByte = null,
                SongData = null,
                Hash = null
            };

            _mqtt.Send(msg);
        }

        public void AskMedia(ISong song, string name, int startByte, int endByte)
            => throw new NotImplementedException();

        public void SendMedia(ISong song, string name, int startByte, int endByte)
            => throw new NotImplementedException();

        public void Dispose() => _mqtt.Dispose();
    }
}
