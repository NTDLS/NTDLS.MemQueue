using NTDLS.MemQueue;
using System;

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
                StaleItemExpirationSeconds = 600
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
            Console.ReadLine();

            server.Stop(); //Stop the server.
        }

        private static PayloadSendAction Server_OnBeforeCommandSend(NMQServer sender, NMQMessageBase message)
        {
            messagesSent++;
            Console.Write($"Sent: {messagesSent}, Rcvd: {messagesReceived}, QDepth: {sender.QueueDepth()}, Unacknowledged:{sender.OutstandingAcknowledgments}, Dead:{sender.PresumedDeadCommandCount}   \r");
            return PayloadSendAction.Process;
        }

        private static PayloadReceiveAction Server_OnBeforeCommandReceive(NMQServer sender, NMQMessageBase message)
        {
            messagesReceived++;
            Console.Write($"Sent: {messagesSent}, Rcvd: {messagesReceived}, QDepth: {sender.QueueDepth()}, Unacknowledged:{sender.OutstandingAcknowledgments}, Dead:{sender.PresumedDeadCommandCount}   \r");
            return PayloadReceiveAction.Process;
        }

        private static void Server_OnExceptionOccured(NMQServer sender, Exception exception)
        {
            Console.WriteLine($"Exception {exception.Message}");
        }
    }
}
