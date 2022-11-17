using MemQueue;
using System;

namespace TestHarness.Chat
{
    internal class Program
    {
        static void Main(string[] args)
        {
            var client = new NMQClient();
            client.OnNotificationReceived += Client_OnMessageReceived;
            client.Connect("localhost");

            client.Subscribe("Chatroom");

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
