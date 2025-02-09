﻿using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shadowsocks.Controller;
using Shadowsocks.Encryption;
using System;
using System.Collections.Generic;
using System.Threading;

namespace test;

[TestClass]
public class UnitTest
{
    [TestMethod]
    public void TestCompareVersion()
    {
        Assert.IsTrue(UpdateChecker.CompareVersion("2.3.1.0", "2.3.1") == 0);
        Assert.IsTrue(UpdateChecker.CompareVersion("1.2", "1.3") < 0);
        Assert.IsTrue(UpdateChecker.CompareVersion("1.3", "1.2") > 0);
        Assert.IsTrue(UpdateChecker.CompareVersion("1.3", "1.3") == 0);
        Assert.IsTrue(UpdateChecker.CompareVersion("1.2.1", "1.2") > 0);
        Assert.IsTrue(UpdateChecker.CompareVersion("2.3.1", "2.4") < 0);
        Assert.IsTrue(UpdateChecker.CompareVersion("1.3.2", "1.3.1") > 0);
    }

    private void RunEncryptionRound(IEncryptor encryptor, IEncryptor decryptor)
    {
        var plain = new byte[16384];
        var cipher = new byte[plain.Length + 16];
        var plain2 = new byte[plain.Length + 16];
        var random = new Random();
        random.NextBytes(plain);
        encryptor.Encrypt(plain, plain.Length, cipher, out var outLen);
        decryptor.Decrypt(cipher, outLen, plain2, out var outLen2);
        Assert.AreEqual(plain.Length, outLen2);
        for (var j = 0; j < plain.Length; j++)
        {
            Assert.AreEqual(plain[j], plain2[j]);
        }
        encryptor.Encrypt(plain, 1000, cipher, out outLen);
        decryptor.Decrypt(cipher, outLen, plain2, out outLen2);
        Assert.AreEqual(1000, outLen2);
        for (var j = 0; j < outLen2; j++)
        {
            Assert.AreEqual(plain[j], plain2[j]);
        }
        encryptor.Encrypt(plain, 12333, cipher, out outLen);
        decryptor.Decrypt(cipher, outLen, plain2, out outLen2);
        Assert.AreEqual(12333, outLen2);
        for (var j = 0; j < outLen2; j++)
        {
            Assert.AreEqual(plain[j], plain2[j]);
        }
    }

    private static bool encryptionFailed;
    private static readonly object locker = new();

    [TestMethod]
    public void TestPolarSSLEncryption()
    {
        // run it once before the multi-threading test to initialize global tables
        RunSinglePolarSSLEncryptionThread();
        var threads = new List<Thread>();
        for (var i = 0; i < 10; i++)
        {
            var t = new Thread(RunSinglePolarSSLEncryptionThread);
            threads.Add(t);
            t.Start();
        }
        foreach (var t in threads)
        {
            t.Join();
        }
        Assert.IsFalse(encryptionFailed);
    }

    private void RunSinglePolarSSLEncryptionThread()
    {
        try
        {
            for (var i = 0; i < 100; i++)
            {
                IEncryptor encryptor = new MbedTLSEncryptor("aes-256-cfb", "barfoo!", false);
                IEncryptor decryptor = new MbedTLSEncryptor("aes-256-cfb", "barfoo!", false);
                RunEncryptionRound(encryptor, decryptor);
            }
        }
        catch
        {
            encryptionFailed = true;
            throw;
        }
    }

    [TestMethod]
    public void TestRC4Encryption()
    {
        // run it once before the multi-threading test to initialize global tables
        RunSingleRC4EncryptionThread();
        var threads = new List<Thread>();
        for (var i = 0; i < 10; i++)
        {
            var t = new Thread(RunSingleRC4EncryptionThread);
            threads.Add(t);
            t.Start();
        }
        foreach (var t in threads)
        {
            t.Join();
        }
        Assert.IsFalse(encryptionFailed);
    }

    private void RunSingleRC4EncryptionThread()
    {
        try
        {
            for (var i = 0; i < 100; i++)
            {
                var random = new Random();
                IEncryptor encryptor = new MbedTLSEncryptor("rc4-md5", "barfoo!", false);
                IEncryptor decryptor = new MbedTLSEncryptor("rc4-md5", "barfoo!", false);
                RunEncryptionRound(encryptor, decryptor);
            }
        }
        catch
        {
            encryptionFailed = true;
            throw;
        }
    }

    [TestMethod]
    public void TestSodiumEncryption()
    {
        // run it once before the multi-threading test to initialize global tables
        RunSingleSodiumEncryptionThread();
        var threads = new List<Thread>();
        for (var i = 0; i < 10; i++)
        {
            var t = new Thread(RunSingleSodiumEncryptionThread);
            threads.Add(t);
            t.Start();
        }
        foreach (var t in threads)
        {
            t.Join();
        }
        Assert.IsFalse(encryptionFailed);
    }

    private void RunSingleSodiumEncryptionThread()
    {
        try
        {
            for (var i = 0; i < 100; i++)
            {
                var random = new Random();
                IEncryptor encryptor = new SodiumEncryptor("salsa20", "barfoo!", false);
                IEncryptor decryptor = new SodiumEncryptor("salsa20", "barfoo!", false);
                RunEncryptionRound(encryptor, decryptor);
            }
        }
        catch
        {
            encryptionFailed = true;
            throw;
        }
    }
}