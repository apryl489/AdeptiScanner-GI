﻿using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace GenshinArtifactOCR
{
    static class Program
    {


        private static System.Globalization.CultureInfo culture = new System.Globalization.CultureInfo("en-GB", false);

        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main()
        {
            Directory.CreateDirectory(GenshinArtifactOCR.appDir);
            Directory.CreateDirectory(GenshinArtifactOCR.appDir + @"\tessdata");
            Directory.CreateDirectory(GenshinArtifactOCR.appDir + @"\images");
            Directory.CreateDirectory(GenshinArtifactOCR.appDir + @"\filterdata");
            GenerateFilters();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new GenshinArtifactOCR());
        }

        static void ReadMainStats(JArray mainStats)
        {
            foreach (JObject mainStat in mainStats)
            {
                foreach (KeyValuePair<string, JToken> statNameTup in mainStat["name"].ToObject<JObject>())
                {
                    string statName = statNameTup.Key;
                    string statKey = statNameTup.Value.ToObject<string>();

                    foreach (double statValue in mainStat["value"].ToObject<List<double>>())
                    {
                        string text = statName + statValue.ToString("N0", culture);
                        if (statName.Contains("%"))
                        {
                            text = statName + statValue.ToString("N1", culture);
                            text = text.Replace("%", "") + "%";
                        }
                        GenshinArtifactOCR.MainStats.Add(text);
                        GenshinArtifactOCR.MainStats_trans.Add(Tuple.Create(text, statKey, statValue));
                        //Console.WriteLine(text);
                    }
                }
            }
        }

        static void readSubstats(JArray substats)
        {
            foreach (JObject substat in substats)
            {
                foreach (KeyValuePair<string, JToken> statNameTup in substat["name"].ToObject<JObject>())
                {
                    string statName = statNameTup.Key;
                    string statKey = statNameTup.Value.ToObject<string>();
                    List<int> baserolls = new List<int>();
                    List<int> rolls = new List<int>();
                    foreach (double statValue in substat["rolls"].ToObject<List<double>>())
                    {
                        baserolls.Add((int)(statValue * 100));
                        rolls.Add((int)(statValue * 100));

                    }

                    int start = 0;
                    int stop = rolls.Count;
                    for (int i = 0; i < 4; i++)
                    {
                        for (int j = start; j < stop; j++)
                        {
                            foreach (int value in baserolls)
                            {
                                int tmp = rolls[j] + value;
                                if (!rolls.Contains(tmp))
                                    rolls.Add(tmp);
                            }
                        }
                        start = stop;
                        stop = rolls.Count;
                    }
                    foreach (int value_int in rolls)
                    {
                        double value = value_int / 100.0;
                        string text = statName + value.ToString("N0", culture);
                        if (statName.Contains("%"))
                        {
                            text = statName + value.ToString("N1", culture);
                            text = text.Replace("%", "") + "%";
                        }
                        GenshinArtifactOCR.Substats.Add(text);
                        GenshinArtifactOCR.Substats_trans.Add(Tuple.Create(text, statKey, value));
                        //Console.WriteLine(text);
                    }
                }
            }
        }

        static void readSets(JArray sets)
        {
            foreach (JObject set in sets)
            {
                foreach (KeyValuePair<string, JToken> statNameTup in set["name"].ToObject<JObject>())
                {
                    string statName = statNameTup.Key;
                    string statKey = statNameTup.Value.ToObject<string>();
                    string text = statName + "";
                    GenshinArtifactOCR.Sets.Add(text);
                    GenshinArtifactOCR.Sets_trans.Add(Tuple.Create(text, statKey));
                    for (int i = 0; i < 6; i++)
                    {
                        text = statName + ":(" + i + ")";
                        GenshinArtifactOCR.Sets.Add(text);
                        GenshinArtifactOCR.Sets_trans.Add(Tuple.Create(text, statKey));
                        //Console.WriteLine(text);
                    }
                }
            }
        }

        static void readCharacters(JArray characters)
        {
            foreach (JObject character in characters)
            {
                foreach (KeyValuePair<string, JToken> statNameTup in character["name"].ToObject<JObject>())
                {
                    string statName = statNameTup.Key;
                    string statKey = statNameTup.Value.ToObject<string>();
                    string text =  "Equipped: " + statName;
                    GenshinArtifactOCR.Characters.Add(text);
                    GenshinArtifactOCR.Characters_trans.Add(Tuple.Create(text, statKey));
                    Console.WriteLine(text);
                }
            }
        }

        static void readPieces(JArray pieces)
        {
            foreach (JObject piece in pieces)
            {
                foreach (KeyValuePair<string, JToken> statNameTup in piece["name"].ToObject<JObject>())
                {
                    string statName = statNameTup.Key;
                    string statKey = statNameTup.Value.ToObject<string>();
                    string text = statName;
                    GenshinArtifactOCR.Pieces.Add(text);
                    GenshinArtifactOCR.Pieces_trans.Add(Tuple.Create(text, statKey));
                    Console.WriteLine(text);
                }
            }
        }

        /// <summary>
        /// Generate all possible text to look for and assign to filter word lists
        /// </summary>
        static void GenerateFilters()
        {
            //Main stat filter
            JObject allJson = JsonConvert.DeserializeObject<JObject>(File.ReadAllText(GenshinArtifactOCR.appDir + @"\filterdata\ArtifactInfo.json"));
            foreach (KeyValuePair<string, JToken> entry in allJson)
            {
                JArray entry_arr = entry.Value.ToObject<JArray>();
                if (entry.Key == "MainStats")
                {
                    ReadMainStats(entry_arr);
                }

                if (entry.Key == "Substats")
                {
                    readSubstats(entry_arr);
                }

                if (entry.Key == "Sets")
                {
                    readSets(entry_arr);
                }

                if (entry.Key == "Characters")
                {
                    readCharacters(entry_arr);
                }

                if (entry.Key == "Pieces")
                {
                    readPieces(entry_arr);
                }

            }

            //Level filter
            for (int i = 0; i < 21; i++)
            {
                string text = "+" + i;
                int statValue = i;
                GenshinArtifactOCR.Levels.Add(text);
                GenshinArtifactOCR.Levels_trans.Add(Tuple.Create(text, statValue));
            }
        }
    }
}
