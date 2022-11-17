using NTDLS.MemQueue;
using System;

namespace TestHarness.Chat
{
    internal class Program
    {
        static void Main()
        {
            var local = new NMQLocalOnly(); //Manage the server and the client for us, default port.

            local.Client.OnNotificationReceived += Client_OnNotificationReceived;
            local.Client.Subscribe("Chatroom"); //Subscribe to the queue. If it does not exist, it will be created.

            Console.WriteLine("Be sure to open more than one chat console...");

            while (true)
            {
                var text = Console.ReadLine();
                local.Client.Enqueue(new NMQNotification("Chatroom", text));
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
