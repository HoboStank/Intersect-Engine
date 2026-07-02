using System;
using System.IO;
using System.Security.Cryptography;

if (args.Length < 2)
{
    Console.WriteLine("Usage: DecryptTest.exe <path-to-private-bkey> <cipher-hex>");
    return 1;
}

var keyPath = args[0];
var cipherHex = args[1];
if (!File.Exists(keyPath))
{
    Console.Error.WriteLine($"Key file not found: {keyPath}");
    return 2;
}

var cipher = Convert.FromHexString(cipherHex);

var data = File.ReadAllBytes(keyPath);
try
{
    // Try Microsoft PRIVATEKEYBLOB parsing like the key-inspector
    if (data.Length > 4 && data[0] == 0x07 && data[1] == 0x02 && data[2] == 0x00 && data[3] == 0x00)
    {
        int offset = 0;
        byte bType = data[offset++];
        byte bVersion = data[offset++];
        ushort reserved = BitConverter.ToUInt16(data, offset); offset += 2;
        uint aiKeyAlg = BitConverter.ToUInt32(data, offset); offset += 4;

        uint magic = BitConverter.ToUInt32(data, offset); offset += 4;
        uint bitlen = BitConverter.ToUInt32(data, offset); offset += 4;
        uint pubexp = BitConverter.ToUInt32(data, offset); offset += 4;

        int modulusBytes = (int)(bitlen / 8);
        var modulusLe = new byte[modulusBytes];
        Array.Copy(data, offset, modulusLe, 0, modulusBytes);
        offset += modulusBytes;

        var privExpLe = new byte[modulusBytes];
        Array.Copy(data, offset, privExpLe, 0, modulusBytes);
        offset += modulusBytes;

        int half = modulusBytes / 2;
        var pLe = new byte[half]; Array.Copy(data, offset, pLe, 0, half); offset += half;
        var qLe = new byte[half]; Array.Copy(data, offset, qLe, 0, half); offset += half;
        var dpLe = new byte[half]; Array.Copy(data, offset, dpLe, 0, half); offset += half;
        var dqLe = new byte[half]; Array.Copy(data, offset, dqLe, 0, half); offset += half;
        var inverseQLe = new byte[half]; Array.Copy(data, offset, inverseQLe, 0, half); offset += half;

        Array.Reverse(modulusLe); // big-endian
        Array.Reverse(privExpLe);
        Array.Reverse(pLe);
        Array.Reverse(qLe);
        Array.Reverse(dpLe);
        Array.Reverse(dqLe);
        Array.Reverse(inverseQLe);

        var rsaParams = new RSAParameters
        {
            Modulus = modulusLe,
            Exponent = BitConverter.GetBytes(pubexp).Reverse().ToArray(),
            D = privExpLe,
            P = pLe,
            Q = qLe,
            DP = dpLe,
            DQ = dqLe,
            InverseQ = inverseQLe,
        };

        using var rsa = RSA.Create();
        rsa.ImportParameters(rsaParams);
        Console.WriteLine($"Imported private key. KeySize={rsa.KeySize}");

        foreach (var pad in new[] { RSAEncryptionPadding.OaepSHA256, RSAEncryptionPadding.OaepSHA1 })
        {
            try
            {
                var plain = rsa.Decrypt(cipher, pad);
                Console.WriteLine($"Decrypted with {pad}: {BitConverter.ToString(plain).Replace("-","")}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to decrypt with {pad}: {ex.GetType().Name}: {ex.Message}");
            }
        }

        return 0;
    }
    else
    {
        using var rsa = RSA.Create();
        rsa.ImportRSAPrivateKey(data, out _);
        Console.WriteLine($"Imported private key. KeySize={rsa.KeySize}");
        foreach (var pad in new[] { RSAEncryptionPadding.OaepSHA256, RSAEncryptionPadding.OaepSHA1 })
        {
            try
            {
                var plain = rsa.Decrypt(cipher, pad);
                Console.WriteLine($"Decrypted with {pad}: {BitConverter.ToString(plain).Replace("-","")}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to decrypt with {pad}: {ex.GetType().Name}: {ex.Message}");
            }
        }
        return 0;
    }
}
catch (Exception ex)
{
    Console.Error.WriteLine("Exception parsing key: " + ex);
    return 3;
}
