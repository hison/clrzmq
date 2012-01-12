﻿namespace ZeroMQ
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using System.Threading;

    using ZeroMQ.Interop;

    /// <summary>
    /// Multiplexes input/output events in a level-triggered fashion over a set of sockets.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Sockets will be polled according to their capabilities. For example, sockets that are
    /// receive-only (e.g., PULL and SUB sockets) will only poll for Input events. Sockets that
    /// can both send and receive (e.g., REP, REQ, etc.) will poll for both Input and Output events.
    /// </para>
    /// <para>
    /// To actually send or receive data, the socket's <see cref="ZmqSocket.ReceiveReady"/> and/or
    /// <see cref="ZmqSocket.SendReady"/> event handlers must be attached to. If attached, these will
    /// be invoked when data is ready to be received or sent.
    /// </para>
    /// </remarks>
    public class Poller
    {
        private readonly Dictionary<PollItem, ZmqSocket> _pollableSockets;
        private readonly PollerProxy _pollerProxy;

        private PollItem[] _pollItems;

        /// <summary>
        /// Initializes a new instance of the <see cref="Poller"/> class.
        /// </summary>
        public Poller()
            : this(new PollerProxy())
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Poller"/> class with a collection of sockets to poll over.
        /// </summary>
        /// <param name="socketsToPoll">The collection of <see cref="ZmqSocket"/>s to poll.</param>
        public Poller(IEnumerable<ZmqSocket> socketsToPoll)
            : this()
        {
            AddSockets(socketsToPoll);
        }

        internal Poller(PollerProxy pollerProxy)
        {
            if (pollerProxy == null)
            {
                throw new ArgumentNullException("pollerProxy");
            }

            _pollerProxy = pollerProxy;
            _pollableSockets = new Dictionary<PollItem, ZmqSocket>();
        }

        /// <summary>
        /// Add a socket that will be polled for input/output events, depending on its capabilities.
        /// </summary>
        /// <param name="socket">The <see cref="ZmqSocket"/> to poll.</param>
        public void AddSocket(ZmqSocket socket)
        {
            if (socket == null)
            {
                throw new ArgumentNullException("socket");
            }

            _pollableSockets.Add(new PollItem(socket.SocketHandle, IntPtr.Zero, socket.GetPollEvents()), socket);
        }

        /// <summary>
        /// Add a collection of sockets that will be polled for input/output events, depending on their capabilities.
        /// </summary>
        /// <param name="sockets">The collection of <see cref="ZmqSocket"/>s to poll.</param>
        public void AddSockets(IEnumerable<ZmqSocket> sockets)
        {
            if (sockets == null)
            {
                throw new ArgumentNullException("sockets");
            }

            foreach (var socket in sockets)
            {
                AddSocket(socket);
            }
        }

        /// <summary>
        /// Multiplex input/output events over the contained set of sockets in blocking mode, firing
        /// <see cref="ZmqSocket.ReceiveReady" /> or <see cref="ZmqSocket.SendReady" /> as appropriate.
        /// </summary>
        /// <exception cref="ZmqSocketException">An error occurred polling for socket events.</exception>
        public void Poll()
        {
            PollBlocking();
        }

        /// <summary>
        /// Multiplex input/output events over the contained set of sockets in non-blocking mode, firing
        /// <see cref="ZmqSocket.ReceiveReady" /> or <see cref="ZmqSocket.SendReady" /> as appropriate.
        /// Returns when one or more events are ready to fire or when the specified timeout elapses, whichever
        /// comes first.
        /// </summary>
        /// <param name="timeout">A <see cref="TimeSpan"/> indicating the timeout value.</param>
        /// <exception cref="ZmqSocketException">An error occurred polling for socket events.</exception>
        public void Poll(TimeSpan timeout)
        {
            if (timeout.TotalMilliseconds == Timeout.Infinite)
            {
                PollBlocking();
            }
            else
            {
                PollNonBlocking(timeout);
            }
        }

        private static void ContinueIfInterrupted()
        {
            // An error value of EINTR indicates that the operation was interrupted
            // by delivery of a signal before any events were available. This is a recoverable
            // error, so try polling again for the remaining amount of time in the timeout.
            if (!ErrorProxy.ThreadWasInterrupted)
            {
                throw new ZmqSocketException(ErrorProxy.GetLastError());
            }
        }

        private void PollBlocking()
        {
            CreatePollItems();

            while (Poll(Timeout.Infinite) == -1 && !ErrorProxy.ContextWasTerminated)
            {
                ContinueIfInterrupted();
            }
        }

        private void PollNonBlocking(TimeSpan timeout)
        {
            CreatePollItems();

            var remainingTimeout = (int)timeout.TotalMilliseconds;
            var elapsed = Stopwatch.StartNew();

            do
            {
                int result = Poll(remainingTimeout);

                if (result >= 0 || ErrorProxy.ContextWasTerminated)
                {
                    break;
                }

                ContinueIfInterrupted();
                remainingTimeout -= (int)elapsed.ElapsedMilliseconds;
            }
            while (remainingTimeout >= 0);
        }

        private void CreatePollItems()
        {
            if (_pollItems == null || _pollItems.Length != _pollableSockets.Count)
            {
                _pollItems = _pollableSockets.Keys.ToArray();
            }
        }

        private int Poll(int timeoutMilliseconds)
        {
            if (_pollableSockets.Count == 0)
            {
                throw new InvalidOperationException("At least one socket is required for polling.");
            }

            int readyCount = _pollerProxy.Poll(_pollItems, timeoutMilliseconds);

            if (readyCount > 0)
            {
                foreach (PollItem pollItem in _pollItems.Where(item => item.ReadyEvents != (short)PollEvents.None))
                {
                    ZmqSocket socket = _pollableSockets[pollItem];

                    socket.InvokePollEvents((PollEvents)pollItem.ReadyEvents);
                }
            }

            return readyCount;
        }
    }
}
