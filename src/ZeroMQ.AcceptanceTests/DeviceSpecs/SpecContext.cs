﻿namespace ZeroMQ.AcceptanceTests.DeviceSpecs
{
    using System;
    using System.Threading;

    using Machine.Specifications;

    using ZeroMQ.Devices;

    abstract class using_threaded_device<TDevice> where TDevice : Device
    {
        protected const string FrontendAddr = "inproc://dev_frontend";
        protected const string BackendAddr = "inproc://dev_backend";

        protected static Func<TDevice> createDevice;
        protected static Func<ZmqSocket> createSender;
        protected static Func<ZmqSocket> createReceiver;

        protected static ZmqSocket sender;
        protected static ZmqSocket receiver;
        protected static TDevice device;
        protected static ZmqContext zmqContext;

        protected static Action<TDevice> deviceInit;
        protected static Action<ZmqSocket> senderInit;
        protected static Action<ZmqSocket> senderAction;
        protected static Action<ZmqSocket> receiverInit;
        protected static Action<ZmqSocket> receiverAction;

        private static Thread deviceThread;
        private static Thread receiverThread;
        private static Thread senderThread;

        private static ManualResetEvent deviceReady;
        private static ManualResetEvent receiverSignal;

        Establish context = () =>
        {
            zmqContext = ZmqContext.Create();
            device = createDevice();
            sender = createSender();
            receiver = createReceiver();

            deviceInit = dev => { };
            senderInit = sck => { };
            receiverInit = sck => { };
            senderAction = sck => { };
            receiverAction = sck => { };

            deviceReady = new ManualResetEvent(false);
            receiverSignal = new ManualResetEvent(false);

            deviceThread = new Thread(() =>
            {
                deviceInit(device);
                device.Initialize();

                device.Start();
                device.PollEvent.WaitOne();
                deviceReady.Set();
            });

            receiverThread = new Thread(() =>
            {
                deviceReady.WaitOne();

                receiverInit(receiver);
                receiver.ReceiveHighWatermark = 1;
                receiver.Connect(BackendAddr);

                receiverSignal.Set();

                receiverAction(receiver);
            });

            senderThread = new Thread(() =>
            {
                receiverSignal.WaitOne();

                senderInit(sender);
                sender.SendHighWatermark = 1;
                sender.Connect(FrontendAddr);

                senderAction(sender);
            });
        };

        Cleanup resources = () =>
        {
            sender.Dispose();
            receiver.Dispose();
            device.Dispose();
            zmqContext.Dispose();
        };

        protected static void StartThreads()
        {
            deviceThread.Start();
            receiverThread.Start();
            senderThread.Start();

            if (!receiverThread.Join(5000))
            {
                receiverThread.Abort();
            }

            if (!senderThread.Join(5000))
            {
                senderThread.Abort();
            }

            device.Stop();

            if (!deviceThread.Join(5000))
            {
                deviceThread.Abort();
            }
        }
    }
}
