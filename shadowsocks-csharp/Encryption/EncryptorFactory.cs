
using System;
using System.Collections.Generic;
using System.Reflection;
namespace Shadowsocks.Encryption
{
    public static class EncryptorFactory
    {
        private static readonly Dictionary<string, Type> _registeredEncryptors;
        private static readonly List<string> _registeredEncryptorNames;

        private static readonly Type[] _constructorTypes = new[] { typeof(string), typeof(string), typeof(bool) };

        static EncryptorFactory()
        {
            _registeredEncryptors = new Dictionary<string, Type>();
            _registeredEncryptorNames = new List<string>();
            foreach (var method in NoneEncryptor.SupportedCiphers())
            {
                if (!_registeredEncryptorNames.Contains(method))
                {
                    _registeredEncryptorNames.Add(method);
                    _registeredEncryptors.Add(method, typeof(NoneEncryptor));
                }
            }

            {
                foreach (var method in MbedTLSEncryptor.SupportedCiphers())
                {
                    if (!_registeredEncryptorNames.Contains(method))
                    {
                        _registeredEncryptorNames.Add(method);
                        _registeredEncryptors.Add(method, typeof(MbedTLSEncryptor));
                    }
                }
            }
            if (LibcryptoEncryptor.isSupport())
            {
                LibcryptoEncryptor.InitAviable();
                foreach (var method in LibcryptoEncryptor.SupportedCiphers())
                {
                    if (!_registeredEncryptorNames.Contains(method))
                    {
                        _registeredEncryptorNames.Add(method);
                        _registeredEncryptors.Add(method, typeof(LibcryptoEncryptor));
                    }
                }
            }
            foreach (var method in SodiumEncryptor.SupportedCiphers())
            {
                if (!_registeredEncryptorNames.Contains(method))
                {
                    _registeredEncryptorNames.Add(method);
                    _registeredEncryptors.Add(method, typeof(SodiumEncryptor));
                }
            }
        }

        public static List<string> GetEncryptor() => _registeredEncryptorNames;

        public static IEncryptor GetEncryptor(string method, string password, bool cache)
        {
            if (string.IsNullOrEmpty(method))
            {
                method = "aes-256-cfb";
            }
            method = method.ToLowerInvariant();
            var t = _registeredEncryptors[method];
            var c = t.GetConstructor(_constructorTypes);
            var result = (IEncryptor)c.Invoke(new object[] { method, password, cache });
            return result;
        }

        public static EncryptorInfo GetEncryptorInfo(string method)
        {
            if (string.IsNullOrEmpty(method))
            {
                method = "aes-256-cfb";
            }
            method = method.ToLowerInvariant();
            var t = _registeredEncryptors[method];
            var c = t.GetConstructor(_constructorTypes);
            var result = (IEncryptor)c.Invoke(new object[] { method, "0", false });
            var info = result.getInfo();
            result.Dispose();
            return info;
        }
    }
}
