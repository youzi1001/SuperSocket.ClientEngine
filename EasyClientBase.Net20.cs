﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using SuperSocket.ProtoBase;
using System.Net;

namespace SuperSocket.ClientEngine
{
    public abstract class EasyClientBase : IBufferState
    {
        private IClientSession m_Session;
        private AutoResetEvent m_ConnectEvent = new AutoResetEvent(false);
        private bool m_Connected = false;

        protected IPipelineProcessor PipeLineProcessor { get; set; }

        public int ReceiveBufferSize { get; set; }

        public EndPoint LocalEndPoint { get; set; }

        public bool NoDelay { get; set; }

        public SecurityOption Security { get; set; }

        public EasyClientBase()
        {

        }

        public bool IsConnected { get { return m_Connected; } }

        private TcpClientSession GetUnderlyingSession()
        {
            var security = Security;

            // no SSL/TLS enabled
            if (security == null || security.EnabledSslProtocols == System.Security.Authentication.SslProtocols.None)
            {
                return new AsyncTcpSession();
            }

            return new SslStreamTcpSession()
            {
                Security = security
            };
        }

        public void BeginConnect(EndPoint remoteEndPoint)
        {
            if (PipeLineProcessor == null)
                throw new Exception("This client has not been initialized.");

            var session = GetUnderlyingSession();

            var localEndPoint = LocalEndPoint;

            if (localEndPoint != null)
            {
                session.LocalEndPoint = localEndPoint;
            }

            session.NoDelay = NoDelay;

            session.Connected += new EventHandler(OnSessionConnected);
            session.Error += new EventHandler<ErrorEventArgs>(OnSessionError);
            session.Closed += new EventHandler(OnSessionClosed);
            session.DataReceived += new EventHandler<DataEventArgs>(OnSessionDataReceived);

            if (ReceiveBufferSize > 0)
                session.ReceiveBufferSize = ReceiveBufferSize;

            m_Session = session;
            session.Connect(remoteEndPoint);
        }
        
        public void Send(byte[] data)
        {
            Send(new ArraySegment<byte>(data, 0, data.Length));
        }

        public void Send(ArraySegment<byte> segment)
        {
            var session = m_Session;
            
            if (!m_Connected || session == null)
                throw new Exception("The socket is not connected.");

            session.Send(segment);
        }

        public void Send(List<ArraySegment<byte>> segments)
        {
            var session = m_Session;
            
            if (!m_Connected || session == null)
                throw new Exception("The socket is not connected.");

            session.Send(segments);
        }

        public void Close()
        {
            var session = m_Session;
            
            if (session != null && m_Connected)
            {
                session.Close();
            }
        }

        void OnSessionDataReceived(object sender, DataEventArgs e)
        {
            var result = PipeLineProcessor.Process(new ArraySegment<byte>(e.Data, e.Offset, e.Length), this as IBufferState);

            // allocate new receive buffer if the previous one was cached
            if (result.State == ProcessState.Cached)
            {
                var session = m_Session;

                if (session != null)
                {
                    var bufferSetter = session as IBufferSetter;

                    if (bufferSetter != null)
                    {
                        bufferSetter.SetBuffer(new ArraySegment<byte>(new byte[session.ReceiveBufferSize]));
                    }
                }
            }
        }

        void OnSessionError(object sender, ErrorEventArgs e)
        {
            if (!m_Connected)
            {
                m_ConnectEvent.Set();
            }

            OnError(e);
        }

        private void OnError(Exception e)
        {
            OnError(new ErrorEventArgs(e));
        }

        private void OnError(ErrorEventArgs args)
        {
            var handler = Error;

            if (handler != null)
                handler(this, args);
        }

        public event EventHandler<ErrorEventArgs> Error;

        void OnSessionClosed(object sender, EventArgs e)
        {
            m_Connected = false;

            var handler = Closed;

            if (handler != null)
                handler(this, EventArgs.Empty);

            m_ConnectEvent.Set();
        }

        public event EventHandler Closed;

        void OnSessionConnected(object sender, EventArgs e)
        {
            m_Connected = true;
            m_ConnectEvent.Set();

            var handler = Connected;
            if (handler != null)
            {
                handler(this, EventArgs.Empty);
            }
        }

        public event EventHandler Connected;

        int IBufferState.DecreaseReference()
        {
            return 0;
        }

        void IBufferState.IncreaseReference()
        {

        }
    }
}
