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

        public static Dictionary<int, WebSocket> WebSockets = new Dictionary<int, WebSocket>();

        public static int GetLastIndex()
        {
            lock (thisLock)
            {
                lastIndex++;
                return lastIndex;
            }
        }

        public async static void CloseAll()
        {
            var buffer = new byte[1024 * 4];

            // send close message to client
            foreach (KeyValuePair<int, WebSocket> entry in WebSockets)
            {
                await entry.Value.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, ClosingFromServer, CancellationToken.None);
            }
        }
    }
}
