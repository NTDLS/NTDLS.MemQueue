using MemQueue;
using System;
using System.Threading;

namespace TestHarness.Server
{
    internal class Program
    {
        static void Main()
        {
            int messagesReceived = 0;
            int messagesSent = 0;

            var server = new NMQServer()
            {
                BrodcastScheme = BrodcastScheme.Uniform
            };

            //These events are 100% unnecessary, just wanted to show some status text.
            server.OnExceptionOccured += (sender, exception) => { Console.WriteLine($"Exception {exception.Message}"); };
            server.OnBeforeMessageReceive += (sender, message) => { messagesReceived++; return PayloadInterceptAction.Process; };
            server.OnBeforeMessageSend += (sender, message) => { messagesSent++; return PayloadInterceptAction.Process; };

            server.Start(); //Start the server on the default port.

            Console.WriteLine($"Server running on port {server.ListenPort}.");
            Console.WriteLine("Press any key to shutdown.");

            while (!Console.KeyAvailable) //Press any key to close.
            {
                Console.Write($"Sent: {messagesSent}, Rcvd: {messagesReceived}, QDepth: {server.QueueDepth()}, Unacknowledged:{server.OutstandingCommandAcknowledgments}, Dead:{server.PresumedDeadCommandCount}   \r");
                Thread.Sleep(100);
            }

            server.Stop(); //Stop the server.
        }
    }
}
