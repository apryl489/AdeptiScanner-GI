﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Windows.Input;
using Tesseract;

namespace GenshinArtifactOCR
{
    public partial class GenshinArtifactOCR : Form
    {
        private TesseractEngine tessEngine;
        private Bitmap img_Raw;
        private Bitmap img_Filtered;
        private int[] filtered_rows;
        Rectangle artifactArea;

        public static string appDir = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData) + @"\GenshinArtifactOCR";
        //These get filled on startup by other file
        public static List<string> Pieces = new List<string>();
        public static List<string> MainStats = new List<string>();
        public static List<string> Levels = new List<string>();
        public static List<string> Substats = new List<string>();
        public static List<string> Sets = new List<string>();
        public static List<string> Characters = new List<string>();
        public static List<Tuple<string, string>> Pieces_trans = new List<Tuple<string, string>>();
        public static List<Tuple<string, string, double>> MainStats_trans = new List<Tuple<string, string, double>>();
        public static List<Tuple<string, int>> Levels_trans = new List<Tuple<string, int>>();
        public static List<Tuple<string, string, double>> Substats_trans = new List<Tuple<string, string, double>>();
        public static List<Tuple<string, string>> Sets_trans = new List<Tuple<string, string>>();
        public static List<Tuple<string, string>> Characters_trans = new List<Tuple<string, string>>();

        public GenshinArtifactOCR()
        {
            InitializeComponent();
            tessEngine = new TesseractEngine(appDir + @"/tessdata", "en")
            {
                DefaultPageSegMode = PageSegMode.SingleLine
            };
            //simple junk defaults
            img_Raw = new Bitmap(image_preview.Width, image_preview.Height);
            using (Graphics g = Graphics.FromImage(img_Raw))
            {
                g.FillRectangle(Brushes.Black, 0, 0, img_Raw.Width, img_Raw.Height);
                g.FillRectangle(Brushes.White, img_Raw.Width / 8, img_Raw.Height / 8, img_Raw.Width * 6 / 8, img_Raw.Height * 6 / 8);
            }
            img_Filtered = new Bitmap(img_Raw);
            image_preview.Image = new Bitmap(img_Raw);
        }

        /// <summary>
        /// Empties all text boxes
        /// </summary>
        private void resetTextBoxes ()
        {
            text_full.Text = "";
            text_Set.Text = "";
            text_Level.Text = "";
            text_Type.Text = "";
            text_statMain.Text = "";
            text_statSub1.Text = "";
            text_statSub2.Text = "";
            text_statSub3.Text = "";
            text_statSub4.Text = "";
            text_character.Text = "";
        }

        private void displayInventoryItem (InventoryItem item)
        {
            text_full.Text = "";
            if (item.set != null)
            {
                text_full.Text += item.set.Item1 + Environment.NewLine;
                text_Set.Text = item.set.Item1;
            }
            if (item.level != null)
            {
                text_full.Text += item.level.Item1 + Environment.NewLine;
                text_Level.Text = item.level.Item1;
            }
            if (item.piece != null)
            {
                text_full.Text += item.piece.Item1 + Environment.NewLine;
                text_Type.Text = item.piece.Item1;
            }
            if (item.main != null)
            {
                text_full.Text += item.main.Item1 + Environment.NewLine;
                text_statMain.Text = item.main.Item1;
            }
            if (item.subs != null)
            {
                if (item.subs.Count > 0)
                {
                    text_full.Text += item.subs[0].Item1 + Environment.NewLine;
                    text_statSub1.Text = item.subs[0].Item1;
                }
                if (item.subs.Count > 1)
                {
                    text_full.Text += item.subs[1].Item1 + Environment.NewLine;
                    text_statSub2.Text = item.subs[1].Item1;
                }
                if (item.subs.Count > 2)
                {
                    text_full.Text += item.subs[2].Item1 + Environment.NewLine;
                    text_statSub3.Text = item.subs[2].Item1;
                }
                if (item.subs.Count > 3)
                {
                    text_full.Text += item.subs[3].Item1 + Environment.NewLine;
                    text_statSub4.Text = item.subs[3].Item1;
                }
            }
            if (item.character != null)
            {
                text_full.Text += item.character.Item1 + Environment.NewLine;
                text_character.Text = item.character.Item1;
            }
        }

        /// <summary>
        /// Find artifact area from an image of the backpack
        /// </summary>
        /// <param name="img">Full screenshot containing game</param>
        /// <param name="gameArea">Area of the screenshot containing only the game</param>
        /// <returns>Area of <paramref name="img"/> containing the artifact info</returns>
        private Rectangle findArtifactArea_WindowMode(Bitmap img, Rectangle gameArea)
        {
            //Cut out relevant part of image
            gameArea = new Rectangle(gameArea.X + gameArea.Width / 2, gameArea.Y, gameArea.Width / 2, gameArea.Height);
            Bitmap areaImg = new Bitmap(gameArea.Width, gameArea.Height);
            using (Graphics g = Graphics.FromImage(areaImg))
            {
                g.DrawImage(img, 0, 0, gameArea, GraphicsUnit.Pixel);
            }

            int[] cols = new int[gameArea.Width];
            //prepare bytewise image processing
            BitmapData imgData = areaImg.LockBits(new Rectangle(0, 0, gameArea.Width, gameArea.Height), ImageLockMode.ReadWrite, areaImg.PixelFormat);
            int numBytes = Math.Abs(imgData.Stride) * imgData.Height;
            byte[] imgBytes = new byte[numBytes];
            Marshal.Copy(imgData.Scan0, imgBytes, 0, numBytes);
            areaImg.UnlockBits(imgData);
            int PixelSize = 4; //ARGB, reverse order

            for (int i = 0; i < numBytes; i += PixelSize)
            {
                int x = (i / PixelSize) % gameArea.Width;
                if ((imgBytes[i] > 210 && imgBytes[i + 1] > 210 && imgBytes[i + 2] > 210)) //look for white-ish text background
                {
                    cols[x]++;
                }
            }

            //Find artifact text columns
            int edgewidth = 0;
            //find right edge
            while (cols[cols.Length - 1 - edgewidth] / (double)gameArea.Height < 0.05)
                edgewidth++;
            int rightmost =  cols.Length - (int)(edgewidth * 2);
            //find left edge
            int leftmost = rightmost - edgewidth;
            int misses = 0;
            while (leftmost - misses > 0 && misses < edgewidth)
            {
                if (cols[leftmost - misses] / (double)gameArea.Height > 0.20)
                {
                    leftmost -= misses + 1;
                    misses = 0;
                }
                else
                {
                    misses++;
                }
            }
            leftmost += 3;

            return new Rectangle(gameArea.Left + leftmost, gameArea.Top, rightmost - leftmost, gameArea.Height);
        }


        /// <summary>
        /// Find artifact area from an image of the character equipment
        /// </summary>
        /// <param name="img">Full screenshot containing game</param>
        /// <param name="gameArea">Area of the screenshot containing only the game</param>
        /// <returns>Area of <paramref name="img"/> containing the artifact info</returns>
        private Rectangle findArtifactArea(Bitmap img, Rectangle gameArea)
        {
            //Cut out relevant part of image
            gameArea = new Rectangle(gameArea.X + gameArea.Width / 2, gameArea.Y, gameArea.Width / 2, gameArea.Height);
            Bitmap areaImg = new Bitmap(gameArea.Width, gameArea.Height);
            using (Graphics g = Graphics.FromImage(areaImg))
            {
                g.DrawImage(img, 0, 0, gameArea, GraphicsUnit.Pixel);
            }

            int[] cols = new int[gameArea.Width];

            //prepare bytewise image processing
            BitmapData imgData = areaImg.LockBits( new Rectangle(0, 0, gameArea.Width, gameArea.Height), ImageLockMode.ReadWrite, areaImg.PixelFormat);
            int numBytes = Math.Abs(imgData.Stride) * imgData.Height;
            byte[] imgBytes = new byte[numBytes];
            Marshal.Copy(imgData.Scan0, imgBytes, 0, numBytes);
            areaImg.UnlockBits(imgData);
            int PixelSize = 4; //ARGB, reverse order

            for (int i = 0; i < numBytes; i += PixelSize)
            {
                int x = (i / PixelSize) % gameArea.Width;
                if (imgBytes[i + 1] > 220 || (imgBytes[i] > 160 && imgBytes[i + 1] > 160 && imgBytes[i + 2] > 160)) //look for green or white-ish text
                {
                    cols[x]++;
                }
            }

            //Find artifact text columns
            int edgewidth = 0;
            //find right edge
            while (cols[cols.Length - 1 - edgewidth] / (double)gameArea.Height < 0.01)
                edgewidth++;
            int rightmost = cols.Length - edgewidth * 2;
            //find left edge
            int leftmost = rightmost - edgewidth;
            int misses = 0;
            while (leftmost - misses > 0 && misses < edgewidth * 2)
            {
                if (cols[leftmost - misses] / (double)gameArea.Height > 0.05)
                {
                    leftmost -= misses + 1;
                    misses = 0;
                }
                else
                {
                    misses++;
                }
            }
            leftmost -= edgewidth / 2;

            return new Rectangle(gameArea.Left + leftmost, gameArea.Top, rightmost - leftmost, gameArea.Height);
        }


        /// <summary>
        /// Extract and filter the artifact area from an image of the backpack
        /// </summary>
        /// <param name="img">Full screenshot containing game</param>
        /// <param name="area">Area containing the artifact info</param>
        /// <param name="rows">Filter results per row</param>
        /// <returns>Filtered image of the artifact area</returns>
        private Bitmap getArtifactImg_WindowMode(Bitmap img, Rectangle area, out int[] rows)
        {
            rows = new int[area.Height];
            //Get relevant part of image
            Bitmap areaImg = new Bitmap(area.Width, area.Height);
            using (Graphics g = Graphics.FromImage(areaImg))
            {
                g.DrawImage(img, 0, 0, area, GraphicsUnit.Pixel);
            }
            //Prepare bytewise image processing
            BitmapData imgData = areaImg.LockBits(new Rectangle(0, 0, areaImg.Width, areaImg.Height), ImageLockMode.ReadWrite, areaImg.PixelFormat);
            int numBytes = Math.Abs(imgData.Stride) * imgData.Height;
            byte[] imgBytes = new byte[numBytes];
            Marshal.Copy(imgData.Scan0, imgBytes, 0, numBytes);
            int PixelSize = 4; //ARGB, reverse order
            //some variables to keep track of which part of the image we are in
            int section = 0; //0 = top part, 1 = artifact level part, 2 = substat and set, 3 = character
            int secOneStart = 0;
            int secOneEnd = 0;
            for (int i = 0; i < numBytes; i += PixelSize)
            {
                int x = (i / PixelSize) % areaImg.Width;
                int y = (i / PixelSize - x) / areaImg.Width;
                if ( 
                    (section == 0 && (x < areaImg.Width * 0.65 && imgBytes[i] > 140 && imgBytes[i + 1] > 140 && imgBytes[i + 2] > 140)) //look for white-ish text, skip right edge (artifact image)
                    || (section == 1 && (imgBytes[i] > 240 && imgBytes[i + 1] > 240 && imgBytes[i + 2] > 240)) //look for bright white text
                    || (section == 2 && (imgBytes[i] < 150 && imgBytes[i + 1] < 150 && imgBytes[i + 2] < 150)) //look for black
                    || (section == 3 && ((imgBytes[i] < 120 && imgBytes[i + 1] > 160 && imgBytes[i + 2] < 120) 
                        || (imgBytes[i] < 110 && imgBytes[i + 1] < 100 && imgBytes[i + 2] < 100))) //look for green or black
                    )
                {
                    //Make Black
                    imgBytes[i] = 0;
                    imgBytes[i + 1] = 0;
                    imgBytes[i + 2] = 0;
                    imgBytes[i + 3] = 255;

                    rows[y]++;
                }
                else
                {
                    //Make White
                    imgBytes[i] = 255;
                    imgBytes[i + 1] = 255;
                    imgBytes[i + 2] = 255;
                    imgBytes[i + 3] = 255;
                }

                if (x == 0)
                {
                    if (section == 0)
                    {
                        //check if coming row is white-ish, if so move to section 1
                        int tmp = (y * areaImg.Width + (int)(areaImg.Width * 0.05)) * PixelSize;
                        if ((imgBytes[tmp] > 200 && imgBytes[tmp + 1] > 200 && imgBytes[tmp + 2] > 200) && (imgBytes[tmp] < 240 && imgBytes[tmp + 1] < 240 && imgBytes[tmp + 2] < 240))
                        {
                            section = 1;
                            i += areaImg.Width * PixelSize;
                        }

                    } else if (section == 1)
                    {
                        if (y == secOneEnd)
                        {
                            section = 2;
                            secOneStart = 0;
                            secOneEnd = 0;
                        } else if (secOneEnd == 0 && secOneStart != 0 && rows[y - 1] == 0)
                        {
                            secOneEnd = y + (y - secOneStart);
                        }else if (secOneStart == 0 && rows[y - 1] != 0)
                        {
                            secOneStart = y - 1;
                        }

                    } else if (section == 2)
                    {
                        if (y == secOneEnd)
                        {
                            y -= (secOneStart - secOneEnd) / 5;
                            section = 3;
                            secOneStart = 0;
                            secOneEnd = 0;
                        }
                        else if (secOneEnd == 0 && secOneStart != 0 && rows[y - 1] == 0)
                        {
                            secOneEnd = y + (y - secOneStart);
                        }
                        else if (rows[y - 2] == 0 && rows[y - 1] != 0)
                        {
                            secOneStart = y - 1;
                            secOneEnd = 0;
                        }
                    }
                }
            }
            Marshal.Copy(imgBytes, 0, imgData.Scan0, numBytes);
            areaImg.UnlockBits(imgData);

            string timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH-mm-ssff");
            if (checkbox_saveImages.Checked)
                areaImg.Save(appDir + @"\images\GenshinArtifactImgFiltered " + timestamp + ".png");
            return areaImg;
        }

        /// <summary>
        /// Extract and filter the artifact area from an image of the character equipment
        /// </summary>
        /// <param name="img">Full screenshot containing game</param>
        /// <param name="area">Area containing the artifact info</param>
        /// <param name="rows">Filter results per row</param>
        /// <returns>Filtered image of the artifact area</returns>
        private Bitmap getArtifactImg(Bitmap img, Rectangle area, out int[] rows)
        {
            rows = new int[area.Height];
            //Get relevant part of image
            Bitmap areaImg = new Bitmap(area.Width, area.Height);
            using (Graphics g = Graphics.FromImage(areaImg))
            {
                g.DrawImage(img, 0, 0, area, GraphicsUnit.Pixel);
            }
            //Prepare bytewise image processing
            BitmapData imgData = areaImg.LockBits(new Rectangle(0, 0, areaImg.Width, areaImg.Height), ImageLockMode.ReadWrite, areaImg.PixelFormat);
            int numBytes = Math.Abs(imgData.Stride) * imgData.Height;
            byte[] imgBytes = new byte[numBytes];
            Marshal.Copy(imgData.Scan0, imgBytes, 0, numBytes);
            int PixelSize = 4; //ARGB, reverse order

            for (int i = 0; i < numBytes; i += PixelSize)
            {
                int x = (i / PixelSize) % areaImg.Width;
                int y = (i / PixelSize - x) / areaImg.Width;
                if (imgBytes[i + 1] > 220 || (imgBytes[i] > 160 && imgBytes[i + 1] > 160 && imgBytes[i + 2] > 160)) //look for green or white-ish text
                {
                    //Make Black
                    imgBytes[i] = 0;
                    imgBytes[i + 1] = 0;
                    imgBytes[i + 2] = 0;
                    imgBytes[i + 3] = 255;

                    rows[y]++;
                }
                else
                {
                    //Make White
                    imgBytes[i] = 255;
                    imgBytes[i + 1] = 255;
                    imgBytes[i + 2] = 255;
                    imgBytes[i + 3] = 255;
                }
            }
            Marshal.Copy(imgBytes, 0, imgData.Scan0, numBytes);
            areaImg.UnlockBits(imgData);

            string timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH-mm-ssff");
            if (checkbox_saveImages.Checked)
                areaImg.Save(appDir + @"\images\GenshinArtifactArea " + timestamp + ".png");
            return areaImg;
        }

        /// <summary>
        /// Capture screenshot of main screen
        /// </summary>
        /// <returns>Screenshot of main screen</returns>
        private Bitmap CaptureScreenshot()
        {
            Bitmap img = new Bitmap(Screen.PrimaryScreen.Bounds.Width, Screen.PrimaryScreen.Bounds.Height);
            Size fullSize = new Size(img.Width, img.Height);
            using (Graphics g = Graphics.FromImage(img))
            {
                g.CopyFromScreen(0, 0, 0, 0, fullSize);
            }
            string timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH-mm-ssff");
            if (checkbox_saveImages.Checked)
                img.Save(appDir + @"\images\GenshinFullscreen " + timestamp + ".png");
            return img;
        }

        /// <summary>
        /// Find approximate area containing the game
        /// </summary>
        /// <param name="full">Screenshot of main monitor</param>
        /// <returns>Area containing game</returns>
        private Rectangle findGameArea(Bitmap full)
        {
            //Prepare bytewise image processing
            BitmapData imgData = full.LockBits(new Rectangle(0, 0, full.Width, full.Height), ImageLockMode.ReadWrite, full.PixelFormat);
            int numBytes = Math.Abs(imgData.Stride) * imgData.Height;
            byte[] imgBytes = new byte[numBytes];
            Marshal.Copy(imgData.Scan0, imgBytes, 0, numBytes);
            int PixelSize = 4; //ARGB, reverse order
            full.UnlockBits(imgData);

            int minWidth = Screen.PrimaryScreen.Bounds.Width / 4;
            int x = full.Width / 2; //probing via middle of screen, looking for white window header
            for (int y = full.Height / 2; y > 0; y--)
            {
                int i_pos = 0;
                int i_neg = 0;
                int index = (y * full.Width + x) * PixelSize;
                Color pixel = Color.FromArgb(imgBytes[index + 3], imgBytes[index + 2], imgBytes[index + 1], imgBytes[index]);

                //explore white area right
                while ( x + i_pos < full.Width*0.99 && pixel.R > 220 && pixel.G > 220 && pixel.B > 220) 
                {
                    i_pos++;
                    index = (y * full.Width + x + i_pos) * PixelSize;
                    pixel = Color.FromArgb(imgBytes[index + 3], imgBytes[index + 2], imgBytes[index + 1], imgBytes[index]);
                }

                if (i_pos == 0)
                    continue;

                index = (y * full.Width + x) * PixelSize;
                pixel = Color.FromArgb(imgBytes[index + 3], imgBytes[index + 2], imgBytes[index + 1], imgBytes[index]);
                //explore white area left
                while (x - i_neg > full.Width * 0.01 && pixel.R > 220 && pixel.G > 220 && pixel.B > 220)
                {
                    i_neg++;
                    index = (y * full.Width + x - i_neg) * PixelSize;
                    pixel = Color.FromArgb(imgBytes[index + 3], imgBytes[index + 2], imgBytes[index + 1], imgBytes[index]);
                }

                //check if feasible game window size
                if (i_pos + i_neg < minWidth)
                    continue;

                int top = y + 1;
                int left = x - i_neg;
                int width = (i_pos + i_neg);

                //find bottom
                int height = 10;
                while (height < full.Height - y - 1)
                {
                    int row = 0;
                    int currStreak = 0;
                    int maxStreak = 0;
                    for (int i = 0; i < width * 0.3; i++)
                    {
                        index = ((y + height) * full.Width + left + i) * PixelSize;
                        pixel = Color.FromArgb(imgBytes[index + 3], imgBytes[index + 2], imgBytes[index + 1], imgBytes[index]);
                        if (pixel.R > 230 && pixel.G > 220 && pixel.B > 210)
                        {
                            row++;
                            currStreak++;
                            if (currStreak > maxStreak)
                                maxStreak = currStreak;
                        } else
                        {
                            currStreak = 0;
                        }
                    }

                    if (row > width * 0.3 * 0.65 && maxStreak > width * 0.3 * 0.25)
                        break;

                    height++;
                }
                return new Rectangle(left, top, width, height);
            }
            return new Rectangle(0, 0, full.Width, full.Height);
        }

        /// <summary>
        /// Select and load screenshot, taken from WFInfo but heavily cut down
        /// </summary>
        /// <returns>Loaded screenshot, or empty 1x1 bitmap on failure</returns>
        private Bitmap LoadScreenshot()
        {
            Bitmap img = new Bitmap(1, 1);
            // Using WinForms for the openFileDialog because it's simpler and much easier
            using (OpenFileDialog openFileDialog = new OpenFileDialog())
            {
                openFileDialog.InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
                openFileDialog.Filter = "image files (*.png)|*.png|All files (*.*)|*.*";
                openFileDialog.FilterIndex = 2;
                openFileDialog.RestoreDirectory = true;
                openFileDialog.Multiselect = true;

                if (openFileDialog.ShowDialog() == DialogResult.OK)
                {
                    try
                    {
                        foreach (string file in openFileDialog.FileNames)
                        {
                            img = new Bitmap(file);
                            break;
                        }
                    }
                    catch (Exception e)
                    {

                    }
                }
            }
            return img;
        }

        /// <summary>
        /// Get Levenshtein Distance between two strings, taken from WFInfo and slightly modified
        /// </summary>
        /// <param name="s">One of the words to compare</param>
        /// <param name="t">Second word to compare</param>
        /// <returns>Levenshtein distance between <paramref name="s"/> and <paramref name="t"/>, after some filtering</returns>
        public int LevenshteinDistance(string s, string t)
        {
            // Levenshtein Distance determines how many character changes it takes to form a known result
            // For more info see: https://en.wikipedia.org/wiki/Levenshtein_distance
            s = s.ToLower();
            t = t.ToLower();
            s = Regex.Replace(s, @"[+,. ]", "");
            t = Regex.Replace(t, @"[+,. ]", "");
            int n = s.Length;
            int m = t.Length;
            int[,] d = new int[n + 1, m + 1];

            if (n == 0 || m == 0)
                return n + m;

            d[0, 0] = 0;

            int count = 0;
            for (int i = 1; i <= n; i++)
                d[i, 0] = (s[i - 1] == ' ' ? count : ++count);

            count = 0;
            for (int j = 1; j <= m; j++)
                d[0, j] = (t[j - 1] == ' ' ? count : ++count);

            for (int i = 1; i <= n; i++)
                for (int j = 1; j <= m; j++)
                {
                    // deletion of s
                    int opt1 = d[i - 1, j];
                    if (s[i - 1] != ' ')
                        opt1++;

                    // deletion of t
                    int opt2 = d[i, j - 1];
                    if (t[j - 1] != ' ')
                        opt2++;

                    // swapping s to t
                    int opt3 = d[i - 1, j - 1];
                    if (t[j - 1] != s[i - 1])
                        opt3++;
                    d[i, j] = Math.Min(Math.Min(opt1, opt2), opt3);
                }



            return d[n, m];
        }

        /// <summary>
        /// Get closest match according to Levenshtein Distance, taken from WFInfo but slightly modified
        /// </summary>
        /// <param name="rawText">Word to find match for</param>
        /// <param name="validText">List of words to match against</param>
        /// <param name="dist">Levenshtein distance to closest match</param>
        /// <returns>Closest matching word</returns>
        private string FindClosestMatch(string rawText, List<string> validText, out int index, out int dist)
        {
            string lowest = "ERROR";
            dist = 9999;
            index = 0;

            for (int i = 0; i < validText.Count; i++)
            {
                string validWord = validText[i];
                int val = LevenshteinDistance(validWord, rawText);
                if (val < dist)
                {
                    index = i;
                    dist = val;
                    lowest = validWord;
                }
            }
            return lowest;
        }

        /// <summary>
        /// Use OCR to read text from row
        /// </summary>
        /// <param name="img">Image to read, filtered</param>
        /// <param name="start">Starting row of <paramref name="img"/> to read</param>
        /// <param name="stop">Ending row of <paramref name="img"/> to read</param>
        /// <param name="validText">List of words to match against</param>
        /// <param name="dist">Levenshtein distance to closest match</param>
        /// <param name="rawText">Raw result from OCR process (appended to <paramref name="prevRaw"/>)</param>
        /// <param name="prevRaw">String to append in front of raw OCR result before searching for match</param>
        /// <returns>Closest matching word</returns>
        private string OCRRow(Bitmap img, int start, int stop, List<string> validText, out int index, out int dist, out string rawText, string prevRaw)
        {

            tessEngine.SetVariable("tessedit_char_whitelist", @"abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ9876543210+%,:() ");
            //Copy relevant part of image
            int height = stop - start;
            Bitmap scanArea = new Bitmap(img.Width, height);
            using (Graphics g = Graphics.FromImage(scanArea))
            {
                Rectangle sourceRect = new Rectangle(0, start, img.Width, height);
                g.DrawImage(img, 0, 0, sourceRect, GraphicsUnit.Pixel);
            }


            string timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH-mm-ssff");
            if (checkbox_saveImages.Checked)
                scanArea.Save(appDir + @"\images\GenshinTextRow " + timestamp + ".png");

            //Do OCR and append to prevRaw
            string text = prevRaw;
            using ( var page = tessEngine.Process(scanArea, PageSegMode.SparseText))
            {
                using (var iterator = page.GetIterator())
                {
                    iterator.Begin();
                    do
                    {
                        text += iterator.GetText(PageIteratorLevel.TextLine);
                    } while (iterator.Next(PageIteratorLevel.TextLine));
                }
            }
            //mild filtering
            text = Regex.Replace(text, @"\s+", "");
            rawText = text;

            string bestMatch = FindClosestMatch(text, validText, out index, out dist);
            Console.WriteLine("\nGot (" + dist + ") \"" + bestMatch + "\" from \"" + text + "\"");

            return bestMatch;
        }

        /// <summary>
        /// Extract stats from image to window text boxes
        /// </summary>
        /// <param name="img">Image of artifact area, filtered</param>
        /// <param name="rows">Filter results per row</param>
        private InventoryItem getArtifacts(Bitmap img, int[] rows)
        {
            //get all potential text rows
            List<Tuple<int, int>> textRows = new List<Tuple<int, int>>();
            int i = 0;
            while ( i+1 < img.Height)
            {
                while ( i+1 < img.Height && rows[i] / (double)img.Width < 0.01)
                    i++;
                int rowTop = i;
                while (i+1 < img.Height && !(rows[i] / (double)img.Width < 0.01))
                    i++;
                textRows.Add(Tuple.Create(Math.Max(0, rowTop-3), Math.Min(img.Height-1, i + 3)));
            }

            //first row guaranteed to be of no use (artifact name etc)
            textRows.RemoveAt(0);


            InventoryItem foundArtifact = new InventoryItem();
            i = 0;
            //Piece type
            for (; i < textRows.Count; i++)
            {
                string result = OCRRow(img, textRows[i].Item1, textRows[i].Item2, Pieces, out int resultIndex, out int dist, out _, "");
                if (dist < 3)
                {
                    foundArtifact.piece = Pieces_trans[resultIndex];
                    i++;
                    break;
                }
            }

            //Main stat
            string prevRaw = "";
            for (; i < textRows.Count; i++)
            {
                string result = OCRRow(img, textRows[i].Item1, textRows[i].Item2, MainStats, out int resultIndex, out int dist, out string rawText, prevRaw);
                if (dist < rawText.Length - 2 && rawText.Any(char.IsDigit))
                {
                    foundArtifact.main = MainStats_trans[resultIndex];
                    i++;
                    break;
                }
                prevRaw = rawText;
            }

            //Level
            for (; i < textRows.Count; i++)
            {
                string result = OCRRow(img, textRows[i].Item1, textRows[i].Item2, Levels, out int resultIndex, out int dist, out string rawText, "");
                if (rawText.Length != 0)
                {
                    foundArtifact.level = Levels_trans[resultIndex];
                    i++;
                    break;
                }
            }

            //Substats
            foundArtifact.subs = new List<Tuple<string, string, double>>();
            int substat = 0;
            for (; i < textRows.Count; i++)
            {
                string result = OCRRow(img, textRows[i].Item1, textRows[i].Item2, Substats, out int resultIndex, out int dist, out string rawText, "");
                if (dist < 3)
                {
                    foundArtifact.subs.Add(Substats_trans[resultIndex]);
                    if (substat > 2)
                    {
                        i++;
                        break;
                    }
                    substat++;
                } else if (substat > 2 && rawText.Length > 5)
                {
                    break;
                }
            }

            //Set
            int startRow = i;
            prevRaw = "";
            for (; i < textRows.Count; i++)
            {
                string result = OCRRow(img, textRows[i].Item1, textRows[i].Item2, Sets, out int resultIndex, out int dist, out string rawText, prevRaw);
                if (dist < 5)
                {
                    foundArtifact.set = Sets_trans[resultIndex];
                    break;
                }
                else
                {
                    prevRaw = rawText;
                    if (startRow - i > 4)
                        break;
                }
            }

            //Character
            for (i = textRows.Count - 1; i > Math.Max(0, textRows.Count - 3); i--)
            {
                string result = OCRRow(img, textRows[i].Item1, textRows[i].Item2, Characters, out int resultIndex, out int dist, out _, "");
                if (dist < 5)
                {
                    foundArtifact.character = Characters_trans[resultIndex];
                    break;
                }
            }

            return foundArtifact;
        }

        private void btn_capture_Click(object sender, EventArgs e)
        {
            resetTextBoxes();
            if (Keyboard.IsKeyDown(Key.LeftShift))
            {
                img_Raw = LoadScreenshot();
            } else
            {
                img_Raw = CaptureScreenshot();
            }


            Rectangle gameArea = findGameArea(img_Raw);
            if (checkbox_inventorymode.Checked)
            {
                artifactArea = findArtifactArea_WindowMode(img_Raw, gameArea);
            } else
            {
                artifactArea = findArtifactArea(img_Raw, gameArea);
            }
            if (artifactArea.Width == 0 || artifactArea.Height == 0)
            {
                artifactArea = gameArea;
                image_preview.Image = new Bitmap(img_Raw);
            }

            string timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH-mm-ssff");
            if (checkbox_saveImages.Checked)
            {
                Bitmap gameImg = new Bitmap(gameArea.Width, gameArea.Height);
                using (Graphics g = Graphics.FromImage(gameImg))
                {
                    g.DrawImage(img_Raw, 0, 0, gameArea, GraphicsUnit.Pixel);
                }
                gameImg.Save(appDir + @"\images\GenshinGameArea " + timestamp + ".png");
                Bitmap artifactImg = new Bitmap(artifactArea.Width, artifactArea.Height);
                using (Graphics g = Graphics.FromImage(artifactImg))
                {
                    g.DrawImage(img_Raw, 0, 0, artifactArea, GraphicsUnit.Pixel);
                }
                artifactImg.Save(appDir + @"\images\GenshinArtifactArea " + timestamp + ".png");
            }


            if (artifactArea.Width == 0 || artifactArea.Height == 0)
            {
                image_preview.Image = new Bitmap(img_Raw);
            }
            image_preview.Image = new Bitmap(artifactArea.Width, artifactArea.Height);
            using (Graphics g = Graphics.FromImage(image_preview.Image))
            {
                g.DrawImage(img_Raw, 0, 0, artifactArea, GraphicsUnit.Pixel);
            }
        }

        private void btn_OCR_Click(object sender, EventArgs e)
        {

            if (checkbox_OCRcapture.Checked)
            {
                if (Keyboard.IsKeyDown(Key.LeftShift))
                {
                    img_Raw = LoadScreenshot();
                }
                else
                {
                    img_Raw = CaptureScreenshot();
                }
            }


            img_Filtered = new Bitmap(img_Raw);
            if (checkbox_inventorymode.Checked)
            {
                img_Filtered = getArtifactImg_WindowMode(img_Filtered, artifactArea, out filtered_rows);
            } else
            {
                img_Filtered = getArtifactImg(img_Filtered, artifactArea, out filtered_rows);
            }

            image_preview.Image = new Bitmap(img_Filtered);

            InventoryItem artifact = getArtifacts(img_Filtered, filtered_rows);
            displayInventoryItem(artifact);
        }

    }
}
