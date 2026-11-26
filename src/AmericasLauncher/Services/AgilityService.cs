// SPDX-License-Identifier: GPL-2.0-only
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;

namespace AmericasLauncher.Services;

/// <summary>
/// Windows 10 support for the runners: the D3D12 Agility SDK.
///
/// The runners translate every console shader to shader model 6.6 with directly indexed
/// descriptor heaps. The Direct3D 12 runtime built into Windows 10 stops at shader model 6.5:
/// the runner says "the device supports SM 0x65 only", or every root signature is refused and
/// the title crashes at its first frames. An executable that exports <c>D3D12SDKVersion</c> and
/// <c>D3D12SDKPath</c> makes the system load a newer runtime, <c>D3D12\D3D12Core.dll</c> beside it,
/// instead (the Agility SDK).
///
/// No runner exports them, and the launcher does not change their source (its <c>git pull</c>
/// would then fail): on Windows 10 the build links a small object that holds the two exports
/// into the runner (<see cref="BuildEnvironmentAsync"/>), and the runtime is put beside the built
/// executable (<see cref="EnsureRuntimeAsync"/>). Windows 11 has shader model 6.6 itself: nothing
/// is done there.
/// </summary>
public static class AgilityService
{
    /// <summary>The SDK version the exports ask for: the package's minor version.</summary>
    public const int SdkVersion = 620;

    private const string PackageVersion = "1.620.2";

    /// <summary>SHA-256 of Microsoft.Direct3D.D3D12 1.620.2 from nuget.org: nothing else is installed.</summary>
    private const string PackageSha256 = "70489669c2bbeaf318b62d72b0ac3e753d521387bfc848c9763791c4e920bfcc";

    private static readonly string PackageUrl =
        $"https://api.nuget.org/v3-flatcontainer/microsoft.direct3d.d3d12/{PackageVersion}/microsoft.direct3d.d3d12.{PackageVersion}.nupkg";

    private static readonly string[] RuntimeDlls = ["D3D12Core.dll", "d3d12SDKLayers.dll"];

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(5) };

    /// <summary>
    /// The exports, as Rust (any runner's toolchain compiles it). The path is written as bytes,
    /// ".\D3D12\" and a terminating zero, so that no escaping can change it.
    /// </summary>
    private const string ExportsSource = """
        //! D3D12 Agility SDK exports, linked into a runner's executable by AmericasLauncher.
        #![no_std]
        #![allow(non_upper_case_globals)]
        #[no_mangle]
        #[used]
        pub static D3D12SDKVersion: u32 = 620;
        #[no_mangle]
        #[used]
        pub static D3D12SDKPath: &[u8; 9] = &[0x2E, 0x5C, 0x44, 0x33, 0x44, 0x31, 0x32, 0x5C, 0x00];
        """;

    private static string WorkDir => Path.Combine(Paths.Root, "agility");

    /// <summary>
    /// Whether this Windows needs the Agility SDK: Windows 10 (builds before 22000).
    /// <c>AMERICAS_AGILITY=1</c> forces it on (any Windows), <c>=0</c> off.
    /// </summary>
    public static bool Needed
    {
        get
        {
            var forced = Environment.GetEnvironmentVariable("AMERICAS_AGILITY");
            if (forced == "1") return true;
            if (forced == "0") return false;
            return OperatingSystem.IsWindows() && Environment.OSVersion.Version.Build < 22000;
        }
    }

    /// <summary>
    /// Environment for a runner's build commands on Windows 10: <c>CARGO_ENCODED_RUSTFLAGS</c>
    /// that link the exports object and export its two symbols from the executable.
    /// <c>null</c> when not needed, or when the object could not be made (the build then goes on
    /// as before and the runner keeps the system runtime).
    /// </summary>
    public static async Task<IReadOnlyDictionary<string, string?>?> BuildEnvironmentAsync(
        Action<string> log, CancellationToken ct = default)
    {
        if (!Needed) return null;
        var obj = Path.Combine(WorkDir, $"agility-exports-{SdkVersion}.obj");
        if (!File.Exists(obj))
        {
            Directory.CreateDirectory(WorkDir);
            var source = Path.Combine(WorkDir, "agility-exports.rs");
            await File.WriteAllTextAsync(source, ExportsSource, ct);
            log("[..] Windows 10: making the D3D12 Agility SDK exports for the runner");
            var code = await ProcessRunner.RunAsync(
                "rustc",
                "+stable-x86_64-pc-windows-msvc --edition 2021 --crate-type=lib --emit=obj " +
                $"-C codegen-units=1 -C opt-level=1 -C panic=abort \"{source}\" -o \"{obj}\"",
                WorkDir, log, ct);
            if (code != 0 || !File.Exists(obj))
            {
                log("[!!] Could not make the Agility SDK exports: the runner is built without Windows 10 support.");
                return null;
            }
        }

        // One flag per field, split on 0x1F by cargo: a path with spaces stays one argument.
        var flags = string.Join('\u001f',
            $"-Clink-arg={obj}",
            "-Clink-arg=/EXPORT:D3D12SDKVersion,DATA",
            "-Clink-arg=/EXPORT:D3D12SDKPath,DATA");
        log("[ok] Windows 10: the runner is built with the D3D12 Agility SDK exports");
        return new Dictionary<string, string?> { ["CARGO_ENCODED_RUSTFLAGS"] = flags };
    }

    /// <summary>
    /// Puts the Agility SDK runtime in <c>D3D12\</c> beside <paramref name="exePath"/> if it is not
    /// there: downloaded once from nuget.org and installed only if its SHA-256 is the expected one.
    /// Returns false if it could not be installed.
    /// </summary>
    public static async Task<bool> EnsureRuntimeAsync(string exePath, Action<string> log, CancellationToken ct = default)
    {
        if (!Needed) return true;
        var target = Path.Combine(Path.GetDirectoryName(exePath)!, "D3D12");
        if (RuntimeDlls.All(d => File.Exists(Path.Combine(target, d)))) return true;

        try
        {
            Directory.CreateDirectory(WorkDir);
            var package = Path.Combine(WorkDir, $"microsoft.direct3d.d3d12.{PackageVersion}.nupkg");
            if (!File.Exists(package) || !HashMatches(package))
            {
                log($"[..] Windows 10: downloading the D3D12 Agility SDK {PackageVersion} (nuget.org)");
                var partial = package + ".part";
                await using (var file = File.Create(partial))
                await using (var body = await Http.GetStreamAsync(PackageUrl, ct))
                {
                    await body.CopyToAsync(file, ct);
                }
                if (!HashMatches(partial))
                {
                    File.Delete(partial);
                    log("[!!] The Agility SDK package is not the expected one (SHA-256 differs): not installed.");
                    return false;
                }
                File.Move(partial, package, overwrite: true);
            }

            Directory.CreateDirectory(target);
            using var zip = ZipFile.OpenRead(package);
            foreach (var dll in RuntimeDlls)
            {
                var entry = zip.GetEntry($"build/native/bin/x64/{dll}")
                    ?? throw new InvalidDataException($"{dll} is missing from the package");
                entry.ExtractToFile(Path.Combine(target, dll), overwrite: true);
            }
            log($"[ok] Windows 10: D3D12 Agility SDK {PackageVersion} installed in {target}");
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception e)
        {
            log($"[!!] Could not install the D3D12 Agility SDK: {e.Message}");
            return false;
        }
    }

    private static bool HashMatches(string path)
    {
        using var file = File.OpenRead(path);
        var hash = Convert.ToHexString(SHA256.HashData(file));
        return string.Equals(hash, PackageSha256, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Whether the executable exports <c>D3D12SDKVersion</c> (it was built with the exports).
    /// Reads the PE export table; false for anything it cannot read.
    /// </summary>
    public static bool HasExports(string exePath)
    {
        try
        {
            return ExportNames(File.ReadAllBytes(exePath)).Contains("D3D12SDKVersion");
        }
        catch
        {
            return false;
        }
    }

    /// <summary>The names a PE32+ image exports.</summary>
    internal static HashSet<string> ExportNames(byte[] image)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        int I32(int at) => BitConverter.ToInt32(image, at);
        int U16(int at) => BitConverter.ToUInt16(image, at);

        if (image.Length < 0x40 || U16(0) != 0x5A4D) return names;            // "MZ"
        var pe = I32(0x3C);
        if (pe <= 0 || pe + 24 > image.Length || I32(pe) != 0x4550) return names;   // "PE\0\0"
        var sections = U16(pe + 6);
        var optionalSize = U16(pe + 20);
        var optional = pe + 24;
        if (U16(optional) != 0x20B) return names;                             // PE32+ only
        var exportRva = I32(optional + 112);
        if (exportRva == 0) return names;

        var table = optional + optionalSize;
        int Offset(int rva)
        {
            for (var i = 0; i < sections; i++)
            {
                var s = table + i * 40;
                var size = Math.Max(I32(s + 8), I32(s + 16));
                var start = I32(s + 12);
                if (rva >= start && rva < start + size) return rva - start + I32(s + 20);
            }
            return -1;
        }

        var directory = Offset(exportRva);
        if (directory < 0) return names;
        var count = I32(directory + 24);
        var namesAt = Offset(I32(directory + 32));
        if (namesAt < 0 || count < 0 || count > 65536) return names;
        for (var i = 0; i < count; i++)
        {
            var at = Offset(I32(namesAt + i * 4));
            if (at < 0) continue;
            var end = Array.IndexOf(image, (byte)0, at);
            if (end > at) names.Add(Encoding.ASCII.GetString(image, at, end - at));
        }
        return names;
    }
}
