using System;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Client.Options;
using MQTTnet.Protocol;

namespace BitRuisseau
{
    /// <summary>
    /// Wrapper MQTT simple pour envoyer/recevoir des Message (JSON) sur un topic.
    /// </summary>
    public class MqttCommunicator : IDisposable
    {
        private const int DefaultPort = 1883;

        private readonly string _brokerHost;
        private readonly int _brokerPort;
        private readonly string _nodeId;
        private readonly string _topic;

        private readonly MqttClientFactory _factory = new();
        private IMqttClient _mqttClient;

        public Action<Message>? OnMessageReceived { get; set; }

        public MqttCommunicator(string brokerHost,
                                string? nodeId = null,
                                string? topic = null,
                                int brokerPort = DefaultPort)
        {
            _brokerHost = brokerHost;
            _brokerPort = brokerPort;
            _nodeId = nodeId ?? Dns.GetHostName();
            _topic = topic ?? Config.TOPIC;

            _mqttClient = _factory.CreateMqttClient();
        }

        /// <summary>
        /// Démarre la connexion MQTT et la souscription au topic.
        /// </summary>
        public void Start()
        {
            // Contexte de synchro éventuellement utile si on veut remonter les erreurs au thread UI
            var syncContext = SynchronizationContext.Current;

            _mqttClient.ApplicationMessageReceivedAsync += async e =>
            {
                var payload = Encoding.UTF8.GetString(e.ApplicationMessage.PayloadSegment);

                try
                {
                    var msg = JsonSerializer.Deserialize<Message>(payload);
                    if (msg != null)
                    {
                        OnMessageReceived?.Invoke(msg);
                    }
                }
                catch
                {
                    // On ignore les messages invalides
                }

                await Task.CompletedTask;
            };

            Connect();

            var subscribeOptions = _factory
                .CreateSubscribeOptionsBuilder()
                .WithTopicFilter(_topic,
                    MqttQualityOfServiceLevel.AtLeastOnce,
                    retainAsPublished: false,
                    retainHandling: MqttRetainHandling.SendAtSubscribe)
                .Build();

            var subscriptionResult = _mqttClient.SubscribeAsync(subscribeOptions).Result;
            if (subscriptionResult.Items.Count <= 0)
            {
                throw new InvalidOperationException("Failed to subscribe to the MQTT topic.");
            }
        }

        private void Connect()
        {
            var options = new MqttClientOptionsBuilder()
                .WithClientId(_nodeId)
                .WithTcpServer(_brokerHost, _brokerPort)
                .Build();

            var connectResult = _mqttClient.ConnectAsync(options).Result;
            if (connectResult.ResultCode != MqttClientConnectResultCode.Success)
            {
                throw new InvalidOperationException(
                    $"Failed to connect to the MQTT broker. Reason: {connectResult.ReasonString}");
            }
        }

        /// <summary>
        /// Envoie un Message sur le topic (en JSON).
        /// </summary>
        public void Send(Message msg)
        {
            if (!_mqttClient.IsConnected)
            {
                Connect();
            }

            var payload = JsonSerializer.Serialize(msg);

            var appMessage = new MqttApplicationMessageBuilder()
                .WithTopic(_topic)
                .WithRetainFlag(false)
                .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
                .WithPayload(payload)
                .Build();

            var publishResult = _mqttClient.PublishAsync(appMessage).Result;
            if (!publishResult.IsSuccess)
            {
                throw new InvalidOperationException(
                    $"Failed to publish MQTT message. Reason: {publishResult.ReasonString}");
            }
        }

        public void Dispose()
        {
            try
            {
                _mqttClient.DisconnectAsync().Wait(1000);
            }
            catch
            {
                // ignore
            }
        }
    }
}
