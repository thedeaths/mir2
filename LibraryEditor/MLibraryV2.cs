using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace LibraryEditor
{
    public sealed class MLibraryV2
    {
        public const int LibVersion = 3;
        public int CurrentVersion;
        public static bool Load = true;
        public string FileName;

        public List<MImage> Images = new List<MImage>();
        public List<int> IndexList = new List<int>();
        public int Count;
        private bool _initialized;

        private BinaryReader _reader;
        private FileStream _stream;

        public MLibraryV2(string filename)
        {
            //colormap
            FileName = filename;
            Initialize();
            Close();
        }

        public void Initialize()
        {
            _initialized = true;

            if (!File.Exists(FileName))
                return;

            _stream = new FileStream(FileName, FileMode.Open, FileAccess.ReadWrite);
            _reader = new BinaryReader(_stream);
            CurrentVersion = _reader.ReadInt32();
            if (CurrentVersion < 2)
            {
                MessageBox.Show("Wrong version, expecting lib version: " + 2 + " found version: " + CurrentVersion.ToString() + ".", "Failed to open", MessageBoxButtons.OK, MessageBoxIcon.Error, MessageBoxDefaultButton.Button1);
                return;
            }
            Count = _reader.ReadInt32();
            Images = new List<MImage>();
            IndexList = new List<int>();

            for (int i = 0; i < Count; i++)
                IndexList.Add(_reader.ReadInt32());

            for (int i = 0; i < Count; i++)
                Images.Add(null);

            for (int i = 0; i < Count; i++)
                CheckImage(i);
        }

        public void Close()
        {
            if (_stream != null)
                _stream.Dispose();
            // if (_reader != null)
            //     _reader.Dispose();
        }

        public void Save()
        {
            Close();

            MemoryStream stream = new MemoryStream();
            BinaryWriter writer = new BinaryWriter(stream);

            Count = Images.Count;
            IndexList.Clear();

            int offSet = 8 + Count * 4;
            for (int i = 0; i < Count; i++)
            {
                IndexList.Add((int)stream.Length + offSet);
                Images[i].Save(writer, CurrentVersion);
                //Images[i] = null;
            }

            writer.Flush();
            byte[] fBytes = stream.ToArray();
            //  writer.Dispose();

            _stream = File.Create(FileName);
            writer = new BinaryWriter(_stream);
            writer.Write(LibVersion);
            writer.Write(Count);
            for (int i = 0; i < Count; i++)
                writer.Write(IndexList[i]);

            writer.Write(fBytes);
            writer.Flush();
            writer.Close();
            writer.Dispose();
            Close();
        }

        private void CheckImage(int index)
        {
            if (!_initialized)
                Initialize();

            if (Images == null || index < 0 || index >= Images.Count)
                return;

            if (Images[index] == null)
            {
                _stream.Position = IndexList[index];
                Images[index] = new MImage(_reader, CurrentVersion);
            }

            if (!Load) return;

            MImage mi = Images[index];
            if (!mi.TextureValid)
            {
                _stream.Seek(IndexList[index] + 12, SeekOrigin.Begin);
                mi.CreateTexture(/*_reader*/);
            }
        }

        public Point GetOffSet(int index)
        {
            if (!_initialized)
                Initialize();

            if (Images == null || index < 0 || index >= Images.Count)
                return Point.Empty;

            if (Images[index] == null)
            {
                _stream.Seek(IndexList[index], SeekOrigin.Begin);
                Images[index] = new MImage(_reader, CurrentVersion);
            }

            return new Point(Images[index].X, Images[index].Y);
        }

        public Size GetSize(int index)
        {
            if (!_initialized)
                Initialize();
            if (Images == null || index < 0 || index >= Images.Count)
                return Size.Empty;

            if (Images[index] == null)
            {
                _stream.Seek(IndexList[index], SeekOrigin.Begin);
                Images[index] = new MImage(_reader, CurrentVersion);
            }

            return new Size(Images[index].Width, Images[index].Height);
        }

        public MImage GetMImage(int index)
        {
            if (index < 0 || index >= Images.Count)
                return null;

            return Images[index];
        }

        public Bitmap GetPreview(int index)
        {
            if (index < 0 || index >= Images.Count)
                return new Bitmap(1, 1);

            MImage image = Images[index];

            if (image == null || image.Image == null)
                return new Bitmap(1, 1);

            if (image.Preview == null)
                image.CreatePreview();

            return image.Preview;
        }

        public void AddImage(Bitmap image, short x, short y)
        {
            MImage mImage = new MImage(image) { X = x, Y = y };

            Count++;
            Images.Add(mImage);
        }

        public void ReplaceImage(int Index, Bitmap image, short x, short y)
        {
            MImage mImage = new MImage(image) { X = x, Y = y };

            Images[Index] = mImage;
        }

        public void InsertImage(int index, Bitmap image, short x, short y)
        {
            MImage mImage = new MImage(image) { X = x, Y = y };

            Count++;
            Images.Insert(index, mImage);
        }

        public void RemoveImage(int index)
        {
            if (Images == null || Count <= 1)
            {
                Count = 0;
                Images = new List<MImage>();
                return;
            }
            Count--;

            Images.RemoveAt(index);
        }

        public static bool CompareBytes(byte[] a, byte[] b)
        {
            if (a == b) return true;

            if (a == null || b == null || a.Length != b.Length) return false;

            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;

            return true;
        }

        public void RemoveBlanks(bool safe = false)
        {
            for (int i = Count - 1; i >= 0; i--)
            {
                if (Images[i].FBytes == null || Images[i].FBytes.Length <= 24)
                {
                    if (!safe)
                        RemoveImage(i);
                    else if (Images[i].X == 0 && Images[i].Y == 0)
                        RemoveImage(i);
                }
            }
        }

        public void GenerateLightMasks()
        {
            if (!_initialized) return;
            if (Images == null) return;
            CurrentVersion = 3;
            for (int i = 0; i < Images.Count; i++)
            {
                //CheckImage(i);
                Images[i].GetLightImage();
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
        }

        public class LightMask
        {
            public Bitmap OriginalImage;
            public Bitmap Image;
            public Pixel[] Colors;
            public static int StartSize = 60;
            public int OffsetX, OffsetY;

            public LightMask(Bitmap Original)
            {
                if (Original == null) return;//will cause crashes :p
                OffsetX = StartSize * -1;
                OffsetY = StartSize * -1;
                OriginalImage = MakeGrayscale(Original);
                FillPixels();
                Bleed();
                CreateNewImage();
                for (int i = 0; i < Colors.Length; i++)
                    Colors[i] = null;
                Colors = null;
            }

            public unsafe void FillPixels()
            {
                int Width = OriginalImage.Width;
                int Height = OriginalImage.Height;
                Colors = new Pixel[Width * Height];
                BitmapData data = OriginalImage.LockBits(new Rectangle(0, 0, Width,Height), ImageLockMode.ReadOnly,
                                                 PixelFormat.Format32bppArgb);

                byte[] pixels = new byte[Width * Height * 4];
                
                Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);

                OriginalImage.UnlockBits(data);

                int index = 0;
                for (int i = 0; i < pixels.Length; i += 4)
                {
                    index = i / 4;
                    Colors[index] = new Pixel();
                    Colors[index].OriginalColor = pixels[i];
                    Colors[index].NewColor = pixels[i];
                }

            }

            byte BrightestColor = 0;
            public void Bleed()
            {                
                for (int i = 0; i < Colors.Length; i++)
                {
                    Colors[i].Processed = false;
                    if (BrightestColor < Colors[i].OriginalColor)
                        BrightestColor = Colors[i].OriginalColor;
                }
                for (int i = BrightestColor; i > 1/*testing*/; i--)
                {
                    BrightestColor = (byte)i;
                    for (int j = 0; j < Colors.Length; j++)
                    {
                        if (Colors[j].Processed) continue;
                        if (Colors[j].NewColor == i)
                        {
                            BleedPixel(j);
                            Colors[j].Processed = true;
                        }
                    }
                }

            }

            public void BleedPixel(int index)
            {
                int origx, origy, x, y;
                origx = index % OriginalImage.Width;
                origy = index / OriginalImage.Width;
                int newindex;
                for (int i = 0; i < 9; i++)
                {
                    x = (origx - 1) + (int)(i % 3);
                    y = (origy -1) + (int)(i / 3);
                    newindex = (y * OriginalImage.Width) + x;
                    if (((x == origx) && (y == origy)) || (x < 0) || (y < 0) || (x > OriginalImage.Width-1) || (y > OriginalImage.Height -1)) continue;
                    if ((Colors[newindex].Processed) || (Colors[newindex].NewColor == BrightestColor)) continue;
                    Colors[newindex].NewColor = GetNewcolor(Colors[newindex].NewColor, Colors[index].NewColor, 2-(i % 2));

                }
            }

            public byte GetNewcolor(byte OriginalColor, byte Neighbourcolor, int distance)
            {
                if ((distance == 2) && OriginalColor == BrightestColor - 2) return (byte)Math.Max(0, (int)BrightestColor - 1);
                float Reduction = 0.25f;
                //if ((OriginalColor == 0) && (Neighbourcolor < 100))
                //    Reduction = 0.15f;
                return (byte)(Math.Min((int)Math.Max(0,(BrightestColor - distance)),OriginalColor + (int)(Neighbourcolor * (Reduction * (3 - distance)) /*- 1*/)));
                //return (byte)(Math.Min((int)Math.Max(0, (BrightestColor - 1)), OriginalColor + (int)(Neighbourcolor * (0.5))));
            }

            public unsafe void CreateNewImage()
            {
                /*//orig
                byte[] pixels = new byte[OriginalImage.Width * OriginalImage.Height * 4];
                byte Color;
                for (int i = 0; i < Colors.Length;i++)
                {
                    Color = Colors[i].OriginalColor != 0 ? (byte)255 : Colors[i].NewColor;
                    pixels[i * 4] = Color;
                    pixels[i * 4 + 1] = Color;
                    pixels[i * 4 + 2] = Color;
                    pixels[i * 4 + 3] = (byte)(Color != 0? 255: 0);//probably should make alpha layer better :p
                }
                //*/
                //test code
                byte[] pixels = new byte[OriginalImage.Width * OriginalImage.Height];
                for (int i = 0; i < Colors.Length;i++)
                    pixels[i] = Colors[i].OriginalColor > 100? (byte)255:  Colors[i].NewColor;
                //*/
                Image = new Bitmap(OriginalImage.Width, OriginalImage.Height, PixelFormat.Format8bppIndexed);
                //Image = OriginalImage;

                BitmapData data = Image.LockBits(new Rectangle(0, 0, OriginalImage.Width, OriginalImage.Height), ImageLockMode.ReadWrite,
                                                 PixelFormat.Format8bppIndexed/*.Format32bppArgb*/);


                Marshal.Copy(pixels, 0, data.Scan0, pixels.Length);

                Image.UnlockBits(data);
            }

            public static Bitmap MakeGrayscale(Bitmap original)
            {
                if (original == null) return null;
                //create a blank bitmap the same size as original
                //original code
                Bitmap newBitmap = new Bitmap(original.Width + (StartSize * 2), original.Height + (StartSize * 2));
               //get a graphics object from the new image
                Graphics g = Graphics.FromImage(newBitmap);

               //create the grayscale ColorMatrix
                ColorMatrix colorMatrix = new ColorMatrix(
                new float[][] 
                {
                    new float[] {.3f, .3f, .3f, 0, 0},//0.3f
                    new float[] {.59f, .59f, .59f, 0, 0},//0.59f
                    new float[] {.11f, .11f, .11f, 0, 0},//0.11f
                    new float[] {0, 0, 0, 1, 0},
                    new float[] {0, 0, 0, 0, 1}
                    
                });

                //create some image attributes
                ImageAttributes attributes = new ImageAttributes();

                //set the color matrix attribute
                attributes.SetColorMatrix(colorMatrix);

                //draw the original image on the new image
                //using the grayscale color matrix
                g.DrawImage(original, new Rectangle(StartSize * 1, StartSize * 1, original.Width, original.Height),
                    0, 0, original.Width, original.Height, GraphicsUnit.Pixel, attributes);

               //dispose the Graphics object
                g.Dispose();
                return newBitmap;
            }

        }

        public class Pixel
        {
            public byte OriginalColor,NewColor;
            public bool Processed = false;
        }

        public sealed class MImage
        {
            public short Width, Height, X, Y, ShadowX, ShadowY;
            public byte Shadow;
            public int Length;
            public byte[] FBytes;
            public bool TextureValid;
            public Bitmap Image, Preview;

            //layer 2:
            public short MaskWidth, MaskHeight, MaskX, MaskY;

            public int MaskLength;
            public byte[] MaskFBytes;
            public Bitmap MaskImage;
            public Boolean HasMask;

            //layer 3:
            private Bitmap LightImage;
            public int LightLength;
            public short LightWidth, LightHeight, LightX, LightY;
            public byte[] LightFBytes;
            public Boolean HasLight;
            Color LightColor;

            public MImage(BinaryReader reader, int CurrentVersion)
            {
                //read layer 1
                Width = reader.ReadInt16();
                Height = reader.ReadInt16();
                X = reader.ReadInt16();
                Y = reader.ReadInt16();
                ShadowX = reader.ReadInt16();
                ShadowY = reader.ReadInt16();
                Shadow = reader.ReadByte();
                if (CurrentVersion >= 3)
                    HasLight = reader.ReadBoolean();
                Length = reader.ReadInt32();
                FBytes = reader.ReadBytes(Length);
                //check if there's a second layer and read it
                HasMask = ((Shadow >> 7) == 1) ? true : false;
                
                if (HasMask)
                {
                    MaskWidth = reader.ReadInt16();
                    MaskHeight = reader.ReadInt16();
                    MaskX = reader.ReadInt16();
                    MaskY = reader.ReadInt16();
                    MaskLength = reader.ReadInt32();
                    MaskFBytes = reader.ReadBytes(MaskLength);
                }
                if (CurrentVersion >= 3)
                {
                    if (HasLight)
                    {
                        LightColor = Color.FromArgb(reader.ReadInt32());
                        LightWidth = reader.ReadInt16();
                        LightHeight = reader.ReadInt16();
                        LightX = reader.ReadInt16();
                        LightY = reader.ReadInt16();
                        LightLength = reader.ReadInt32();
                        LightFBytes = reader.ReadBytes(LightLength);
                    }
                }
            }

            public MImage(byte[] image, short Width, short Height)//only use this when converting from old to new type!
            {
                FBytes = image;
                this.Width = Width;
                this.Height = Height;
            }

            public MImage(Bitmap image)
            {
                if (image == null)
                {
                    FBytes = new byte[0];
                    return;
                }

                Width = (short)image.Width;
                Height = (short)image.Height;

                Image = image;// FixImageSize(image);
                FBytes = ConvertBitmapToArray(Image);
            }

            public MImage(Bitmap image, Bitmap Maskimage)
            {
                if (image == null)
                {
                    FBytes = new byte[0];
                    return;
                }

                Width = (short)image.Width;
                Height = (short)image.Height;
                Image = image;// FixImageSize(image);
                FBytes = ConvertBitmapToArray(Image);
                if (Maskimage == null)
                {
                    MaskFBytes = new byte[0];
                    return;
                }
                HasMask = true;
                MaskWidth = (short)Maskimage.Width;
                MaskHeight = (short)Maskimage.Height;
                MaskImage = Maskimage;// FixImageSize(Maskimage);
                MaskFBytes = ConvertBitmapToArray(MaskImage);
            }

            private Bitmap FixImageSize(Bitmap input)
            {
                int w = input.Width + (4 - input.Width % 4) % 4;
                int h = input.Height + (4 - input.Height % 4) % 4;

                if (input.Width != w || input.Height != h)
                {
                    Bitmap temp = new Bitmap(w, h);
                    using (Graphics g = Graphics.FromImage(temp))
                    {
                        g.Clear(Color.Transparent);
                        g.InterpolationMode = InterpolationMode.NearestNeighbor;
                        g.DrawImage(input, 0, 0);
                        g.Save();
                    }
                    input.Dispose();
                    input = temp;
                }

                return input;
            }

            private unsafe Color AverageColorFromTexture()
            {
                if (Image == null) return Color.White;
                BitmapData data = Image.LockBits(new Rectangle(0, 0, Image.Width, Image.Height), ImageLockMode.ReadOnly,
                                                 PixelFormat.Format32bppArgb);

                byte[] pixels = new byte[Image.Width * Image.Height*4];

                Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);

                Image.UnlockBits(data);


                if (pixels.Length == 0) return Color.White;
                int red = 0, green = 0, blue = 0;
                bool foundcolor = false;
                int count = 0;
                for (int i = 0; i < (Width * Height * 4); i += 4)
                {
                    if (pixels[i + 3] == 0) continue;
                    if ((pixels[i] == 0) && (pixels[i] == 0) && (pixels[i] == 0)) continue;
                    if (!foundcolor)
                    {
                        foundcolor = true;
                        red = pixels[i + 2];
                        green = pixels[i + 1];
                        blue = pixels[i];
                        count++;
                        continue;
                    }
                    red += pixels[i + 2];
                    green += pixels[i + 1];
                    blue += pixels[i];
                    count++;
                }
                if (count == 0) return Color.White;
                return Color.FromArgb(255, Convert.ToInt32(red / count), (int)(green / count), (int)(blue / count));
            }


            private unsafe byte[] ConvertBitmapToArray(Bitmap input)
            {
                BitmapData data = input.LockBits(new Rectangle(0, 0, input.Width, input.Height), ImageLockMode.ReadOnly,
                                                 PixelFormat.Format32bppArgb);

                byte[] pixels = new byte[input.Width * input.Height * 4];

                Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);

                input.UnlockBits(data);

                for (int i = 0; i < pixels.Length; i += 4)
                {
                    if (pixels[i] == 0 && pixels[i + 1] == 0 && pixels[i + 2] == 0)
                        pixels[i + 3] = 0; //Make Transparent
                }

                byte[] compressedBytes;
                compressedBytes = Compress(pixels);

                return compressedBytes;
            }

            private unsafe byte[] ConvertBitmapTo8bitArray(Bitmap input)
            {
                BitmapData data = input.LockBits(new Rectangle(0, 0, input.Width, input.Height), ImageLockMode.ReadOnly,
                                                 PixelFormat.Format8bppIndexed);

                byte[] pixels = new byte[input.Width * input.Height];

                Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);

                input.UnlockBits(data);
                byte[] compressedBytes;
                compressedBytes = Compress(pixels);

                return compressedBytes;
            }


            public unsafe void CreateTexture(/*BinaryReader reader*/)
            {
                int w = Width;// +(4 - Width % 4) % 4;
                int h = Height;// +(4 - Height % 4) % 4;

                if (w == 0 || h == 0)
                    return;
                if ((w < 2) || (h < 2)) return;
                Image = new Bitmap(w, h);

                BitmapData data = Image.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadWrite,
                                                 PixelFormat.Format32bppArgb);

                byte[] dest = Decompress(FBytes);

                Marshal.Copy(dest, 0, data.Scan0, dest.Length);

                Image.UnlockBits(data);

                dest = null;

                if (HasMask)
                {
                    w = MaskWidth;// +(4 - MaskWidth % 4) % 4;
                    h = MaskHeight;// +(4 - MaskHeight % 4) % 4;

                    if (w == 0 || h == 0)
                    {
                        return;
                    }
                    if ((w < 2) || (h < 2)) return;

                    try
                    {
                        MaskImage = new Bitmap(w, h);

                        data = MaskImage.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadWrite,
                                                         PixelFormat.Format32bppArgb);

                        dest = Decompress(MaskFBytes);

                        Marshal.Copy(dest, 0, data.Scan0, dest.Length);

                        MaskImage.UnlockBits(data);
                    }
                    catch(Exception ex)
                    {
                        File.AppendAllText(@".\Error.txt",
                                       string.Format("[{0}] {1}{2}", DateTime.Now, ex, Environment.NewLine));
                    }
                }
                if (HasLight)
                {
                    w = LightWidth;
                    h = LightHeight;
                    if (w == 0 || h == 0) return;
                    if ((w < 2) || (h < 2)) return;

                    try
                    {
                        LightImage = new Bitmap(w, h, PixelFormat.Format8bppIndexed);
                        GetGrayScalePalette();
                        data = LightImage.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadWrite, PixelFormat.Format8bppIndexed);
                        dest = Decompress(LightFBytes);
                        Marshal.Copy(dest, 0, data.Scan0, dest.Length);
                        LightImage.UnlockBits(data);
                    }
                    catch(Exception ex)
                    {

                    }
                }
                dest = null;
            }

            private void GetGrayScalePalette()
            {
                ColorPalette palette = LightImage.Palette;
                Color[] _entries = palette.Entries;
                for (int i = 0; i < 256; i++)
                {
                    Color b = new Color();
                    b = Color.FromArgb((byte)i, (byte)i, (byte)i);
                    _entries[i] = b;
                }
                LightImage.Palette = palette;
            }

            public void Save(BinaryWriter writer, int CurrentVersion)
            {
                writer.Write(Width);
                writer.Write(Height);
                writer.Write(X);
                writer.Write(Y);
                writer.Write(ShadowX);
                writer.Write(ShadowY);
                writer.Write(HasMask ? (byte)(Shadow | 0x80) : (byte)Shadow);
                bool HasLight = LightImage != null;
                if (CurrentVersion > 2)                 
                    writer.Write(HasLight);
                writer.Write(FBytes.Length);
                writer.Write(FBytes);
                if (HasMask)
                {
                    writer.Write(MaskWidth);
                    writer.Write(MaskHeight);
                    writer.Write(MaskX);
                    writer.Write(MaskY);
                    writer.Write(MaskFBytes.Length);
                    writer.Write(MaskFBytes);
                }
                if (CurrentVersion > 2)
                {
                    if (HasLight)
                    {
                        writer.Write(LightColor.ToArgb());
                        writer.Write(LightWidth);
                        writer.Write(LightHeight);
                        writer.Write(LightX);
                        writer.Write(LightY);
                        LightFBytes = ConvertBitmapTo8bitArray(LightImage);
                        writer.Write(LightFBytes.Length);
                        writer.Write(LightFBytes);//could reduce filesize by saving the lightmask instead of the lightimage bytes> but compression will do pretty much the same really)
                        LightFBytes = null;
                    }
                }
            }

            public static byte[] Compress(byte[] raw)
            {
                using (MemoryStream memory = new MemoryStream())
                {
                    using (GZipStream gzip = new GZipStream(memory,
                    CompressionMode.Compress, true))
                    {
                        gzip.Write(raw, 0, raw.Length);
                    }
                    return memory.ToArray();
                }
            }

            static byte[] Decompress(byte[] gzip)
            {
                // Create a GZIP stream with decompression mode.
                // ... Then create a buffer and write into while reading from the GZIP stream.
                using (GZipStream stream = new GZipStream(new MemoryStream(gzip), CompressionMode.Decompress))
                {
                    const int size = 4096;
                    byte[] buffer = new byte[size];
                    using (MemoryStream memory = new MemoryStream())
                    {
                        int count = 0;
                        do
                        {
                            count = stream.Read(buffer, 0, size);
                            if (count > 0)
                            {
                                memory.Write(buffer, 0, count);
                            }
                        }
                        while (count > 0);
                        return memory.ToArray();
                    }
                }
            }

            public void CreatePreview()
            {
                if (Image == null)
                {
                    Preview = new Bitmap(1, 1);
                    return;
                }

                Preview = new Bitmap(64, 64);

                using (Graphics g = Graphics.FromImage(Preview))
                {
                    g.InterpolationMode = InterpolationMode.Low;//HighQualityBicubic
                    g.Clear(Color.Transparent);
                    int w = Math.Min((int)Width, 64);
                    int h = Math.Min((int)Height, 64);
                    g.DrawImage(Image, new Rectangle((64 - w) / 2, (64 - h) / 2, w, h), new Rectangle(0, 0, Width, Height), GraphicsUnit.Pixel);

                    g.Save();
                }
            }

            public Bitmap GetLightImage()
            {
                if (LightImage == null)
                {
                    LightMask Mask = new LightMask(Image);
                    LightImage = Mask.Image;
                    if (LightImage != null)
                    {
                        LightWidth = (short)Mask.Image.Width;
                        LightHeight = (short)Mask.Image.Height;
                        LightColor = AverageColorFromTexture();
                        LightX = (short)(Mask.OffsetX + X);
                        LightY = (short)(Mask.OffsetY + Y);
                        //LightFBytes = ConvertBitmapToArray(Mask.Image);
                    }
                    else
                    {
                        LightWidth = 0;
                        LightHeight = 0;
                        LightFBytes = new byte[0];
                        LightColor = Color.White;
                    }
                    Mask = null;
                }
                
                return LightImage;
            }
        }
    }
}