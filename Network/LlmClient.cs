using System;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace FirstMod.Network
{
    public class LlmClient
    {
        private static LlmClient? _instance;
        public static LlmClient Instance => _instance ??= new LlmClient();

        private ClientWebSocket _webSocket = new ClientWebSocket();

        public async Task ConnectAsync()
        {
            if (_webSocket.State != WebSocketState.Open)
            {
                _webSocket = new ClientWebSocket();
                try
                {
                    await _webSocket.ConnectAsync(new Uri("ws://localhost:8000/ws/game"), CancellationToken.None);
                    Console.WriteLine("[LLM Mod] Connected to Python LLM Server!");
                }
                catch (Exception e)
                {
                    Console.WriteLine($"[LLM Mod] Failed to connect: {e.Message}");
                }
            }
        }

        public async Task<string?> SendStateAndGetResponseAsync(object state)
        {
            if (_webSocket.State != WebSocketState.Open)
            {
                await ConnectAsync();
            }

            if (_webSocket.State == WebSocketState.Open)
            {
                // Serialize game state to JSON
                string jsonState = JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = false });
                var sendBuffer = Encoding.UTF8.GetBytes(jsonState);

                // Send to Python
                await _webSocket.SendAsync(new ArraySegment<byte>(sendBuffer), WebSocketMessageType.Text, true, CancellationToken.None);

                // Wait for response 
                var receiveBuffer = new byte[1024 * 64];
                var result = await _webSocket.ReceiveAsync(new ArraySegment<byte>(receiveBuffer), CancellationToken.None);

                string responseJson = Encoding.UTF8.GetString(receiveBuffer, 0, result.Count);
                return responseJson;
            }
            return null;
        }

        public async Task SendEventOnlyAsync(object eventData)
        {
            if (_webSocket.State != WebSocketState.Open)
            {
                await ConnectAsync();
            }

            if (_webSocket.State == WebSocketState.Open)
            {
                string jsonState = JsonSerializer.Serialize(eventData, new JsonSerializerOptions { WriteIndented = false });
                var sendBuffer = Encoding.UTF8.GetBytes(jsonState);
                await _webSocket.SendAsync(new ArraySegment<byte>(sendBuffer), WebSocketMessageType.Text, true, CancellationToken.None);
            }
        }
    }
}
