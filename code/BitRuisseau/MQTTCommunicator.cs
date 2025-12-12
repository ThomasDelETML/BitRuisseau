using MQTTnet;
using MQTTnet.Protocol;
using System;
using System.Buffers;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace BitRuisseau
{
    public class MqttCommunicator : IDisposable
    {
        private const int DefaultPort = 1883;
        private const string DefaultTopic = "BitRuisseau";

        private readonly string _brokerHost;
        private readonly int _brokerPort;
        private readonly string _nodeId;
        private readonly string _topic;
        private readonly string? _username;
        private readonly string? _password;

        private readonly MqttClientFactory _factory = new();
        private readonly IMqttClient _mqttClient;

        public Action<Message>? OnMessageReceived { get; set; }

        // Overload compatible avec new MqttCommunicator("host", "T")
        public MqttCommunicator(string brokerHost, string nodeId)
            : this(brokerHost, nodeId, DefaultTopic, DefaultPort, null, null)
        {
        }

        public MqttCommunicator(
            string brokerHost,
            string nodeId,
            string topic,
            int brokerPort = DefaultPort,
            string? username = null,
            string? password = null)
        {
            _brokerHost = brokerHost;
            _brokerPort = brokerPort;
            _nodeId = string.IsNullOrWhiteSpace(nodeId) ? Dns.GetHostName() : nodeId;
            _topic = string.IsNullOrWhiteSpace(topic) ? DefaultTopic : topic;

            _username = username;
            _password = password;

            _mqttClient = _factory.CreateMqttClient();
        }

        public void Start()
        {
            _mqttClient.ApplicationMessageReceivedAsync += e =>
            {
                try
                {
                    var payload = e.ApplicationMessage.Payload; // ReadOnlySequence<byte> (MQTTnet 4.x)

                    if (payload.IsEmpty)
                        return Task.CompletedTask;

                    var json = Encoding.UTF8.GetString(payload.ToArray());
                    var msg = JsonSerializer.Deserialize<Message>(json);

                    if (msg != null)
                        OnMessageReceived?.Invoke(msg);
                }
                catch
                {
                    // ignore
                }

                return Task.CompletedTask;
            };

            Connect();

            var subscribeOptions = _factory
                .CreateSubscribeOptionsBuilder()
                .WithTopicFilter(_topic, MqttQualityOfServiceLevel.AtLeastOnce)
                .Build();

            var subResult = _mqttClient.SubscribeAsync(subscribeOptions)
                                      .GetAwaiter()
                                      .GetResult();

            if (subResult.Items.Count == 0)
                throw new InvalidOperationException("Failed to subscribe to MQTT topic.");
        }

        private void Connect()
        {
            try
            {
                var builder = new MqttClientOptionsBuilder()
                    .WithClientId(_nodeId)
                    .WithTcpServer(_brokerHost, _brokerPort)
                    .WithCleanSession();

                if (!string.IsNullOrEmpty(_username))
                {
                    // IMPORTANT: certains brokers n'aiment pas password = null
                    builder = builder.WithCredentials(_username, _password ?? string.Empty);
                }

                var options = builder.Build();

                var result = _mqttClient.ConnectAsync(options)
                                        .GetAwaiter()
                                        .GetResult();

                if (result.ResultCode != MqttClientConnectResultCode.Success)
                {
                    throw new InvalidOperationException(
                        $"MQTT connect failed. ResultCode={result.ResultCode}, Reason='{result.ReasonString}'");
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"MQTT connect exception to {_brokerHost}:{_brokerPort} clientId='{_nodeId}': {ex.Message}",
                    ex);
            }
        }

        public void Send(Message msg)
        {
            if (!_mqttClient.IsConnected)
                Connect();

            var json = JsonSerializer.Serialize(msg);

            var appMessage = new MqttApplicationMessageBuilder()
                .WithTopic(_topic)
                .WithPayload(json)
                .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
                .WithRetainFlag(false)
                .Build();

            var pub = _mqttClient.PublishAsync(appMessage)
                                 .GetAwaiter()
                                 .GetResult();

            if (!pub.IsSuccess)
            {
                throw new InvalidOperationException(
                    $"Failed to publish MQTT message. Reason='{pub.ReasonString}'");
            }
        }

        public void Dispose()
        {
            try
            {
                if (_mqttClient.IsConnected)
                    _mqttClient.DisconnectAsync().GetAwaiter().GetResult();
            }
            catch
            {
                // ignore
            }
        }
    }
}
