using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net;
using System.Net.Sockets;

namespace wk.svr
{
    public struct st_clientSet
    {
        public const int sz_buf = 2048;
        public const int sz_client = 100;

        public byte[] m_buf;
        public TcpClient m_tcp;
        public NetworkStream m_ns;
        public static st_clientSet makeOne()
        {
            st_clientSet s;
            s.m_tcp = null;
            s.m_ns = null;
            s.m_buf = new byte[sz_buf];
            return s;
        }
    }
}
