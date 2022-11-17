# MemQueue
In memory non-persistent message queue with messaging, query/reply support and at least two message boradcast schemes for inter-process-communication, queuing, load-balancing and buffering over TCP/IP.

Did I mention it has no external dependenies? Not even json. ¯\\_(ツ)_/¯

**Collaboration welcomed and encouraged**


>:runner: **Running the server:**
>
>Running the server is literally two lines of code and can be run in the same process as the client.
>The server does not have to be dedicated either, it can eimply be one of the process that is involved in inner-process-communication.
```csharp
using MemQueue;

internal class Program
{
    static void Main()
    {
        var server = new NMQServer()
        {
            //There are lots of options if you want to get fancy.
        };

        server.Start(); //Start the server on the default port.
    }
}
```


>:loudspeaker:	**Enqueuing a notification. A message type which does not expect a reply.**
>
>Enqueuing a notification (as we call them) is a one way message that is broadcast to all connected peer that have
>subscribed to the queue.
```csharp
using MemQueue;
using System;

internal class Program
{
    static void Main()
    {
        var client = new NMQClient();

        client.Connect("localhost");

        var message = new NMQNotification("TestQueue", "TestLabel", $"This is a message sent at {DateTime.Now:u}!");
        client.Enqueue(message);
    }
}
```

>:eyes:	**Receiving a notification message:**
>
>Receiving a notification is easy. If you are subscribed to the queue, you will receive the message. Further messages will be held until the event method returns.
```csharp
using MemQueue;
using System;

internal class Program
{
    static void Main()
    {
        var client = new NMQClient();

        client.OnNotificationReceived += Client_OnNotificationReceived; //Setup the event handler
        client.Connect("localhost"); //Connect to the server on the default port.
        client.Subscribe("TestQueue"); //We have to subscribe to a queue, otherwise we wont receive anything.
    }

    private static void Client_OnNotificationReceived(NMQClient sender, NMQNotification notification)
    {
        Console.WriteLine($"Message received: {notification.Message}");
    }
}
```


>:mega:	**Enqueuing a query, a message type that does expect a reply:**
>
>You can also enque a query. The query will be received by a connected peer that is subscribed to the queue,
>respond to the query and you will receive the reply in code.
```csharp
using MemQueue;
using System;
using System.Threading.Tasks;

internal class Program
{
    static void Main()
    {
        var client = new NMQClient();

        client.Connect("localhost"); //Connect to the server on the default port.
        client.Subscribe("TestQueue"); //We have to subscribe to a queue, otherwise we wont receive anything.

        var query = new NMQQuery("TestQueue", "Ping");

        //Enqueue a query and wait for the reply.
        client.QueryAsync(query).ContinueWith((t) =>
        {
            if (t.Status == TaskStatus.RanToCompletion && t.Result != null)
            {
                Console.WriteLine($"Received message: {t.Result.Message}.");
            }
        });
    }
}
```


>:massage_man:	**Receiving a query and replying to it:**
>
>Receiving a query and responding to it is easy. The server handles all the routing.
```csharp
using MemQueue;
using System;

internal class Program
{
    static void Main()
    {
        var client = new NMQClient();

        client.OnQueryReceived += Client_OnQueryReceived; //Setup the event handler.
        client.Connect("localhost"); //Connect to the server on the default port.
        client.Subscribe("TestQueue"); //We have to subscribe to a queue, otherwise we wont receive anything.
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
}
```

