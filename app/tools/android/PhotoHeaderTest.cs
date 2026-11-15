using System;
using System.Collections.Generic;
using System.IO;
using TravelAgent.Integrations.Photos;

internal static class PhotoHeaderTest
{
    private static int checks;
    public static void Main(string[] args)
    {
        foreach (bool little in new[] { true, false }) for (int orientation = 1; orientation <= 8; orientation++)
        {
            byte[] jpeg = Jpeg(orientation, little);
            var header = PhotoHeader.Read(jpeg);
            Check(header.width == 3 && header.height == 2 && header.orientation == orientation, "JPEG dimensions and all EXIF orientations, both TIFF byte orders");
            byte[] retained = (byte[])jpeg.Clone(); PhotoHeader.Read(jpeg);
            Check(Convert.ToBase64String(retained) == Convert.ToBase64String(jpeg), "metadata inspection preserves original bytes");
        }
        foreach (string path in args)
        {
            var header = PhotoHeader.Read(File.ReadAllBytes(path));
            Check(header.width > 0 && header.height > 0 && header.orientation == 1, "actual fixture PNG dimensions and missing-EXIF fallback");
        }
        Reject(new byte[8], "unknown format rejected");
        Reject(new byte[] {255,216,255,225,255,255,0,0}, "truncated JPEG segment rejected");
        Reject(new byte[20 * 1024 * 1024 + 1], "byte budget enforced before decoder");
        byte[] malicious = Jpeg(6, true); malicious[16] = 255; malicious[17] = 255; malicious[18] = 255; malicious[19] = 127;
        Check(PhotoHeader.Read(malicious).orientation == 1, "out-of-range EXIF pointer cannot read outside segment");
        byte[] png = new byte[33]; new byte[] {137,80,78,71,13,10,26,10}.CopyTo(png, 0); png[12]=73;png[13]=72;png[14]=68;png[15]=82;
        Reject(png, "zero dimensions rejected");
        png[16]=255;png[17]=255;png[18]=255;png[19]=255;png[23]=1;
        Reject(png, "overflowing PNG dimensions rejected before allocation");
        Console.WriteLine("PASS: " + checks + " photo header/EXIF/bounds assertions");
    }
    private static byte[] Jpeg(int orientation, bool little)
    {
        var tiff = new List<byte>(); tiff.Add((byte)(little ? 73 : 77)); tiff.Add(tiff[0]);
        Add16(tiff,42,little);Add32(tiff,8,little);Add16(tiff,1,little);Add16(tiff,274,little);Add16(tiff,3,little);Add32(tiff,1,little);Add16(tiff,orientation,little);Add16(tiff,0,little);Add32(tiff,0,little);
        var jpeg = new List<byte> {255,216,255,225,0,(byte)(tiff.Count+8),69,120,105,102,0,0};jpeg.AddRange(tiff);
        jpeg.AddRange(new byte[] {255,192,0,11,8,0,2,0,3,1,1,17,0,255,217});return jpeg.ToArray();
    }
    private static void Add16(List<byte> bytes,int value,bool little) { if(little){bytes.Add((byte)value);bytes.Add((byte)(value>>8));}else{bytes.Add((byte)(value>>8));bytes.Add((byte)value);} }
    private static void Add32(List<byte> bytes,int value,bool little) { if(little){Add16(bytes,value,little);Add16(bytes,value>>16,little);}else{Add16(bytes,value>>16,little);Add16(bytes,value,little);} }
    private static void Check(bool condition,string message){checks++;if(!condition)throw new Exception(message);}
    private static void Reject(byte[] bytes,string message){try{PhotoHeader.Read(bytes);}catch(IOException){checks++;return;}catch(OverflowException){checks++;return;}throw new Exception(message);}
}
