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

        private readonly HashSet<string> _online =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public Protocole()
        {
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

            // Annonce + découverte au démarrage
            SayOnline();
            AskOnline();
        }

        private void HandleIncoming(Message msg)
        {
            if (msg == null) return;

            // Broadcast ou message pour moi
            if (!string.Equals(msg.Recipient, BroadcastRecipient, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(msg.Recipient, _selfName, StringComparison.OrdinalIgnoreCase))
                return;

            switch (msg.Action)
            {
                case "askOnline":
                    // On répond "online" en broadcast
                    SayOnline();
                    break;

                case "online":
                    if (!string.IsNullOrWhiteSpace(msg.Sender))
                    {
                        lock (_online) _online.Add(msg.Sender);
                    }
                    break;

                // Non implémenté ici
                case "askCatalog":
                case "sendCatalog":
                case "askMedia":
                case "sendMedia":
                default:
                    break;
            }
        }

        private void AskOnline()
        {
            var msg = new Message
            {
                Recipient = BroadcastRecipient,
                Sender = _selfName,
                Action = "askOnline"
            };

            _mqtt.Send(msg);
        }

        public void SayOnline()
        {
            var msg = new Message
            {
                Recipient = BroadcastRecipient,
                Sender = _selfName,
                Action = "online"
            };

            _mqtt.Send(msg);
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

        public List<ISong> AskCatalog(string name) => throw new NotImplementedException();
        public void SendCatalog(string name) => throw new NotImplementedException();
        public void AskMedia(ISong song, string name, int startByte, int endByte) => throw new NotImplementedException();
        public void SendMedia(ISong song, string name, int startByte, int endByte) => throw new NotImplementedException();

        public void Dispose() => _mqtt.Dispose();
    }
}
