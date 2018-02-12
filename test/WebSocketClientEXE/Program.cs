using AspNetCoreModule.Test.Framework;
using AspNetCoreModule.Test.WebSocketClient;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace WebSocketClientEXE
{
    class Program
    {
        static void Main(string[] args)
        {
            
            string parameter = null;
            foreach (string item in args) { parameter += item; }

            if (!parameter.ToLower().Contains("http"))
            {
                TestUtility.LogInformation("Usage example: WebSocketClientEXE http://localhost:40000/aspnetcoreapp/websocket [-quiet]");
                return;
            }

            if (parameter.ToLower().Contains("-quiet"))
            {
                TestUtility.VerboseMode = false;
            }

            using (WebSocketClientHelper websocketClient = new WebSocketClientHelper())
            {
                string url = "http://localhost:40000/aspnetcoreapp/websocket";
                if (args[0].Contains("http"))
                {
                    url = args[0];
                }
                
                string consoleInput = null;
                int repeatcount = 0;
                while (true)
                {
                    if (consoleInput != null && consoleInput.ToLower() == "q")
                    {
                        // terminate if users entered "Q"
                        break;
                    }

                    if (consoleInput == null)
                    {
                        // initialize with the first connect command
                        consoleInput = "connect";
                    }
                    else
                    {
                        if (repeatcount <= 0 || consoleInput == "")
                        {
                            // 'q' to quit, 'close' or 'CloseFromServer' to disconnect, 'connect' to connect, 'repeat;<count>' to repeat the next command
                            Console.WriteLine("Type any data to send (Commands: 'q' to quit, 'close' or 'CloseFromServer', 'connect' 'repeat;<count>'");
                            consoleInput = Console.ReadLine();

                            string[] tempTokens = consoleInput.Split(new char[] { ';' });
                            
                            if (tempTokens.Length == 2 && tempTokens[0].ToLower() == "repeat")
                            {
                                repeatcount = Convert.ToInt32(tempTokens[1]);
                                TestUtility.LogInformation("Initialzing repeat count " + repeatcount + "...");
                                consoleInput = "";
                                continue;
                            }
                        }
                        else
                        {
                            TestUtility.LogInformation("#### Repeating " + repeatcount);
                            repeatcount--;
                        }
                    }
                                        
                    string[] tokens = consoleInput.Split(new char[] { ';' });
                    Frame frameReturned = null;

                    for (int i = 0; i < tokens.Length; i++)
                    {
                        if (!websocketClient.IsOpened)
                        {
                            TestUtility.LogInformation("Connection closed...");
                        }

                        string data = tokens[i];
                        string temp = data.Trim().ToLower();

                        if (temp == "connect")
                        {

                            frameReturned = websocketClient.Connect(
                            new Uri(url),   // target url
                            true,           // store data
                            true);          // always reading

                            TestUtility.LogInformation(frameReturned.Content);
                            continue;
                        }

                        if (temp == "q" || temp == "close")
                        {
                            if (!websocketClient.IsOpened)
                            {
                                TestUtility.LogInformation("Connection is already closed, skipping websocket close handshaking...");
                                if (!websocketClient.WaitForWebSocketState(WebSocketState.ConnectionClosed))
                                {
                                    throw new Exception("Failed to close a connection");
                                }
                            }
                            else
                            {
                                frameReturned = websocketClient.Close();
                                TestUtility.LogInformation(frameReturned.Content);
                            }

                            if (temp == "q")
                            {
                                break;
                            }
                            continue;
                        }

                        if (temp == "ping")
                        {
                            websocketClient.SendPing();
                            continue;
                        }

                        if (temp == "pong")
                        {
                            websocketClient.SendPong();
                            continue;
                        }

                        if (temp.StartsWith("["))
                        {
                            websocketClient.SendTextData(data, 0x01);  // 0x01: start of sending partial data
                            continue;
                        }

                        if (temp.EndsWith("]"))
                        {
                            websocketClient.SendTextData(data, 0x80);  // 0x80: end of sending partial data
                            continue;
                        }

                        websocketClient.SendTextData(data);
                    }
                }
            }
        }
    }
}
