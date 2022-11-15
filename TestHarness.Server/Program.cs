using NTDLS.MemQueue;
using System;
using System.Threading;

namespace TestHarness.Server
{
    internal class Program
    {
        static int messagesReceived = 0;
        static int messagesSent = 0;

        static void Main()
        {
            var server = new NMQServer()
            {
                StaleItemExpirationSeconds = 600,
                BrodcastScheme = BrodcastScheme.Uniform
            };

            //These events are 100% unnecessary, just wanted to show some status text.
            server.OnExceptionOccured += Server_OnExceptionOccured;
            server.OnBeforeMessageReceive += Server_OnBeforeCommandReceive;
            server.OnBeforeMessageSend += Server_OnBeforeCommandSend;

            server.Start(); //Start the server on the default port.

            Console.WriteLine($"Server running on port {server.ListenPort}.");
            Console.WriteLine($"Awaiting connections...");
            Console.WriteLine("...");
            Console.WriteLine("Press any key to shutdown.");

            while (!Console.KeyAvailable) //Press any key to close.
            {
                Console.Write($"Sent: {messagesSent}, Rcvd: {messagesReceived}, QDepth: {server.QueueDepth()}, Unacknowledged:{server.OutstandingCommandAcknowledgments}, Dead:{server.PresumedDeadCommandCount}   \r");
                Thread.Sleep(100);
            }

            server.Stop(); //Stop the server.
        }

        private static PayloadSendAction Server_OnBeforeCommandSend(NMQServer sender, NMQMessageBase message)
        {
            messagesSent++;
            return PayloadSendAction.Process;
        }

        private static PayloadReceiveAction Server_OnBeforeCommandReceive(NMQServer sender, NMQMessageBase message)
        {
            messagesReceived++;
            return PayloadReceiveAction.Process;
        }

        private static void Server_OnExceptionOccured(NMQServer sender, Exception exception)
        {
            Console.WriteLine($"Exception {exception.Message}");
        }
    }
}
