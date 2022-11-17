using NTDLS.MemQueue;
using System;

namespace TestHarness.Chat
{
    internal class Program
    {
        static void Main()
        {
            var client = new NMQClient();

            client.OnNotificationReceived += Client_OnMessageReceived;
            
            client.Connect("localhost"); //Connect to the local host, make sure that [TestHarness.Server] is running.
            client.Subscribe("Chatroom"); //Subscribe to the queue. If it does not exist, it will be created.

            Console.WriteLine("Be sure to open more than one chat console...");

            while (true)
            {
                var text = Console.ReadLine();
                client.Enqueue(new NMQNotification("Chatroom", text));
            }
        }

        private static void Client_OnMessageReceived(NMQClient sender, NMQNotification notification)
        {
            if (notification.PeerId != sender.PeerId) //Dont care to see message from ourself.
            {
                Console.WriteLine($"{notification.Message}");
            }
        }
    }
}
