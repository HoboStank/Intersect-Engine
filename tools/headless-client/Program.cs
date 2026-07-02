using System;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Intersect.Core;
using Intersect.Network;
using Intersect.Network.Packets.Unconnected.Client;
using Intersect.Configuration;
using Microsoft.Extensions.Logging;
using Intersect.Threading;
using Intersect.Plugins.Interfaces;
using Intersect.Plugins.Helpers;
using System.Runtime.Loader;

// Headless test client: directly instantiates ClientNetwork and attempts to connect to a running server.

// Ensure assembly resolution is configured before any code that may trigger Assembly.Load executes.
AppDomain.CurrentDomain.AssemblyResolve += (sender, resolveArgs) =>
{
    try
    {
        Console.WriteLine($"AssemblyResolve requested: {resolveArgs.Name}");
        var name = new AssemblyName(resolveArgs.Name).Name;
        if (string.Equals(name, "Intersect.Client.Core", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "Intersect Client Core", StringComparison.OrdinalIgnoreCase))
        {
            var baseDir = AppContext.BaseDirectory ?? Directory.GetCurrentDirectory();
            var candidates = new[] { Path.Combine(baseDir, "Intersect.Client.Core.dll"), Path.Combine(baseDir, "Intersect Client Core.dll") };
            foreach (var p in candidates)
            {
                if (File.Exists(p))
                {
                    try { return Assembly.LoadFrom(p); } catch { }
                }
            }
        }
    }
    catch { }

    return null;
};

// .NET Core/5+ uses AssemblyLoadContext for resolution; hook its Resolving event as well.
AssemblyLoadContext.Default.Resolving += (context, asmName) =>
{
    try
    {
        Console.WriteLine($"AssemblyLoadContext.Resolving requested: {asmName.Name}");
        var name = asmName.Name;
        if (string.Equals(name, "Intersect.Client.Core", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "Intersect Client Core", StringComparison.OrdinalIgnoreCase))
        {
            var baseDir = AppContext.BaseDirectory ?? Directory.GetCurrentDirectory();
            var candidates = new[] { Path.Combine(baseDir, "Intersect.Client.Core.dll"), Path.Combine(baseDir, "Intersect Client Core.dll") };
            foreach (var p in candidates)
            {
                if (File.Exists(p))
                {
                    try { return AssemblyLoadContext.Default.LoadFromAssemblyPath(p); } catch { }
                }
            }
        }
    }
    catch { }

    return null;
};

// Proactively load local assembly files with the expected names so Assembly.Load("Intersect.Client.Core") succeeds
try
{
    var baseDir = AppContext.BaseDirectory ?? Directory.GetCurrentDirectory();
    var path1 = Path.Combine(baseDir, "Intersect.Client.Core.dll");
    var path2 = Path.Combine(baseDir, "Intersect Client Core.dll");
    if (File.Exists(path1)) Assembly.LoadFrom(path1);
    else if (File.Exists(path2)) Assembly.LoadFrom(path2);
}
catch { /* best-effort */ }

// Log first-chance exceptions to help diagnose why Assembly.Load is failing
AppDomain.CurrentDomain.FirstChanceException += (sender, e) =>
{
    try
    {
        Console.WriteLine($"FirstChanceException: {e.Exception.GetType()}: {e.Exception.Message}");
    }
    catch { }
};

Console.WriteLine("Headless client starting...");

var host = args.Length > 0 ? args[0] : "127.0.0.1";
ushort port = args.Length > 1 ? ushort.Parse(args[1]) : (ushort)5400;

ClientConfiguration.Instance.Host = host;
ClientConfiguration.Instance.Port = port;

var loggerFactory = LoggerFactory.Create(builder => builder.AddSimpleConsole());
var logger = loggerFactory.CreateLogger("HeadlessClient");

// Minimal IApplicationContext implementation used only for networking
var appContext = new MinimalAppContext(loggerFactory);
// Make this the ambient ApplicationContext so library code that logs via ApplicationContext.Context.Value will emit here.
Intersect.Core.ApplicationContext.Context.Value = appContext;

// Help the runtime resolve the oddly-named assembly file "Intersect Client Core.dll"
AppDomain.CurrentDomain.AssemblyResolve += (sender, resolveArgs) =>
{
    try
    {
        var name = new AssemblyName(resolveArgs.Name).Name;
        if (string.Equals(name, "Intersect.Client.Core", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "Intersect Client Core", StringComparison.OrdinalIgnoreCase))
        {
            var baseDir = AppContext.BaseDirectory ?? Directory.GetCurrentDirectory();
            var candidates = new[] { Path.Combine(baseDir, "Intersect Client Core.dll"), Path.Combine(baseDir, "Intersect.Client.Core.dll") };
            foreach (var p in candidates)
            {
                if (File.Exists(p))
                {
                    return Assembly.LoadFrom(p);
                }
            }
        }
    }
    catch { }

    return null;
};

// Load embedded public key resource from the client core assembly.
// The assembly name in this repo is sometimes set to "Intersect Client Core" (space) while
// other code refers to "Intersect.Client.Core". Try several strategies to locate the assembly
// and the embedded resource so this headless harness works regardless of the assembly display name.
const string publicKeyResource = "Intersect.Client.network.handshake.bkey.pub";
Assembly? asm = null;
// Prefer already-loaded assemblies that contain the resource
asm = AppDomain.CurrentDomain.GetAssemblies()
    .FirstOrDefault(a => a.GetManifestResourceNames().Contains(publicKeyResource));

// If not loaded, try loading from the local output folder (handles "Intersect Client Core.dll" filename)
if (asm == null)
{
    var baseDir = AppContext.BaseDirectory ?? System.IO.Directory.GetCurrentDirectory();
    var paths = new[] { Path.Combine(baseDir, "Intersect Client Core.dll"), Path.Combine(baseDir, "Intersect.Client.Core.dll") };
    foreach (var p in paths)
    {
        if (File.Exists(p))
        {
            try
            {
                asm = Assembly.LoadFrom(p);
                if (asm.GetManifestResourceNames().Contains(publicKeyResource)) break;
                // otherwise discard and continue
                asm = null;
            }
            catch { asm = null; }
        }
    }
}

if (asm == null)
{
    throw new Exception("Could not locate the Intersect.Client.Core assembly containing the public handshake key resource.");
}

Stream? pubStream = null;
try
{
    Console.WriteLine($"Loaded assembly: {asm.FullName}");
    var resources = asm.GetManifestResourceNames();
    Console.WriteLine("Manifest resources:");
    foreach (var r in resources) Console.WriteLine("  " + r);
    pubStream = asm.GetManifestResourceStream(publicKeyResource);
}
catch { pubStream = null; }

// If we couldn't get an embedded resource, try a filesystem lookup for the public key produced by the build.
if (pubStream == null)
{
    // Heuristic: find the repo root by walking up until Intersect.sln exists, then look in Intersect.Network/bin/<Config>/keys
    string? repoRoot = FindRepoRoot(AppContext.BaseDirectory ?? Directory.GetCurrentDirectory());
    if (repoRoot != null)
    {
        var possibleKeys = new[] {
            Path.Combine(repoRoot, "Intersect.Network", "bin", "Debug", "keys", "network.handshake.bkey.pub"),
            Path.Combine(repoRoot, "Intersect.Network", "bin", "Release", "keys", "network.handshake.bkey.pub"),
            Path.Combine(repoRoot, "Intersect.Network", "bin", "DebugTests", "keys", "network.handshake.bkey.pub")
        };

        foreach (var p in possibleKeys)
        {
            if (File.Exists(p))
            {
                pubStream = File.OpenRead(p);
                break;
            }
        }

        // As a last resort, search the repo for the file name
        if (pubStream == null)
        {
            var matches = Directory.EnumerateFiles(repoRoot, "network.handshake.bkey.pub", SearchOption.AllDirectories);
            var first = matches.FirstOrDefault();
            if (first != null)
            {
                pubStream = File.OpenRead(first);
            }
        }
    }
}

if (pubStream == null)
{
    throw new Exception($"Missing resource {publicKeyResource} in assembly {asm?.FullName} and no network.handshake.bkey.pub found on disk.");
}

using var rsa = RSA.Create();
var pubBytes = new byte[pubStream.Length];
pubStream.Read(pubBytes, 0, pubBytes.Length);
Console.WriteLine($"Public key bytes length: {pubBytes.Length}");
var hexPreview = string.Join(' ', pubBytes.Take(Math.Min(64, pubBytes.Length)).Select(b => b.ToString("X2")));
Console.WriteLine("Public key hex (first 64 bytes): " + hexPreview);
try
{
    var ascii = Encoding.UTF8.GetString(pubBytes);
    Console.WriteLine("Public key ASCII preview (first 200 chars):");
    Console.WriteLine(ascii.Substring(0, Math.Min(200, ascii.Length)));
}
catch { }
// The client code expects an RsaKey -> RSAParameters; try several formats.
var parsed = false;
// 1) Try X.509 SubjectPublicKeyInfo (DER)
try
{
    var span = new ReadOnlySpan<byte>(pubBytes);
    rsa.ImportSubjectPublicKeyInfo(span, out _);
    parsed = true;
}
catch { }

if (!parsed)
{
    // 2) Try PEM
    try
    {
        var pem = Encoding.UTF8.GetString(pubBytes);
        var der = PemToDer(pem);
        rsa.ImportSubjectPublicKeyInfo(der, out _);
        parsed = true;
    }
    catch { }
}

if (!parsed)
{
    // 3) Try OpenSSH "ssh-rsa AAAAB3..." format
    try
    {
        var txt = Encoding.UTF8.GetString(pubBytes).Trim();
        if (txt.StartsWith("ssh-rsa "))
        {
            var parts = txt.Split(' ');
            if (parts.Length >= 2)
            {
                var blob = Convert.FromBase64String(parts[1]);
                using var ms = new MemoryStream(blob);
                static int ReadIntBig(Stream s)
                {
                    Span<byte> buf = stackalloc byte[4];
                    if (s.Read(buf) != 4) throw new InvalidOperationException("Unexpected EOF reading length");
                    if (BitConverter.IsLittleEndian) buf.Reverse();
                    return BitConverter.ToInt32(buf);
                }

                int ReadLen() => ReadIntBig(ms);
                string ReadString()
                {
                    var l = ReadLen();
                    var b = new byte[l];
                    if (ms.Read(b, 0, l) != l) throw new InvalidOperationException("Unexpected EOF reading string");
                    return Encoding.ASCII.GetString(b);
                }

                var keyType = ReadString();
                if (keyType != "ssh-rsa") throw new FormatException("Unexpected key type");
                var elen = ReadLen();
                var e = new byte[elen];
                if (ms.Read(e, 0, elen) != elen) throw new InvalidOperationException("Unexpected EOF reading exponent");
                var nlen = ReadLen();
                var n = new byte[nlen];
                if (ms.Read(n, 0, nlen) != nlen) throw new InvalidOperationException("Unexpected EOF reading modulus");

                // SSH uses big-endian byte order, which is what RSAParameters expects.
                var sshParams = new RSAParameters { Exponent = e, Modulus = n };
                rsa.ImportParameters(sshParams);
                parsed = true;
            }
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"OpenSSH parse failed: {ex.Message}");
    }
}

if (!parsed)
{
    // 4) Try Microsoft CAPI PUBLICKEYBLOB format (starts with BLOBHEADER + RSAPUBKEY, magic 'RSA1')
    try
    {
        // Validate minimum size for BLOBHEADER (8) + RSAPUBKEY (12)
        if (pubBytes.Length >= 20 && pubBytes[0] == 0x06)
        {
            // RSAPUBKEY starts at offset 8
            var magic = Encoding.ASCII.GetString(pubBytes, 8, 4);
            if (magic == "RSA1")
            {
                var bitlen = BitConverter.ToUInt32(pubBytes, 12); // little-endian
                var pubexp = BitConverter.ToUInt32(pubBytes, 16); // little-endian
                var modLen = (int)(bitlen / 8);
                if (pubBytes.Length >= 20 + modLen)
                {
                    var modLE = new byte[modLen];
                    Array.Copy(pubBytes, 20, modLE, 0, modLen);
                    // Convert modulus to big-endian expected by RSAParameters
                    Array.Reverse(modLE);
                    var modulus = modLE;

                    // Exponent is a 32-bit int, convert to big-endian minimal byte array
                    var eBytes = BitConverter.GetBytes(pubexp);
                    if (BitConverter.IsLittleEndian) Array.Reverse(eBytes);
                    // Trim leading zeros
                    int firstNonZero = 0;
                    while (firstNonZero < eBytes.Length && eBytes[firstNonZero] == 0) firstNonZero++;
                    var exponent = firstNonZero == eBytes.Length ? new byte[] { 0 } : eBytes.Skip(firstNonZero).ToArray();

                    var capiParams = new RSAParameters { Modulus = modulus, Exponent = exponent };
                    rsa.ImportParameters(capiParams);
                    parsed = true;
                }
            }
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"CAPI PUBLICKEYBLOB parse failed: {ex.Message}");
    }
}

if (!parsed)
{
    throw new FormatException("Unsupported public key format for network.handshake.bkey.pub");
}

var rsaParams = rsa.ExportParameters(false);
// Diagnostics: print key details so we can compare client-side public key with server-side private key
try
{
    Console.WriteLine($"Parsed public key: KeySize={rsa.KeySize}, ModulusLength={(rsaParams.Modulus?.Length ?? 0)}, ExponentLength={(rsaParams.Exponent?.Length ?? 0)}");
}
catch { }

var config = new NetworkConfiguration(host, (ushort)port);
var clientNetwork = new ClientNetwork(appContext, config, rsaParams);

Console.WriteLine($"Connecting to {host}:{port}...");
if (!clientNetwork.Connect())
{
    Console.WriteLine("ClientNetwork.Connect() returned false immediately.");
}

var sw = System.Diagnostics.Stopwatch.StartNew();
var connected = false;
clientNetwork.OnConnected += (_, __) => { connected = true; Console.WriteLine("OnConnected event received."); };
clientNetwork.OnConnectionApproved += (_, __) => { connected = true; Console.WriteLine("Connection approved."); };
clientNetwork.OnConnectionDenied += (_, __) => { Console.WriteLine("Connection denied."); };

while (sw.Elapsed.TotalSeconds < 10)
{
    // ClientNetwork uses its internal interface threads; just wait for events
    if (connected)
    {
        Console.WriteLine("Connected! Handshake succeeded.");
        return 0;
    }
    Thread.Sleep(100);
}

Console.WriteLine("Failed to connect within timeout.");
return 1;

static byte[] PemToDer(string pem)
{
    var header = "-----BEGIN PUBLIC KEY-----";
    var footer = "-----END PUBLIC KEY-----";
    var start = pem.IndexOf(header, StringComparison.Ordinal);
    if (start >= 0)
    {
        start += header.Length;
        var end = pem.IndexOf(footer, start, StringComparison.Ordinal);
        var base64 = pem.Substring(start, end - start).Replace("\n", string.Empty).Replace("\r", string.Empty).Trim();
        return Convert.FromBase64String(base64);
    }

    throw new FormatException("Not a PEM formatted key");
}

static string? FindRepoRoot(string start)
{
    var dir = new DirectoryInfo(start);
    while (dir != null)
    {
        if (File.Exists(Path.Combine(dir.FullName, "Intersect.sln"))) return dir.FullName;
        dir = dir.Parent;
    }

    return null;
}

// Minimal application context implementation matching IApplicationContext

class MinimalAppContext : IApplicationContext
{
    public MinimalAppContext(ILoggerFactory loggerFactory)
    {
        if (loggerFactory == null) throw new ArgumentNullException(nameof(loggerFactory));

        Logger = loggerFactory.CreateLogger("HeadlessClientContext");

        // Build the registries that PacketHelper expects. Register built-in packet types first so
        // packets like HailPacket are known to the serializer before any handlers attempt to use them.
        var packetTypeRegistryLogger = loggerFactory.CreateLogger<Intersect.Network.PacketTypeRegistry>();
        var packetHandlerRegistryLogger = loggerFactory.CreateLogger<Intersect.Network.PacketHandlerRegistry>();

        // Use the assembly that contains the HailPacket types as the builtin assembly so they are discovered.
        var packetTypeRegistry = new Intersect.Network.PacketTypeRegistry(
            packetTypeRegistryLogger,
            typeof(Intersect.Network.Packets.HailPacket).Assembly
        );

        // Attempt to register built-in packet types now. Log if registration fails so diagnostics are available.
        try
        {
            if (!packetTypeRegistry.TryRegisterBuiltIn())
            {
                loggerFactory.CreateLogger("HeadlessClient").LogWarning("PacketTypeRegistry.TryRegisterBuiltIn returned false; built-in packet types may not be available.");
            }
        }
        catch (Exception ex)
        {
            loggerFactory.CreateLogger("HeadlessClient").LogWarning(ex, "Exception while registering built-in packet types.");
        }

        var packetHandlerRegistry = new Intersect.Network.PacketHandlerRegistry(packetTypeRegistry, packetHandlerRegistryLogger);

        PacketHelper = new Intersect.Plugins.Helpers.PacketHelper(packetTypeRegistry, packetHandlerRegistry);
        // Make the serializer aware of the available packet types so PackedIntersectPacket can map types to keys.
        try
        {
            PackedIntersectPacket.AddKnownTypes(PacketHelper.AvailablePacketTypes);
            loggerFactory.CreateLogger("HeadlessClient").LogInformation(
                "PackedIntersectPacket known types populated: {Count}", PacketHelper.AvailablePacketTypes.Count);
        }
        catch (Exception ex)
        {
            loggerFactory.CreateLogger("HeadlessClient").LogWarning(ex, "Failed to populate PackedIntersectPacket known types.");
        }

        Services = new List<IApplicationService>();
        StartupOptions = new SimpleCommandLineOptions();
    }

    public bool HasErrors => false;
    public bool IsDisposed => false;
    public bool IsStarted => true;
    public bool IsRunning => true;
    public string Name => "HeadlessClient";
    public ICommandLineOptions StartupOptions { get; }
    public ILogger Logger { get; }
    public IPacketHelper PacketHelper { get; }
    public List<IApplicationService> Services { get; }

    public void Dispose() { }

    public TApplicationService GetService<TApplicationService>() where TApplicationService : IApplicationService => default!;

    public void Start(bool lockUntilShutdown = true) { }

    public ILockingActionQueue StartWithActionQueue() => new LockingActionQueue();

    private sealed class SimpleCommandLineOptions : ICommandLineOptions
    {
        public string WorkingDirectory { get; } = System.IO.Directory.GetCurrentDirectory();
        public IEnumerable<string> PluginDirectories { get; } = Array.Empty<string>();
    }
}
