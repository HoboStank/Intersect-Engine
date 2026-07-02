using System;
using System.IO;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;

// Simple key inspector for Intersect network.handshake.bkey (Microsoft PUBLICKEYBLOB or DER/PEM)
// Usage: KeyInspector.exe [path-to-private-bkey]

string? path = args.Length > 0 ? args[0] : Path.Combine(Environment.CurrentDirectory, "network.handshake.bkey");

if (!File.Exists(path))
{
    Console.Error.WriteLine($"File not found: {path}");
    return 1;
}

using var fs = File.OpenRead(path);

// Read all bytes
var data = new byte[fs.Length];
int read = fs.Read(data, 0, data.Length);
Console.WriteLine($"Read {read} bytes from {path}");

// Try to detect Microsoft PUBLICKEYBLOB (BLOBHEADER + RSAPUBKEY) or PRIVATEKEYBLOB
// PUBLICKEYBLOB typically starts with 0x06 0x02 0x00 0x00, PRIVATEKEYBLOB with 0x07 0x02 0x00 0x00
if (data.Length > 4 && (data[0] == 0x06 || data[0] == 0x07) && data[1] == 0x02 && data[2] == 0x00 && data[3] == 0x00)
{
    Console.WriteLine("Detected Microsoft BLOB-like header");
    try
    {
        // Parse BLOBHEADER
        int offset = 0;
        byte bType = data[offset++];
        byte bVersion = data[offset++];
        ushort reserved = BitConverter.ToUInt16(data, offset); offset += 2;
        uint aiKeyAlg = BitConverter.ToUInt32(data, offset); offset += 4;
        Console.WriteLine($"BLOBHEADER: bType={bType}, bVersion={bVersion}, reserved=0x{reserved:X}, aiKeyAlg=0x{aiKeyAlg:X}");

        // RSAPUBKEY
        uint magic = BitConverter.ToUInt32(data, offset); offset += 4; // 'RSA1' or 'RSA2'
        uint bitlen = BitConverter.ToUInt32(data, offset); offset += 4; // bit length of modulus
        uint pubexp = BitConverter.ToUInt32(data, offset); offset += 4; // public exponent (little-endian)
        Console.WriteLine($"RSAPUBKEY: magic=0x{magic:X}, bitlen={bitlen}, pubexp=0x{pubexp:X}");

        int modulusBytes = (int)(bitlen / 8);
        Console.WriteLine($"Modulus bytes: {modulusBytes}");

        var modulusLe = new byte[modulusBytes];
        Array.Copy(data, offset, modulusLe, 0, modulusBytes);
        offset += modulusBytes;

        // PUBLICKEYBLOB contains only public modulus; PRIVATEKEYBLOB continues with private key components
        // Convert little-endian modulus to big-endian
        var modulusBe = (byte[])modulusLe.Clone();
        Array.Reverse(modulusBe);
        Console.WriteLine("Modulus (hex, big-endian): " + BitConverter.ToString(modulusBe).Replace("-", ""));

        Console.WriteLine($"Exponent (decimal): {pubexp}");

        if (bType == 0x06)
        {
            // PUBLICKEYBLOB
            using var rsaPub = RSA.Create();
            rsaPub.ImportParameters(new RSAParameters { Modulus = modulusBe, Exponent = BitConverter.GetBytes(pubexp) });
            // Exponent bytes need to be big-endian
            var expBytes = BitConverter.GetBytes(pubexp);
            Array.Reverse(expBytes);
            rsaPub.ImportParameters(new RSAParameters { Modulus = modulusBe, Exponent = expBytes });
            Console.WriteLine($"RSA KeySize: {rsaPub.KeySize}");
            var spki = rsaPub.ExportSubjectPublicKeyInfo();
            string pem = "-----BEGIN PUBLIC KEY-----\n" + Convert.ToBase64String(spki, Base64FormattingOptions.InsertLineBreaks) + "\n-----END PUBLIC KEY-----\n";
            Console.WriteLine("Public key (SPKI PEM):\n" + pem);
            return 0;
        }

        // bType == 0x07 -> PRIVATEKEYBLOB: parse private key components (little-endian)
        Console.WriteLine("Parsing PRIVATEKEYBLOB components...");
        // The PRIVATEKEYBLOB layout after RSAPUBKEY for RSA is: privateExponent, p, q, dp, dq, inverseQ (all in little-endian)
        int half = modulusBytes / 2;
        var privateExpLe = new byte[modulusBytes];
        Array.Copy(data, offset, privateExpLe, 0, modulusBytes);
        offset += modulusBytes;

        var pLe = new byte[half];
        Array.Copy(data, offset, pLe, 0, half);
        offset += half;
        var qLe = new byte[half];
        Array.Copy(data, offset, qLe, 0, half);
        offset += half;
        var dpLe = new byte[half];
        Array.Copy(data, offset, dpLe, 0, half);
        offset += half;
        var dqLe = new byte[half];
        Array.Copy(data, offset, dqLe, 0, half);
        offset += half;
        var inverseQLe = new byte[half];
        Array.Copy(data, offset, inverseQLe, 0, half);
        offset += half;

        // Convert to big-endian
        Array.Reverse(modulusBe);
        var modulusFinal = modulusBe; // already big-endian

        var privExpBe = (byte[])privateExpLe.Clone(); Array.Reverse(privExpBe);
        var pBe = (byte[])pLe.Clone(); Array.Reverse(pBe);
        var qBe = (byte[])qLe.Clone(); Array.Reverse(qBe);
        var dpBe = (byte[])dpLe.Clone(); Array.Reverse(dpBe);
        var dqBe = (byte[])dqLe.Clone(); Array.Reverse(dqBe);
        var inverseQBe = (byte[])inverseQLe.Clone(); Array.Reverse(inverseQBe);

        Console.WriteLine($"PrivateExponent.Length={privExpBe.Length}, p.Length={pBe.Length}, q.Length={qBe.Length}");

        var rsaParams = new RSAParameters
        {
            Modulus = modulusFinal,
            Exponent = BitConverter.GetBytes(pubexp),
            D = privExpBe,
            P = pBe,
            Q = qBe,
            DP = dpBe,
            DQ = dqBe,
            InverseQ = inverseQBe,
        };

        // Ensure exponent is big-endian
        var expBe = BitConverter.GetBytes(pubexp); Array.Reverse(expBe); rsaParams.Exponent = expBe;

        using var rsa = RSA.Create();
        rsa.ImportParameters(rsaParams);
        Console.WriteLine($"Imported RSA private key. KeySize={rsa.KeySize}");
        var spkiFinal = rsa.ExportSubjectPublicKeyInfo();
        string pemFinal = "-----BEGIN PUBLIC KEY-----\n" + Convert.ToBase64String(spkiFinal, Base64FormattingOptions.InsertLineBreaks) + "\n-----END PUBLIC KEY-----\n";
        Console.WriteLine("Public key (from private, SPKI PEM):\n" + pemFinal);
        return 0;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine("Failed to parse Microsoft BLOB: " + ex);
        // fallthrough
    }
}

// Fallback: try to parse as PKCS#1/PKCS#8/PEM
try
{
    // Attempt to import as RSA private key in PKCS#1 or PKCS#8
    using var rsa = RSA.Create();
    try
    {
        rsa.ImportPkcs8PrivateKey(data, out _);
        Console.WriteLine("Imported as PKCS#8 private key");
    }
    catch
    {
        try
        {
            rsa.ImportRSAPrivateKey(data, out _);
            Console.WriteLine("Imported as PKCS#1 private key");
        }
        catch
        {
            var s = Encoding.UTF8.GetString(data);
            // try PEM
            if (s.Contains("-----BEGIN"))
            {
                var lines = s.Split(new[] {"\r\n", "\n"}, StringSplitOptions.None);
                var body = string.Join("", lines.Where(l => !l.StartsWith("-----"))); 
                var bin = Convert.FromBase64String(body);
                rsa.ImportPkcs8PrivateKey(bin, out _);
                Console.WriteLine("Imported as PEM PKCS#8 private key");
            }
            else
            {
                throw new Exception("Unknown private key format");
            }
        }
    }

    var pub = rsa.ExportSubjectPublicKeyInfo();
    Console.WriteLine("Exported SPKI length: " + pub.Length);
    string pemPub = "-----BEGIN PUBLIC KEY-----\n" + Convert.ToBase64String(pub, Base64FormattingOptions.InsertLineBreaks) + "\n-----END PUBLIC KEY-----\n";
    Console.WriteLine(pemPub);
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine("Failed to import as PKCS#* key: " + ex);
    return 2;
}
