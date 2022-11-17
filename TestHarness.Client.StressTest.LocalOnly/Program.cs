using NTDLS.MemQueue;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace TestHarness.LoadTest
{
    internal class Program
    {
        private static int messagesSent = 0;
        private static int messagesReceived = 0;

        static void Main()
        {
            var local = new NMQLocalOnly();

            local.Client.OnExceptionOccured += (sender, exception) => { Console.WriteLine($"Exception {exception.Message}"); };
            local.Client.OnNotificationReceived += Client_OnNotificationReceived;
            local.Client.OnQueryReceived += Client_OnQueryReceived;

            local.Client.Subscribe("TestQueue"); //We have to subscribe to a queue, otherwise we wont receive anything.

            while (!Console.KeyAvailable) //Press any key to close.
            {
                try
                {
                    var query = new NMQQuery("TestQueue", "Ping");

                    messagesSent++;

                    //Enqueue a query and waits for the reply.
                    local.Client.QueryAsync(query).ContinueWith((t) =>
                    {
                        if (t.Status == TaskStatus.RanToCompletion && t.Result != null)
                        {
                            messagesReceived++;
                        }
                        else
                        {
                            Console.WriteLine($"Something went wrong, failed to send.");
                        }
                    });

                    //This simply enqueues a one way message, no reply expected.
                    var message = new NMQNotification("TestQueue", "TestLabel", $"This is a message sent at {DateTime.Now:u}!");
                    local.Client.Enqueue(message);
                    messagesSent++;
                }
                catch
                {
                }

                Console.Write($"Sent: {messagesSent}, Rcvd: {messagesReceived}, Unacknowledged:{local.Client.OutstandingAcknowledgments}: Dead:{local.Client.PresumedDeadCommandCount}   \r");
                Thread.Sleep(10);
            }

            local.Client.Disconnect();
        }

        private static NMQQueryReplyResult Client_OnQueryReceived(NMQClient sender, NMQQuery query)
        {
            if (query.Message == "Ping") //We receive a query with the message "Ping", reply with "Pong".
            {
                return sender.Reply(query, new NMQReply("Pong"));
            }
            else
            {
                throw new NotImplementedException();
            }
        }

        private static void Client_OnNotificationReceived(NMQClient sender, NMQNotification notification)
        {
            messagesReceived++;
        }
    }
}
