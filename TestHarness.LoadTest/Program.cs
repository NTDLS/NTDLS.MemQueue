using NTDLS.MemQueue;
using System;
using System.Threading.Tasks;

namespace TestHarness.LoadTest
{
    internal class Program
    {
        static void Main(string[] args)
        {
            var client = new NMQClient();
            client.OnExceptionOccured += Client_OnExceptionOccured;

            string guid = Guid.NewGuid().ToString().Replace("-", "");

            client.Connect("localhost");

            client.OnMessageReceived += Client_OnMessageReceived;
            client.OnQueryReceived += Client_OnQueryReceived;

            client.Subscribe("TestQueue"); //We have to subscribe to a queue, otherwise we wont receive anything.

            int messagesSent = 0;
            int messagesReceived = 0;

            while (true)
            {
                try
                {
                    var query = new NMQQuery("TestQueue", "Ping");
                    //Console.WriteLine($"QUERY-SEND: Message: {query.Message}");

                    messagesSent++;

                    client.QueryAsync(query).ContinueWith((t) => //This enqueues a query that expects a reply:
                    {
                        if (t.Status == TaskStatus.RanToCompletion && t.Result != null)
                        {
                            messagesReceived++;
                            //Console.WriteLine($"REPLY-RECV: Message: {t.Result.Message}");
                        }
                        else
                        {
                            Console.WriteLine($"Something went wrong, failed to send.");
                        }
                    });

                    //This simply enqueues a one way message, no reply expected.

                    var message = new NMQMessage("TestQueue", "TestLabel", $"This is a message sent at {DateTime.Now:u}!");
                    //Console.WriteLine($"MSG-SEND: Message: {message.Message}");
                    client.Enqueue(message);

                    //System.Threading.Thread.Sleep(1);
                }
                catch
                {
                }

                Console.Write($"Rcvd: {messagesReceived}, Sent: {messagesSent}, TCPDepth: {client.TCPSendQueueDepth}   \r");
            }
        }

        private static void Client_OnExceptionOccured(NMQBase sender, Exception exception)
        {
            Console.WriteLine($"EXCEPTION: {exception.Message}");
        }

        static int queriresReceived = 0;

        //If we receive a query, reply to it.
        private static NMQQueryReplyResult Client_OnQueryReceived(NMQClient sender, NMQQuery query)
        {
            queriresReceived++;

            if (query.Message == "Ping")
            {
                return sender.Reply(query, new NMQReply("Pong"));
            }
            else
            {
                throw new NotImplementedException();
            }
        }

        //If we receive a message, just display it.
        private static void Client_OnMessageReceived(NMQClient sender, NMQMessage message)
        {
            //Console.WriteLine($"MSG-RECV: {message.Message}");
        }
    }
}
