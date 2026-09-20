param(
    [Parameter(Mandatory = $true)][string]$Name,
    [string]$AssetFile = "C:\Program Files (x86)\Steam\steamapps\common\Cities_Skylines\Cities_Data\resources.assets",
    [string]$OutDir = (Join-Path $env:TEMP "vc_shaderdump")
)

# Disassembles one of the game's compiled shaders, so its maths can be read instead of
# guessed at.
#
#   .\shaderdump.ps1 -Name "Custom/Lights/GroupVolume"
#
# A Unity 5.6 Shader object stores, after its name: two empty strings, a dependency count,
# a bool, then four uint32 arrays (platforms, offsets, compressedLengths, decompressedLengths)
# and one LZ4-compressed blob per platform. Platform 4 is Direct3D 11. Inside the
# decompressed blob are ordinary DXBC containers, which d3dcompiler_47.dll (ships with
# Windows) disassembles -- including the constant buffer layout with the original names.

$src = @'
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

public static class ShaderDump
{
    [DllImport("d3dcompiler_47.dll")]
    static extern int D3DDisassemble(IntPtr data, UIntPtr size, uint flags, string comments, out IntPtr blob);

    delegate IntPtr GetPointerFn(IntPtr self);
    delegate UIntPtr GetSizeFn(IntPtr self);
    delegate uint ReleaseFn(IntPtr self);

    static string Disassemble(byte[] dxbc)
    {
        IntPtr mem = Marshal.AllocHGlobal(dxbc.Length);
        try
        {
            Marshal.Copy(dxbc, 0, mem, dxbc.Length);
            IntPtr blob;
            int hr = D3DDisassemble(mem, (UIntPtr)dxbc.Length, 0, null, out blob);
            if (hr != 0 || blob == IntPtr.Zero) return "; D3DDisassemble failed hr=0x" + hr.ToString("X8");

            // ID3DBlob vtable: QueryInterface, AddRef, Release, GetBufferPointer, GetBufferSize
            IntPtr vtable = Marshal.ReadIntPtr(blob);
            var getPointer = (GetPointerFn)Marshal.GetDelegateForFunctionPointer(Marshal.ReadIntPtr(vtable, 3 * IntPtr.Size), typeof(GetPointerFn));
            var getSize = (GetSizeFn)Marshal.GetDelegateForFunctionPointer(Marshal.ReadIntPtr(vtable, 4 * IntPtr.Size), typeof(GetSizeFn));
            var release = (ReleaseFn)Marshal.GetDelegateForFunctionPointer(Marshal.ReadIntPtr(vtable, 2 * IntPtr.Size), typeof(ReleaseFn));

            string text = Marshal.PtrToStringAnsi(getPointer(blob), (int)getSize(blob));
            release(blob);
            return text;
        }
        finally { Marshal.FreeHGlobal(mem); }
    }

    static byte[] Lz4(byte[] src, int start, int length, int outLength)
    {
        byte[] dst = new byte[outLength];
        int ip = start, end = start + length, op = 0;

        while (ip < end)
        {
            int token = src[ip++];
            int lit = token >> 4;
            if (lit == 15) { int b; do { b = src[ip++]; lit += b; } while (b == 255); }
            Buffer.BlockCopy(src, ip, dst, op, lit);
            ip += lit; op += lit;
            if (ip >= end) break;

            int offset = src[ip] | (src[ip + 1] << 8);
            ip += 2;
            int match = token & 15;
            if (match == 15) { int b; do { b = src[ip++]; match += b; } while (b == 255); }
            match += 4;

            // Overlapping copies are the point of LZ4, so this must go byte by byte.
            for (int i = 0; i < match; i++, op++) dst[op] = dst[op - offset];
        }

        return dst;
    }

    static int Find(byte[] hay, byte[] needle, int from)
    {
        for (int i = from; i <= hay.Length - needle.Length; i++)
        {
            int j = 0;
            while (j < needle.Length && hay[i + j] == needle[j]) j++;
            if (j == needle.Length) return i;
        }
        return -1;
    }

    static uint U32(byte[] b, int o) { return BitConverter.ToUInt32(b, o); }
    static int Align4(int v) { return (v + 3) & ~3; }

    public static string Run(string assetFile, string shaderName, string outDir)
    {
        var log = new StringBuilder();
        byte[] data = File.ReadAllBytes(assetFile);
        byte[] nameBytes = Encoding.ASCII.GetBytes(shaderName);

        int at = -1;
        for (int from = 0; ; )
        {
            int hit = Find(data, nameBytes, from);
            if (hit < 0) break;
            // The real m_Name is length-prefixed and is followed by a non-name byte.
            if (hit >= 4 && U32(data, hit - 4) == nameBytes.Length) { at = hit; break; }
            from = hit + 1;
        }
        if (at < 0) return "shader name not found as a length-prefixed string";

        int p = Align4(at + nameBytes.Length);
        for (int i = 0; i < 2; i++) { int len = (int)U32(data, p); p = Align4(p + 4 + len); }   // editor, fallback names
        int deps = (int)U32(data, p); p += 4;
        if (deps != 0) return "shader has dependencies; layout walk not implemented for that";
        p += 4;                                                                                // m_DisableNoSubshadersMessage

        uint[][] arrays = new uint[4][];
        for (int a = 0; a < 4; a++)
        {
            int n = (int)U32(data, p); p += 4;
            if (n <= 0 || n > 16) return "unexpected array length " + n + " at " + p + "; not a Unity 5.6 shader layout?";
            arrays[a] = new uint[n];
            for (int i = 0; i < n; i++, p += 4) arrays[a][i] = U32(data, p);
        }

        int blobLength = (int)U32(data, p); p += 4;
        int blobStart = p;

        log.AppendLine("shader '" + shaderName + "' at 0x" + at.ToString("X") + ", blob " + blobLength + " bytes");
        log.AppendLine("platforms: " + string.Join(", ", Array.ConvertAll(arrays[0], x => x.ToString())));

        int index = Array.IndexOf(arrays[0], 4u);
        if (index < 0) return log + "no Direct3D 11 platform (4) in this shader";

        byte[] blob = Lz4(data, blobStart + (int)arrays[1][index], (int)arrays[2][index], (int)arrays[3][index]);
        Directory.CreateDirectory(outDir);

        string safe = shaderName.Replace('/', '_');
        byte[] magic = Encoding.ASCII.GetBytes("DXBC");
        int count = 0;

        for (int from = 0; ; )
        {
            int hit = Find(blob, magic, from);
            if (hit < 0) break;

            int size = (int)U32(blob, hit + 24);
            if (size <= 0 || hit + size > blob.Length) { from = hit + 4; continue; }

            byte[] dxbc = new byte[size];
            Buffer.BlockCopy(blob, hit, dxbc, 0, size);

            string text = Disassemble(dxbc);
            string kind = text.Contains("ps_") ? "ps" : (text.Contains("vs_") ? "vs" : "xx");
            string path = Path.Combine(outDir, safe + "." + count.ToString("00") + "." + kind + ".asm");
            File.WriteAllText(path, text);
            log.AppendLine("  [" + count + "] " + kind + "  " + size + " bytes -> " + path);

            count++;
            from = hit + size;
        }

        log.AppendLine(count + " DXBC program(s) disassembled");
        return log.ToString();
    }
}
'@

Add-Type -TypeDefinition $src -Language CSharp
[ShaderDump]::Run($AssetFile, $Name, $OutDir)
