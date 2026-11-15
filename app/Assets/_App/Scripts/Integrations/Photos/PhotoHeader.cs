using System;
using System.IO;

namespace TravelAgent.Integrations.Photos
{
    /// <summary>Bound metadata parsing before the Editor allocates an image decoder.</summary>
    public sealed class PhotoHeader
    {
        public int width, height, orientation = 1;
        public static PhotoHeader Read(byte[] bytes)
        {
            if (bytes == null || bytes.Length < 8 || bytes.Length > 20 * 1024 * 1024) throw new IOException("Choose a PNG or JPEG image smaller than 20 MB.");
            var result = new PhotoHeader();
            if (bytes[0] == 137 && bytes[1] == 80 && bytes[2] == 78 && bytes[3] == 71 && bytes[4] == 13 && bytes[5] == 10 && bytes[6] == 26 && bytes[7] == 10)
            {
                if (bytes.Length < 33 || bytes[12] != 73 || bytes[13] != 72 || bytes[14] != 68 || bytes[15] != 82) throw new IOException("PNG dimensions are unavailable.");
                result.width = checked((int)Big32(bytes, 16)); result.height = checked((int)Big32(bytes, 20));
                for (long index = 8; index + 12 <= bytes.Length;)
                {
                    uint length = Big32(bytes, (int)index); if (index + length + 12 > bytes.Length) break;
                    if (bytes[index + 4] == 101 && bytes[index + 5] == 88 && bytes[index + 6] == 73 && bytes[index + 7] == 102) result.orientation = Exif(bytes, (int)index + 8, (int)length);
                    index += length + 12;
                }
            }
            else if (bytes[0] == 255 && bytes[1] == 216)
            {
                int index = 2;
                while (index + 3 < bytes.Length)
                {
                    if (bytes[index++] != 255) throw new IOException("JPEG header is damaged.");
                    while (index < bytes.Length && bytes[index] == 255) index++;
                    if (index >= bytes.Length) break;
                    int marker = bytes[index++];
                    if (marker == 217 || marker == 218) break;
                    if (marker == 1 || (marker >= 208 && marker <= 215)) continue;
                    if (index + 2 > bytes.Length) break;
                    int length = (bytes[index] << 8) | bytes[index + 1];
                    if (length < 2 || (long)index + length > bytes.Length) throw new IOException("JPEG header is truncated.");
                    if (marker == 225 && length >= 16 && bytes[index + 2] == 69 && bytes[index + 3] == 120 && bytes[index + 4] == 105 && bytes[index + 5] == 102 && bytes[index + 6] == 0 && bytes[index + 7] == 0)
                        result.orientation = Exif(bytes, index + 8, length - 8);
                    if ((marker >= 192 && marker <= 195) || (marker >= 197 && marker <= 199) || (marker >= 201 && marker <= 203) || (marker >= 205 && marker <= 207))
                    {
                        if (length < 8) throw new IOException("JPEG dimensions are unavailable.");
                        result.height = (bytes[index + 3] << 8) | bytes[index + 4]; result.width = (bytes[index + 5] << 8) | bytes[index + 6];
                    }
                    index += length;
                }
            }
            else throw new IOException("Choose a PNG or JPEG image.");
            if (result.width <= 0 || result.height <= 0 || result.width > 100000 || result.height > 100000) throw new IOException("The image dimensions are invalid or unsupported.");
            return result;
        }
        private static uint Big32(byte[] bytes, int at) { return ((uint)bytes[at] << 24) | ((uint)bytes[at + 1] << 16) | ((uint)bytes[at + 2] << 8) | bytes[at + 3]; }
        private static int Exif(byte[] bytes, int start, int length)
        {
            if (length < 8 || start < 0 || (long)start + length > bytes.Length) return 1;
            bool little = bytes[start] == 73 && bytes[start + 1] == 73;
            if (!little && !(bytes[start] == 77 && bytes[start + 1] == 77)) return 1;
            Func<int, ushort> shortAt = at => little ? (ushort)(bytes[at] | (bytes[at + 1] << 8)) : (ushort)((bytes[at] << 8) | bytes[at + 1]);
            Func<int, uint> longAt = at => little ? (uint)(bytes[at] | (bytes[at + 1] << 8) | (bytes[at + 2] << 16) | (bytes[at + 3] << 24)) : Big32(bytes, at);
            if (shortAt(start + 2) != 42) return 1;
            long ifd = start + (long)longAt(start + 4); if (ifd < start + 8 || ifd + 2 > start + length) return 1;
            int count = shortAt((int)ifd); if (count > 256 || ifd + 2 + count * 12L > start + length) return 1;
            for (int index = 0; index < count; index++)
            {
                int at = (int)ifd + 2 + index * 12;
                if (shortAt(at) == 274 && shortAt(at + 2) == 3 && longAt(at + 4) == 1) { int value = shortAt(at + 8); return value >= 1 && value <= 8 ? value : 1; }
            }
            return 1;
        }
    }
}
