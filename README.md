# NTDLS.MemQueue
In memory non-persistent message queue with messaging and query/reply support for inter-process-communication, queuing, load-balancing and buffering over TCP/IP.

Did I mention it has no external dependenies? Not even json. ¯\\_(ツ)_/¯

**Collaboration welcomed and encouraged**


>**Running the server:**
Running the server is literally two lines of code and can be run in the same process as the client.
The server does not have to be dedicated either, it can eimply be one of the process that is involved in inner-process-communication.

![image](https://user-images.githubusercontent.com/11428567/201763420-b1ee0205-48e5-4b77-81d0-1ad9df62d34e.png)


>**Enqueuing a notification. A message type which does not expect a reply.**
Enqueuing a notification (as we call them) is a one way message that is broadcast to all connected peer that have
subscribed to the queue.

![image](https://user-images.githubusercontent.com/11428567/201763480-11b0b2ef-1b9f-4e85-9f69-314b20c7321e.png)


>**Enqueuing a query, a message type that does expect a reply:**
You can also enque a query. The query will be received by a connected peer that is subscribed to the queue,
respond to the query and you will receive the reply in code.

![image](https://user-images.githubusercontent.com/11428567/201763602-12ba8b08-1346-4168-9d4c-2d119e463218.png)


>**Receiving a query and replying to it:**
Receiving a query and responding to it is easy. The server handles all the routing.

![image](https://user-images.githubusercontent.com/11428567/201763687-a3d0ccbc-e072-4861-b98e-69cb6fae6c31.png)
