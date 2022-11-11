using NTDLS.MemQueue.Library;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace NTDLS.MemQueue
{
    /// <summary>
    /// Client class for communicating with the server and thereby communicating with other clients.
    /// </summary>
    public class NMQClient : NMQBase
    {
        /// <summary>
        /// User data, use as you will.
        /// </summary>
        public object Tag { get; set; }

        #region Backend Variables.

        public Guid ClientId { get; private set; } = Guid.NewGuid();

        private bool _continueRunning = false;
        private Socket _connectSocket;
        private AsyncCallback _onDataReceivedCallback;
        private IPAddress _serverIpAddress;
        private int _serverPort;
        private List<string> _subscribedQueues = new List<string>();
        private object _reconnectThreadLock = new object();
        private List<NMQQuerySubscription> messageQuerySubscriptions = new List<NMQQuerySubscription>();

        #endregion

        #region Events.
        public override void LogException(Exception ex)
        {
            OnExceptionOccured?.Invoke(this, ex);
        }

        /// <summary>
        /// Allows excpetions to be logged.
        /// </summary>
        public event ExceptionOccuredEvent OnExceptionOccured;
        public delegate void ExceptionOccuredEvent(NMQBase sender, Exception exception);

        /// <summary>
        /// Receives replies to a query which were sent to the queue [Query(...)].
        /// </summary>
        public event QueryReplyReceivedEvent OnQueryReplyReceived;
        public delegate void QueryReplyReceivedEvent(NMQClient sender, NMQReply reply, bool hasAssociatedOpenQuery);

        /// <summary>
        /// Receives queries which were sent to the queue via [Query(...)], if subscribed.
        /// </summary>
        public event QueryReceivedEvent OnQueryReceived;
        public delegate NMQQueryReplyResult QueryReceivedEvent(NMQClient sender, NMQQuery query);

        /// <summary>
        /// Receives messages which were sent to the queue via [EnqueueMessage(...)].
        /// </summary>
        public event MessageReceivedEvent OnMessageReceived;
        public delegate void MessageReceivedEvent(NMQClient sender, NMQMessage message);

        /// <summary>
        /// Notify when the client connects to the server. The even is also called on subsequent automatically reconnects.
        /// </summary>
        public event ConnectedEvent OnConnected;
        public delegate void ConnectedEvent();

        /// <summary>
        /// Notify when the client disconnects from the server.
        /// </summary>
        public event DisconnectedEvent OnDisconnected;
        public delegate void DisconnectedEvent(NMQClient sender, bool willRetry);

        /// <summary>
        /// Notify when an item is enqueued by this client.
        /// </summary>
        public event EnqueuedEvent OnEnqueued;
        public delegate void EnqueuedEvent(NMQClient sender, NMQMessageBase item);

        /// <summary>
        /// Notify when a queue is subscribed to by this client.
        /// </summary>
        public event QueueSubscribedEvent OnQueueSubscribed;
        public delegate void QueueSubscribedEvent(NMQClient sender, string queueName);

        /// <summary>
        /// Notify when a queue subscription is removed by this client.
        /// </summary>
        public event QueueUnsubscribedEvent OnQueueUnsubscribed;
        public delegate void QueueUnsubscribedEvent(NMQClient sender, string queueName);

        /// <summary>
        /// Notify when a queue is cleared by this client.
        /// </summary>
        public event QueueClearedEvent OnQueueCleared;
        public delegate void QueueClearedEvent(NMQClient sender, string queueName);

        #endregion

        #region Connect/Disconnect.

        /// <summary>
        /// Connect to the server.
        /// </summary>
        /// <param name="hostName">Host or IP address of queue server.</param>
        /// <param name="port">Port number of queue server.</param>
        /// <param name="retryInBackground">If false, the client will not retry to connect if the connection fails.</param>
        /// <returns></returns>
        public bool Connect(string hostName, int port, bool retryInBackground)
        {
            IPAddress ipAddress = SocketUtility.GetIPv4Address(hostName);
            return Connect(ipAddress, port, retryInBackground);
        }

        /// <summary>
        /// Connect to the server.
        /// </summary>
        /// <param name="hostName">Host or IP address of queue server.</param>
        /// <param name="retryInBackground">If false, the client will not retry to connect if the connection fails.</param>
        /// <returns></returns>
        public bool Connect(string hostName, bool retryInBackground)
        {
            IPAddress ipAddress = SocketUtility.GetIPv4Address(hostName);
            return Connect(ipAddress, NMQConstants.DEFAULT_PORT, retryInBackground);
        }

        /// <summary>
        /// Connect to the server.
        /// </summary>
        /// <param name="hostName">Host or IP address of queue server.</param>
        /// <returns></returns>
        public bool Connect(string hostName)
        {
            return Connect(hostName, NMQConstants.DEFAULT_PORT, true);
        }

        /// <summary>
        /// Connect to the server.
        /// </summary>
        /// <param name="hostName">Host or IP address of queue server.</param>
        /// <param name="port">Port number of queue server.</param>
        /// <returns></returns>
        public bool Connect(string hostName, int port)
        {
            return Connect(hostName, port, true);
        }

        /// <summary>
        /// Connect to the server.
        /// </summary>
        /// <param name="ipAddress">IP address of queue server.</param>
        /// <returns></returns>
        public bool Connect(IPAddress ipAddress)
        {
            return Connect(ipAddress, NMQConstants.DEFAULT_PORT, true);
        }

        /// <summary>
        /// Connect to the server.
        /// </summary>
        /// <param name="ipAddress">IP address of queue server.</param>
        /// <param name="port">Port number of queue server.</param>
        /// <returns></returns>
        public bool Connect(IPAddress ipAddress, int port)
        {
            return Connect(ipAddress, port, true);
        }

        /// <summary>
        /// Connect to the server.
        /// </summary>
        /// <param name="hostName">Host or IP address of queue server.</param>
        /// <param name="retryInBackground">If false, the client will not retry to connect if the connection fails.</param>
        /// <returns></returns>
        public bool Connect(IPAddress ipAddress, bool retryInBackground)
        {
            return Connect(ipAddress, NMQConstants.DEFAULT_PORT, retryInBackground);
        }

        /// <summary>
        /// Connect to the server.
        /// </summary>
        /// <param name="hostName">Host or IP address of queue server.</param>
        /// <param name="port">Port number of queue server.</param>
        /// <param name="retryInBackground">If false, the client will not retry to connect if the connection fails.</param>
        /// <returns></returns>
        public bool Connect(IPAddress ipAddress, int port, bool retryInBackground)
        {
            _continueRunning = true;

            _serverIpAddress = ipAddress;
            _serverPort = port;

            try
            {
                _connectSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

                _connectSocket.Connect(new IPEndPoint(ipAddress, port));
                if (_connectSocket != null && _connectSocket.Connected)
                {
                    WaitForData(new Peer(_connectSocket));

                    OnConnected?.Invoke();

                    return true;
                }
            }
            catch
            {
                _connectSocket?.Dispose();
                _connectSocket = null;
            }

            if (retryInBackground)
            {
                new Thread(ReconnectThread).Start();
            }

            return false;
        }

        /// <summary>
        /// Disconnects from the server. Shuts down the client and cancels any outstanding connection attempts.
        /// </summary>
        public void Disconnect()
        {
            _continueRunning = false;
            CloseSocket(false);
        }

        #endregion

        #region Socket Client.

        private void CloseSocket(bool attemptReconnect)
        {
            try
            {
                if (_connectSocket != null)
                {
                    OnDisconnected?.Invoke(this, attemptReconnect);

                    try
                    {
                        _connectSocket?.Shutdown(SocketShutdown.Both);
                    }
                    finally
                    {
                        _connectSocket?.Close();
                        _connectSocket?.Dispose();
                    }
                }
            }
            catch
            {
                //Discard.
            }

            try
            {
                lock (messageQuerySubscriptions)
                {
                    for (int i = 0; i < messageQuerySubscriptions.Count; i++)
                    {
                        messageQuerySubscriptions[i].PayloadReceivedEvent.Set();
                        messageQuerySubscriptions[i].PayloadProcessedEvent.Set();
                    }
                }
            }
            catch
            {
                //Discard.
            }

            _connectSocket = null;

            if (attemptReconnect && _continueRunning)
            {
                new Thread(ReconnectThread).Start();
            }
        }

        private void ReconnectThread(object data)
        {

            if (Monitor.TryEnter(_reconnectThreadLock))
            {
                try
                {
                    while (_continueRunning)
                    {
                        try
                        {
                            if (Connect(_serverIpAddress, _serverPort, false))
                            {
                                foreach (string queueName in _subscribedQueues)
                                {
                                    Subscribe(queueName);
                                }

                                break;
                            }
                        }
                        catch
                        {
                            //Discard.
                        }

                        Thread.Sleep(1000);
                    }
                }
                catch
                {
                    throw;
                }
                finally
                {
                    Monitor.Exit(_reconnectThreadLock);
                }
            }
        }

        private void WaitForData(Peer peer)
        {
            try
            {
                if (_onDataReceivedCallback == null)
                {
                    _onDataReceivedCallback = new AsyncCallback(OnDataReceived);
                }
            }
            catch
            {
                //Discard.
            }

            peer.Socket.BeginReceive(peer.Buffer, 0, peer.Buffer.Length, SocketFlags.None, _onDataReceivedCallback, peer);
        }

        private void OnDataReceived(IAsyncResult asyn)
        {
            Socket socket = null;
            try
            {
                Peer peer = (Peer)asyn.AsyncState;
                socket = peer.Socket;
                peer.BytesReceived = peer.Socket.EndReceive(asyn);

                if (peer.BytesReceived == 0)
                {
                    CloseSocket(true);
                    return;
                }

                Packetizer.DissasemblePacketData(this, peer, PacketPayloadHandler);

                WaitForData(peer);
            }
            catch (ObjectDisposedException)
            {
                CloseSocket(true);
                return;
            }
            catch (SocketException)
            {
                CloseSocket(true);
                return;
            }
            catch (Exception ex)
            {
                LogException(ex);
            }
        }

        #endregion

        #region Commands.

        /// <summary>
        /// Asynchronously enqueues a query and returns the reply. The query is broadcast to all queue subscribers.
        /// </summary>
        /// <param name="queueName">Name of the queue for which to broadcast to.</param>
        /// <param name="label">User data to be sent, typically just a string that provides context to the message.</param>
        /// <param name="message">User data to be sent.</param>
        /// <param name="timeout">The number of milliseconds to wait for a reply. Default 60000 (1 minute).</param>
        /// <returns></returns>
        public async Task<NMQMessageBase> QueryAsync(NMQQuery query, int timeout = 60000)
        {
            if (query == null || string.IsNullOrWhiteSpace(query.QueueName))
            {
                throw new Exception("No queue name was specified.");
            }

            if (_connectSocket == null || _connectSocket.Connected == false)
            {
                throw new Exception("The client is not connected to a server.");
            }

            try
            {
                Guid messageId = Guid.NewGuid();
                var querySubscription = new NMQQuerySubscription(messageId);

                lock (messageQuerySubscriptions)
                {
                    messageQuerySubscriptions.Add(querySubscription);
                }

                var queueItem = new NMQMessageBase(ClientId, query.QueueName, messageId, query.ExpireSeconds)
                {
                    Message = query.Message,
                    Label = query.Label,
                    IsQuery = true
                };

                this.EnqueueEx(
                        new NMQCommand()
                        {
                            Message = queueItem,
                            CommandType = PayloadCommandType.Enqueue
                        }
                    );
                await Task.Run(() =>
                {
                    querySubscription.PayloadReceivedEvent.WaitOne(timeout);
                });

                lock (messageQuerySubscriptions)
                {
                    messageQuerySubscriptions.Remove(querySubscription);
                }

                querySubscription.PayloadProcessedEvent.Set();

                return querySubscription.ReplyQueueItem;
            }
            catch (Exception ex)
            {
                LogException(ex);
            }

            return null;
        }

        /// <summary>
        /// Enqueues a query and returns immediately. The reply is expected to be handeled by OnQueryReplyReceived(). The query is broadcast to all queue subscribers.
        /// </summary>
        /// <param name="queueName">Name of the queue for which to broadcast to.</param>
        /// <param name="label">User data, typically just a string that provides context to the message.</param>
        /// <param name="message">User data to be sent.</param>
        public void QueryNoWait(NMQQuery query)
        {
            Query(query, 0);
        }

        /// <summary>
        /// Synchronously enqueues a query and returns the reply. The query is broadcast to all queue subscribers.
        /// </summary>
        /// <param name="queueName">Name of the queue for which to broadcast to.</param>
        /// <param name="label">User data, typically just a string that provides context to the message.</param>
        /// <param name="message">User data to be sent.</param>
        /// <param name="timeout">The number of milliseconds to wait for a reply. Default 60000 (1 minute).</param>
        /// <returns>Returns the reply to the query or null if timeout occured.</returns>
        public NMQMessageBase Query(NMQQuery query, int timeout = 60000)
        {
            if (query == null || string.IsNullOrWhiteSpace(query.QueueName))
            {
                throw new Exception("No queue name was specified.");
            }

            if (_connectSocket == null || _connectSocket.Connected == false)
            {
                throw new Exception("The client is not connected to a server.");
            }

            try
            {
                Guid messageId = Guid.NewGuid();
                var querySubscription = new NMQQuerySubscription(messageId);

                lock (messageQuerySubscriptions)
                {
                    messageQuerySubscriptions.Add(querySubscription);
                }

                var queueItem = new NMQMessageBase(ClientId, query.QueueName, messageId, query.ExpireSeconds)
                {
                    Message = query.Message,
                    Label = query.Label,
                    IsQuery = true,
                };

                this.EnqueueEx(
                        new NMQCommand()
                        {
                            Message = queueItem,
                            CommandType = PayloadCommandType.Enqueue
                        }
                    );

                querySubscription.PayloadReceivedEvent.WaitOne(timeout);

                lock (messageQuerySubscriptions)
                {
                    messageQuerySubscriptions.Remove(querySubscription);
                }

                querySubscription.PayloadProcessedEvent.Set();

                return querySubscription.ReplyQueueItem;
            }
            catch (Exception ex)
            {
                LogException(ex);
            }

            return null;
        }

        /// <summary>
        /// Enqueues a reply to a message which was received.
        /// </summary>
        /// <param name="queryItem">The queuy message which was received.</param>
        /// <param name="label">User data, typically just a string that provides context to the message.</param>
        /// <param name="message">User data to be sent.</param>
        /// <returns>Returns the reply result which is to be returned by the OnQueryReceived event.</returns>
        public NMQQueryReplyResult Reply(NMQQuery query, NMQReply reply)
        {
            if (query == null || string.IsNullOrWhiteSpace(query.QueueName))
            {
                throw new Exception("No queue name was specified.");
            }

            if (_connectSocket == null || _connectSocket.Connected == false)
            {
                throw new Exception("The client is not connected to a server.");
            }

            try
            {
                var queueItem = new NMQMessageBase(ClientId, query.QueueName, Guid.NewGuid(), query.ExpireSeconds)
                {
                    Message = reply.Message,
                    Label = reply.Label,
                    InReplyToMessageId = query.MessageId
                };

                this.EnqueueEx(
                    new NMQCommand()
                    {
                        Message = queueItem,
                        CommandType = PayloadCommandType.Enqueue
                    }
                );
            }
            catch (Exception ex)
            {
                LogException(ex);
            }

            return new NMQQueryReplyResult();
        }

        /// <summary>
        /// Enqueues an empty reply to a message which was received.
        /// </summary>
        /// <param name="queryItem">The queuy message which was received.</param>
        /// <returns>Returns the reply result which is to be returned by the OnQueryReceived event.</returns>
        public NMQQueryReplyResult Reply(NMQQuery query)
        {
            if (query == null || string.IsNullOrWhiteSpace(query.QueueName))
            {
                throw new Exception("No queue name was specified.");
            }

            if (_connectSocket == null || _connectSocket.Connected == false)
            {
                throw new Exception("The client is not connected to a server.");
            }

            try
            {
                var queueItem = new NMQMessageBase(ClientId, query.QueueName, Guid.NewGuid(), query.ExpireSeconds)
                {
                    Message = string.Empty,
                    Label = string.Empty,
                    InReplyToMessageId = query.MessageId
                };

                this.EnqueueEx(
                    new NMQCommand()
                    {
                        Message = queueItem,
                        CommandType = PayloadCommandType.Enqueue
                    }
                );
            }
            catch (Exception ex)
            {
                LogException(ex);
            }

            return new NMQQueryReplyResult();
        }

        /// <summary>
        /// Enqueues a message which will be broadcast to all queue subscribers.
        /// </summary>
        /// <returns>True upon successful enqueue.</returns>
        public bool Enqueue(NMQMessage message)
        {
            if (message == null || string.IsNullOrWhiteSpace(message.QueueName))
            {
                throw new Exception("No queue name was specified.");
            }

            var queueItem = new NMQMessageBase(ClientId, message.QueueName, Guid.NewGuid(), message.ExpireSeconds)
            {
                Message = message.Message,
                Label = message.Label,
                MessageId = Guid.NewGuid()
            };

            return EnqueueEx(
                    new NMQCommand()
                    {
                        Message = queueItem,
                        CommandType = PayloadCommandType.Enqueue
                    }
                );
        }

        volatile int waitingToSend = 0;

        /// <summary>
        /// Private low-level enqueue method.
        /// </summary>
        /// <param name="payload">Payload to be enqueued</param>
        /// <returns>True upon successful enqueue.</returns>
        private bool EnqueueEx(NMQCommand payload)
        {
            if (_connectSocket == null || _connectSocket.Connected == false)
            {
                throw new Exception("The client is not connected to a server.");
            }

            var messagePacket = Packetizer.AssembleMessagePacket(this, payload);

            try
            {
                waitingToSend++;
                _connectSocket.Send(messagePacket);
                waitingToSend--;
                OnEnqueued?.Invoke(this, payload.Message);
            }
            catch (SocketException)
            {
                CloseSocket(true);
                return false;
            }
            catch (Exception ex)
            {
                LogException(ex);
            }

            return true;
        }

        /// <summary>
        /// Subscribes to a queue and receives any messages and queuries which are enqueued to it.
        /// </summary>
        /// <param name="queueName">The name of the queue to subscribe to.</param>
        public void Subscribe(string queueName)
        {
            var payload = new NMQCommand()
            {
                Message = new NMQMessageBase(ClientId, queueName, Guid.NewGuid(), 0),
                CommandType = PayloadCommandType.Subscribe
            };

            byte[] messagePacket = Packetizer.AssembleMessagePacket(this, payload);

            try
            {
                if (_subscribedQueues.Contains(queueName) == false)
                {
                    _subscribedQueues.Add(queueName);
                }

                if (_connectSocket != null && _connectSocket.Connected)
                {
                    _connectSocket.Send(messagePacket);
                    OnQueueSubscribed?.Invoke(this, queueName);
                }
            }
            catch (SocketException)
            {
                CloseSocket(true);
                return;
            }
            catch (Exception ex)
            {
                LogException(ex);
            }
        }

        /// <summary>
        /// Removes an existing subscription to a queue (if it exists) so that further messages and queuries will not be received.
        /// </summary>
        /// <param name="queueName">The name of the queue to unsubscribe to.</param>
        public void UnSubscribe(string queueName)
        {
            var payload = new NMQCommand()
            {
                Message = new NMQMessageBase(ClientId, queueName, Guid.NewGuid(), 0),
                CommandType = PayloadCommandType.UnSubscribe
            };

            byte[] messagePacket = Packetizer.AssembleMessagePacket(this, payload);

            try
            {
                _subscribedQueues.Remove(queueName);

                if (_connectSocket != null && _connectSocket.Connected)
                {
                    _connectSocket.Send(messagePacket);
                    OnQueueUnsubscribed?.Invoke(this, queueName);
                }
            }
            catch (SocketException)
            {
                CloseSocket(true);
                return;
            }
            catch (Exception ex)
            {
                LogException(ex);
            }
        }

        /// <summary>
        /// Removes all items from the queue.
        /// </summary>
        /// <param name="queueName">Name of the queue for which to clear.</param>
        public void Clear(string queueName)
        {
            var payload = new NMQCommand()
            {
                Message = new NMQMessageBase(ClientId, queueName, Guid.NewGuid(), 0),
                CommandType = PayloadCommandType.Clear
            };

            byte[] messagePacket = Packetizer.AssembleMessagePacket(this, payload);

            try
            {
                if (_connectSocket != null && _connectSocket.Connected)
                {
                    _connectSocket.Send(messagePacket);
                    OnQueueCleared?.Invoke(this, queueName);
                }
            }
            catch (SocketException)
            {
                CloseSocket(true);
                return;
            }
            catch (Exception ex)
            {
                LogException(ex);
            }
        }

        private void PacketPayloadHandler(Peer peer, NMQCommand payload)
        {
            try
            {
                if (payload.Message.IsQuery)
                {
                    OnQueryReceived?.Invoke(this, payload.Message.As<NMQQuery>());
                }
                else if (payload.Message.InReplyToMessageId == null)
                {
                    OnMessageReceived?.Invoke(this, payload.Message.As<NMQMessage>());
                }
                else
                {
                    NMQQuerySubscription messageQuerySubscription = null;

                    lock (messageQuerySubscriptions)
                    {
                        messageQuerySubscription = (from o in messageQuerySubscriptions
                                                    where o.OriginalMessageId == payload.Message.InReplyToMessageId
                                                    select o).FirstOrDefault();

                        OnQueryReplyReceived?.Invoke(this, payload.Message.As<NMQReply>(), messageQuerySubscription != null);

                        if (messageQuerySubscription != null)
                        {
                            messageQuerySubscription.SetReply(payload.Message);
                        }
                        else
                        {
                            //If there is no open query for this reply, then discard it.
                        }
                    }

                    if (messageQuerySubscription != null)
                    {
                        messageQuerySubscription.PayloadProcessedEvent.WaitOne();
                    }
                }

            }
            catch (SocketException)
            {
                CloseSocket(true);
                return;
            }
            catch (Exception ex)
            {
                LogException(ex);
            }
        }

        #endregion
    }
}
