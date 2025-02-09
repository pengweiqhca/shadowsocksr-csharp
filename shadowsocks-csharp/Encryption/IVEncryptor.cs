﻿using Shadowsocks.Model;
using System.Security.Cryptography;
using System.Text;

namespace Shadowsocks.Encryption;

public abstract class IVEncryptor
    : EncryptorBase
{
    protected Dictionary<string, EncryptorInfo> ciphers;

    private static readonly LRUCache<string, byte[]> CachedKeys = new(600);
    protected byte[] _encryptIV;
    protected byte[] _decryptIV;
    protected int _decryptIVReceived;
    protected bool _encryptIVSent;
    protected int _encryptIVOffset;
    protected int _decryptIVOffset;
    protected string _method;
    protected int _cipher;
    protected EncryptorInfo _cipherInfo;
    protected byte[] _key;
    protected int keyLen;
    protected byte[] _iv;
    protected int ivLen;

    protected byte[] encbuf = new byte[MAX_INPUT_SIZE];
    protected byte[] decbuf = new byte[MAX_INPUT_SIZE];

    public IVEncryptor(string method, string password, bool cache)
        : base(method, password)
    {
        InitKey(method, password);
    }

    protected abstract Dictionary<string, EncryptorInfo> getCiphers();

    public override bool SetIV(byte[] iv)
    {
        if (iv != null && iv.Length == ivLen)
        {
            iv.CopyTo(_iv, 0);
            _encryptIVSent = true;
            initCipher(iv, true);
            return true;
        }
        return false;
    }
    public override byte[] getIV() => _iv;
    public override byte[] getKey()
    {
        var key = (byte[])_key.Clone();
        Array.Resize(ref key, keyLen);
        return key;
    }
    public override EncryptorInfo getInfo() => _cipherInfo;

    protected void InitKey(string method, string password)
    {
        method = method.ToLower();
        _method = method;
        var k = $"{method}:{password}";
        ciphers = getCiphers();
        _cipherInfo = ciphers[_method];
        _cipher = _cipherInfo.type;
        if (_cipher == 0)
        {
            throw new Exception("method not found");
        }
        keyLen = ciphers[_method].key_size;
        ivLen = ciphers[_method].iv_size;
        if (!CachedKeys.ContainsKey(k))
        {
            lock (CachedKeys)
            {
                if (!CachedKeys.ContainsKey(k))
                {
                    var passbuf = Encoding.UTF8.GetBytes(password);
                    _key = new byte[32];
                    var iv = new byte[16];
                    bytesToKey(passbuf, _key);
                    CachedKeys.Set(k, _key);
                    CachedKeys.Sweep();
                }
            }
        }
        _key ??= CachedKeys.Get(k);
        Array.Resize(ref _iv, ivLen);
        randBytes(_iv, ivLen);
    }

    protected void bytesToKey(byte[] password, byte[] key)
    {
        var result = new byte[password.Length + 16];
        var i = 0;
        byte[] md5sum = null;
        while (i < key.Length)
        {
            if (i == 0)
            {
                md5sum = MbedTLS.MD5(password);
            }
            else
            {
                md5sum.CopyTo(result, 0);
                password.CopyTo(result, md5sum.Length);
                md5sum = MbedTLS.MD5(result);
            }
            md5sum.CopyTo(key, i);
            i += md5sum.Length;
        }
    }

    protected static void randBytes(byte[] buf, int length)
    {
        var temp = new byte[length];
        RandomNumberGenerator.Create().GetBytes(temp);
        temp.CopyTo(buf, 0);
    }

    protected virtual void initCipher(byte[] iv, bool isCipher)
    {
        if (ivLen > 0)
        {
            if (isCipher)
            {
                _encryptIV = new byte[ivLen];
                Array.Copy(iv, _encryptIV, ivLen);
            }
            else
            {
                _decryptIV = new byte[ivLen];
                Array.Copy(iv, _decryptIV, ivLen);
            }
        }
    }

    protected abstract void cipherUpdate(bool isCipher, int length, byte[] buf, byte[] outbuf);

    public override void Encrypt(byte[] buf, int length, byte[] outbuf, out int outlength)
    {
        if (!_encryptIVSent)
        {
            _encryptIVSent = true;
            Buffer.BlockCopy(_iv, 0, outbuf, 0, ivLen);
            initCipher(outbuf, true);

            outlength = length + ivLen;
            cipherUpdate(true, length, buf, encbuf);
            Buffer.BlockCopy(encbuf, 0, outbuf, ivLen, length);
        }
        else
        {
            outlength = length;
            cipherUpdate(true, length, buf, outbuf);
        }
    }

    public override void Decrypt(byte[] buf, int length, byte[] outbuf, out int outlength)
    {
        if (_decryptIVReceived <= ivLen)
        {
            var start_pos = ivLen;
            if (_decryptIVReceived + length < ivLen)
            {
                _decryptIV ??= new byte[ivLen];
                Buffer.BlockCopy(buf, 0, _decryptIV, _decryptIVReceived, length);
                outlength = 0;
                _decryptIVReceived += length;
            }
            else if (_decryptIVReceived == 0)
            {
                initCipher(buf, false);
                outlength = length - ivLen;
                _decryptIVReceived = ivLen;
            }
            else
            {
                start_pos = ivLen - _decryptIVReceived;
                var temp_buf = new byte[ivLen];
                Buffer.BlockCopy(_decryptIV, 0, temp_buf, 0, _decryptIVReceived);
                Buffer.BlockCopy(buf, 0, temp_buf, _decryptIVReceived, start_pos);
                initCipher(temp_buf, false);
                outlength = length - start_pos;
                _decryptIVReceived = ivLen;
            }

            if (outlength > 0)
            {
                _decryptIVReceived += outlength;

                Buffer.BlockCopy(buf, start_pos, decbuf, 0, outlength);
                cipherUpdate(false, outlength, decbuf, outbuf);
            }
        }
        else
        {
            outlength = length;
            cipherUpdate(false, length, buf, outbuf);
        }
    }

    public override void ResetEncrypt()
    {
        _encryptIVSent = false;
        _encryptIVOffset = 0; // SSL
        randBytes(_iv, ivLen);
    }

    public override void ResetDecrypt()
    {
        _decryptIVReceived = 0;
        _decryptIVOffset = 0; // SSL
    }

}
public class NoneEncryptor
    : IVEncryptor
{
    public NoneEncryptor(string method, string password, bool cache)
        : base(method, password, cache)
    {
        InitKey(method, password);
    }

    private static readonly Dictionary<string, EncryptorInfo> _ciphers = new()
    {
        {
            "none", new EncryptorInfo(16, 0, true, 1)
        },
    };

    public static List<string> SupportedCiphers() => new(_ciphers.Keys);

    protected override Dictionary<string, EncryptorInfo> getCiphers() => _ciphers;

    protected override void cipherUpdate(bool isCipher, int length, byte[] buf, byte[] outbuf)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(ToString());
        }
        Array.Copy(buf, outbuf, length);
    }


        #region IDisposable
    private bool _disposed;

    public override void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    ~NoneEncryptor()
    {
        Dispose(false);
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
        }
    }
        #endregion
}