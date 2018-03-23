using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;

namespace AspnetCoreModule.TestSites.Standard
{
    public class WebSocketConnections
    {
        private static int lastIndex = 0;
        private static Object thisLock = new Object();
        public static string CloseFromServerCmd = "CloseFromServer";
        public static string ClosingFromServer = "ClosingFromServer";
        private static bool _closeAllStarted = false;
        private static Dictionary<int, WebSocket> _webSockets = new Dictionary<int, WebSocket>();

        public static int Add(WebSocket webSocket)
        {
            int webSocketHandle = -1;
            if (_closeAllStarted)
            {
                return -1;
            }

            lock (thisLock)
            {
                webSocketHandle = lastIndex++;
            }
            _webSockets.Add(webSocketHandle, webSocket);
            return webSocketHandle;
        }

        public static bool Remove(int webSocketHandle)
        {
            if (_closeAllStarted)
            {
                return false;
            }

            _webSockets.Remove(webSocketHandle);
            return true;
        }

        public async static void CloseAll()
        {
            _closeAllStarted = true;
            var buffer = new byte[1024 * 4];

            try
            {
                // send close message to client
                foreach (KeyValuePair<int, WebSocket> entry in _webSockets)
                {
                    await entry.Value.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, ClosingFromServer, CancellationToken.None);
                }
                _webSockets.Clear();
            }
            catch (Exception ex)
            {
                Console.WriteLine("CloseAll() run into exception error!!! " + ex.Message);
            }
            _closeAllStarted = false;
        }
    }
}
