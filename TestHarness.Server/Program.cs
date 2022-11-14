using NTDLS.MemQueue;
using System;

namespace TestHarness.Server
{
    internal class Program
    {
        static int messagesReceived = 0;
        static int messagesSent = 0;

        static void Main(string[] args)
        {
            var server = new NMQServer()
            {
                StaleItemExpirationSeconds = 600
            };

            //These events are 100% unnecessary, just wanted to show some status text.
            server.OnExceptionOccured += Server_OnExceptionOccured;
            server.OnBeforeMessageReceive += Server_OnBeforeCommandReceive;
            server.OnBeforeMessageSend += Server_OnBeforeCommandSend;

            server.Start();

            Console.WriteLine($"Server running on port {server.ListenPort}.");
            Console.WriteLine($"Awaiting connections...");
        }

        private static PayloadSendAction Server_OnBeforeCommandSend(NMQServer sender, NMQMessageBase message)
        {
            messagesSent++;
            Console.Write($"Rcvd: {messagesReceived}, Sent: {messagesSent}, QDepth: {sender.QueueDepth()}, TCPDepth: {sender.TCPSendQueueDepth}, O:{sender.OutstandingAcknowledgments}, U:{sender.UnacknowledgedCommands}   \r");
            return PayloadSendAction.Process;
        }

        private static PayloadReceiveAction Server_OnBeforeCommandReceive(NMQServer sender, NMQMessageBase message)
        {
            messagesReceived++;
            Console.Write($"Rcvd: {messagesReceived}, Sent: {messagesSent}, QDepth: {sender.QueueDepth()}, TCPDepth: {sender.TCPSendQueueDepth}, O:{sender.OutstandingAcknowledgments}, U:{sender.UnacknowledgedCommands}   \r");
            return PayloadReceiveAction.Process;
        }

        private static void Server_OnExceptionOccured(NMQServer sender, Exception exception)
        {
            Console.WriteLine($"Exception {exception.Message}");
        }
    }
}
