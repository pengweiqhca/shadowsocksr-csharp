namespace Shadowsocks.Obfs
{
    public abstract class ObfsBase : IObfs
    {
        protected ObfsBase(string method) => Method = method;

        protected string Method;
        protected ServerInfo Server;
        protected long SentLength;

        public abstract Dictionary<string, int[]> GetObfs();

        public string Name() => Method;

        public virtual bool isKeepAlive() => false;

        public virtual bool isAlwaysSendback() => false;


        public virtual byte[] ClientPreEncrypt(byte[] plaindata, int datalength, out int outlength)
        {
            outlength = datalength;
            return plaindata;
        }
        public abstract byte[] ClientEncode(byte[] encryptdata, int datalength, out int outlength);
        public abstract byte[] ClientDecode(byte[] encryptdata, int datalength, out int outlength, out bool needsendback);
        public virtual byte[] ClientPostDecrypt(byte[] plaindata, int datalength, out int outlength)
        {
            outlength = datalength;
            return plaindata;
        }
        public virtual byte[] ClientUdpPreEncrypt(byte[] plaindata, int datalength, out int outlength)
        {
            outlength = datalength;
            return plaindata;
        }
        public virtual byte[] ClientUdpPostDecrypt(byte[] plaindata, int datalength, out int outlength)
        {
            outlength = datalength;
            return plaindata;
        }

        public virtual object InitData() => null;
        public virtual void SetServerInfo(ServerInfo serverInfo)
        {
            Server = serverInfo;
        }
        public virtual void SetServerInfoIV(byte[] iv)
        {
            Server.SetIV(iv);
        }
        public static int GetHeadSize(byte[] plaindata, int defaultValue)
        {
            if (plaindata is not { Length: >= 2 })
                return defaultValue;
            var head_type = plaindata[0] & 0x7;
            return head_type switch
            {
                1 => 7,
                4 => 19,
                3 => 4 + plaindata[1],
                2 => 4 + plaindata[1],
                _ => defaultValue
            };
        }
        public long GetSentLength() => SentLength;
        public virtual int GetOverhead() => 0;

        public int GetTcpMSS() => Server.tcp_mss;


        #region IDisposable
        protected bool _disposed;

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            lock (this)
            {
                if (_disposed)
                {
                    return;
                }
                _disposed = true;
                Disposing();
            }
        }

        protected virtual void Disposing()
        {

        }
        #endregion

    }
}
