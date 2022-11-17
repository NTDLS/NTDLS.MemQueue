using NTDLS.MemQueue;
using System;

namespace TestHarness.Chat
{
    internal class Program
    {
        static void Main()
        {
            var queue = new NMQLocalOnly(); //Manage the server and the client for us, default port.

            queue.Client.OnNotificationReceived += Client_OnNotificationReceived;
            queue.Client.Subscribe("Chatroom");

            Console.WriteLine("Be sure to open more than one chat console...");

            while (true)
            {
                var text = Console.ReadLine();
                queue.Client.Enqueue(new NMQNotification("Chatroom", text));
            }
        }

        private static void Client_OnNotificationReceived(NMQClient sender, NMQNotification notification)
        {
            if (notification.PeerId != sender.PeerId) //Dont care to see message from ourself.
            {
                Console.WriteLine($"{notification.Message}");
            }
        }
    }
}
