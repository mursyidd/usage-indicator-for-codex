Set-StrictMode -Version Latest

function Set-UsageIndicatorVersionResource {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$FilePath,
        [Parameter(Mandatory)][string]$ProductVersion,
        [Parameter(Mandatory)][string]$OriginalFilename
    )

    if ($ProductVersion -cnotmatch '^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$') {
        throw "ProductVersion must be a stable semantic version: $ProductVersion"
    }

    if (-not ('UsageIndicatorVersionResource' -as [type])) {
        Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

public static class UsageIndicatorVersionResource
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr BeginUpdateResource(string fileName, bool deleteExistingResources);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool UpdateResource(
        IntPtr update,
        IntPtr type,
        IntPtr name,
        ushort language,
        byte[] data,
        uint dataSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool EndUpdateResource(IntPtr update, bool discard);

    public static void Apply(string filePath, string productVersion, string originalFilename)
    {
        Version version = Version.Parse(productVersion);
        byte[] resource = BuildResource(version, productVersion, originalFilename);
        IntPtr update = BeginUpdateResource(filePath, false);
        if (update == IntPtr.Zero)
        {
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        }

        bool committed = false;
        try
        {
            if (!UpdateResource(update, new IntPtr(16), new IntPtr(1), 0x0409, resource, (uint)resource.Length))
            {
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            }

            if (!EndUpdateResource(update, false))
            {
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            }

            committed = true;
        }
        finally
        {
            if (!committed)
            {
                EndUpdateResource(update, true);
            }
        }
    }

    private static byte[] BuildResource(Version version, string displayVersion, string originalFilename)
    {
        byte[] fixedInfo;
        using (var stream = new MemoryStream())
        using (var writer = new BinaryWriter(stream))
        {
            writer.Write(0xFEEF04BDu);
            writer.Write(0x00010000u);
            writer.Write(((uint)version.Major << 16) | (uint)version.Minor);
            writer.Write(((uint)version.Build << 16));
            writer.Write(((uint)version.Major << 16) | (uint)version.Minor);
            writer.Write(((uint)version.Build << 16));
            writer.Write(0x0000003Fu);
            writer.Write(0u);
            writer.Write(0x00040004u);
            writer.Write(1u);
            writer.Write(0u);
            writer.Write(0u);
            writer.Write(0u);
            fixedInfo = stream.ToArray();
        }

        var strings = new List<KeyValuePair<string, string>>
        {
            new KeyValuePair<string, string>("CompanyName", "Usage Indicator for Codex contributors"),
            new KeyValuePair<string, string>("FileDescription", "Usage Indicator for Codex command launcher"),
            new KeyValuePair<string, string>("FileVersion", displayVersion),
            new KeyValuePair<string, string>("InternalName", originalFilename),
            new KeyValuePair<string, string>("OriginalFilename", originalFilename),
            new KeyValuePair<string, string>("ProductName", "Usage Indicator for Codex"),
            new KeyValuePair<string, string>("ProductVersion", displayVersion)
        };
        var stringChildren = new List<byte[]>();
        foreach (var pair in strings)
        {
            byte[] value = Encoding.Unicode.GetBytes(pair.Value + "\0");
            stringChildren.Add(BuildBlock(pair.Key, (ushort)(pair.Value.Length + 1), 1, value));
        }

        byte[] stringTable = BuildBlock("040904B0", 0, 1, Array.Empty<byte>(), stringChildren);
        byte[] stringFileInfo = BuildBlock(
            "StringFileInfo",
            0,
            1,
            Array.Empty<byte>(),
            new[] { stringTable });
        byte[] translation = { 0x09, 0x04, 0xB0, 0x04 };
        byte[] translationBlock = BuildBlock("Translation", 4, 0, translation);
        byte[] varFileInfo = BuildBlock(
            "VarFileInfo",
            0,
            1,
            Array.Empty<byte>(),
            new[] { translationBlock });
        return BuildBlock(
            "VS_VERSION_INFO",
            (ushort)fixedInfo.Length,
            0,
            fixedInfo,
            new[] { stringFileInfo, varFileInfo });
    }

    private static byte[] BuildBlock(
        string key,
        ushort valueLength,
        ushort type,
        byte[] value,
        IEnumerable<byte[]> children = null)
    {
        using (var stream = new MemoryStream())
        using (var writer = new BinaryWriter(stream, Encoding.Unicode, true))
        {
            writer.Write((ushort)0);
            writer.Write(valueLength);
            writer.Write(type);
            writer.Write(Encoding.Unicode.GetBytes(key + "\0"));
            Align(writer);
            writer.Write(value);
            Align(writer);
            if (children != null)
            {
                foreach (byte[] child in children)
                {
                    writer.Write(child);
                    Align(writer);
                }
            }

            byte[] block = stream.ToArray();
            byte[] length = BitConverter.GetBytes((ushort)block.Length);
            block[0] = length[0];
            block[1] = length[1];
            return block;
        }
    }

    private static void Align(BinaryWriter writer)
    {
        while ((writer.BaseStream.Position & 3) != 0)
        {
            writer.Write((byte)0);
        }
    }
}
'@
    }

    $resolvedPath = (Resolve-Path -LiteralPath $FilePath).Path
    [UsageIndicatorVersionResource]::Apply(
        $resolvedPath,
        $ProductVersion,
        $OriginalFilename)
}
