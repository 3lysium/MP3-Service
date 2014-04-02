﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.ServiceProcess;
using System.Text.RegularExpressions;
using System.Globalization;
using log4net;
using System.Diagnostics;

[assembly: log4net.Config.XmlConfigurator(Watch = true)]

// MP3 Service by Robin Miklinski

namespace mp3Service
{
    public partial class Service1 : ServiceBase
    {
        private System.Timers.Timer timer;

        public string BasePath = Config.BasePath;
        public string NetworkPath = Config.NetworkPath;
        public string LocalPath = Config.LocalPath;
        public Int32 PollInterval = Convert.ToInt32(Config.PollInterval);
        public string IncludeShare = Config.IncludeShare;
        public string desktopPath = Config.DesktopPath;
        public UInt32 bpm;
        public string fileversion = "v1.1.0.0";

        private string bugPath = "";

        private const string _copiedFileList = "copiedList.txt";

        private List<string> mCurrentFileList = new List<string>();

        //Regex strings
        string RegexPattern1 = @"--";
        string RegexPattern2 = @"[_]{1,}";
        string RegexPattern4 = @"^[a-cA-C0-9]{1,3}[\s-_\.]+";
        string RegexPattern5 = @"(\()*(_-\s)*(www\.*)*-*[a-zA-Z0-9\(\-]+\.[\[\(]*(net|com|org|ru)+[\)\]*[\d]*";
        string RegexPattern6 = @"(?!\)-)[-_\)]+[a-zA-Z0-9]{2,3}\.";
        string RegexPattern7 = @"[-_]*siberia";
        //string RegexPattern3 = @"(?!p3)((^[a-zA-Z0-9]{2})+[\s-_]+?)*";

        //Replace strings
        string RegexReplace1 = " - ";
        string RegexReplace2 = " ";
        string RegexReplace4 = "";
        string RegexReplace5 = "";
        string RegexReplace6 = ".";
        string RegexReplace7 = "";


        public Service1()
        {
                InitializeComponent();
                log4net.Config.XmlConfigurator.Configure();

                if (!System.Diagnostics.EventLog.SourceExists("MySource"))
                    System.Diagnostics.EventLog.CreateEventSource("MySource", "MyNewLog");
                eventLog1.Source = "MySource";
                eventLog1.Log = "MyNewLog";
            }

            protected override void OnStart(string[] args)
            {
                try
                {
                    RemoveDirectories(BasePath);
                    ProcessMP3();
                    timer = new System.Timers.Timer(PollInterval);
                    timer.AutoReset = true;
                    timer.Enabled = true;
                    timer.Elapsed += new System.Timers.ElapsedEventHandler(ProcessFolder);
                    timer.Start();

                    ILog Log = log4net.LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);
                    Log.Info("");
                    Log.Info(" -- MP3 Service started -- " + fileversion);
                    Log.Info("");
                    eventLog1.WriteEntry("MP3Service Started");
                }
                catch (Exception ex)
                {
                    eventLog1.WriteEntry(ex.Message);
                }
            }
            protected override void OnStop()
            {
                eventLog1.WriteEntry("MP3Service stopped");
            }

            //Handle null performers array
            public static string[] InitPerformers(string[] value)
            {
                if (value == null)
                {
                    return new[] { String.Empty };
                }
                return value;
            }
        private void ProcessFolder(object sender, System.Timers.ElapsedEventArgs e)
            {
                RemoveDirectories(BasePath);
                ProcessMP3();
            }

        private void ProcessMP3()
        {
            ILog Log = log4net.LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

            //var fileArr = Directory.GetFiles(BasePath, "*.mp3", SearchOption.AllDirectories);
            var fileArr = Directory.GetFiles(BasePath, "*.*", SearchOption.AllDirectories)
                                   .Where(s => s.EndsWith(".mp3") || s.EndsWith(".m4a")).ToArray();

            string copiedFileList = BasePath + "\\" + _copiedFileList;

            if (!File.Exists(copiedFileList))
            {
                File.Create(copiedFileList);
            }

            #region update current list

            try
            {
                foreach (var file in fileArr)
                {
                    if (!mCurrentFileList.Contains(file))
                        mCurrentFileList.Add(file);
                }
                var curEntries = File.ReadAllLines(copiedFileList);
                List<string> memCopyListtxt = new List<string>(curEntries);

                var fs = new FileStream(copiedFileList, FileMode.OpenOrCreate, FileAccess.Write);
                var mStreamWriter = new StreamWriter(fs);
                mStreamWriter.BaseStream.Seek(0, SeekOrigin.End);

                foreach (string file in mCurrentFileList)
                {
                    if (!file.Contains("INCOMPLETE~") && !memCopyListtxt.Contains(file))
                    {
                        memCopyListtxt.Add(file);
                        mStreamWriter.WriteLine(file);
                    }
                }
                mStreamWriter.Flush();
                mStreamWriter.Close();
            }
            catch (Exception ex)
            {
                eventLog1.WriteEntry(string.Format("GetFiles error: {0}", ex.Message));
            }

            #endregion

            foreach (var file in fileArr)
            {
                string fileName = Path.GetFileName(file);
                string tagArtist = "";
                string tagTitle = "";
                string tempRegFilename = fileName;
                string title = "";

                //Consolebpm config
                ProcessStartInfo info = new ProcessStartInfo("consolebpm.exe");
                info.UseShellExecute = false;
                info.RedirectStandardError = true;
                info.RedirectStandardInput = true;
                info.RedirectStandardOutput = true;
                info.CreateNoWindow = true;
                info.ErrorDialog = false;
                info.WindowStyle = ProcessWindowStyle.Hidden;

                Process process = Process.Start(info);
          
                tempRegFilename = regexFilename(tempRegFilename, ".mp3");

                if (!file.Contains("INCOMPLETE~"))
                {
                    Log.Info("");
                    Log.Info("---------------------------------------------------------------");
                    Log.Info("PROCESSING: " + fileName);

                    try
                    {
                        //Get BPM
                        string bpmVal;

                        process.StartInfo.Arguments = "\"" + file + "\"";
                        process.Start();
                        bpmVal = process.StandardOutput.ReadLine();
                        process.WaitForExit();
                        Log.Info("BPM Detected: " + bpmVal);
                    
                        //Apply to tag
                        TagLib.File mp3tag = TagLib.File.Create(file);

                        if (mp3tag.Tag.BeatsPerMinute.ToString().Length > 1)
                        {
                            if (mp3tag.Tag.BeatsPerMinute > 65 && mp3tag.Tag.BeatsPerMinute < 135)
                            {
                                bpm = mp3tag.Tag.BeatsPerMinute;
                                Log.Info("ID3 BPM: " + bpm);
                                Log.Info("Tag BPM OK");
                            }
                            else
                            {
                                Log.Warn("Tag BPM out of range");
                            }
                        }
                        else
                        {
                            //Cast to UInt and set tag
                            Log.Info("Tag BPM missing...");
                            double d = Convert.ToDouble(bpmVal);
                            int i = (int)Math.Round(d, 0);
                            UInt32 newBpm = Convert.ToUInt32(i);
                            mp3tag.Tag.BeatsPerMinute = newBpm;
                            Log.Info("Setting new BPM: " + "[" + mp3tag.Tag.BeatsPerMinute.ToString() + "]");
                            mp3tag.Save();
                        }

                        if (mp3tag.Tag.Title != null && mp3tag.Tag.Title.Length > 1)
                        {
                            title = mp3tag.Tag.Title;
                        } 
                        else
                        {
                            mp3tag.Tag.Title = String.Empty;
                        }

                        if (mp3tag.Tag.Performers[0].Length < 1 || mp3tag.Tag.Performers[0] == null)
                        {
                            mp3tag.Tag.Performers[0] = null;
                            mp3tag.Tag.Performers = new[] { String.Empty };
                            mp3tag.Save();
                        }

                        if (mp3tag.Tag.Performers[0].Length > 1)
                        {
                                string[] performers = mp3tag.Tag.Performers;
                                if (title.Length > 2 && performers[0].Length > 1)
                                {
                                    tagTitle = title;
                                    tagArtist = performers[0].ToString();
                                    Log.Info("ID3 Artist: " + "[" + tagArtist + "]");
                                    Log.Info("ID3 Title: " + "[" + tagTitle + "]");
                                    Log.Info("Tag data OK");
                                }
                        }
                                //Get artist from filename
                                if (mp3tag.Tag.Performers[0].Length < 1 || mp3tag.Tag.Performers == null)
                                {
                                    mp3tag.Tag.Performers = new[] { String.Empty };
                                    string prevArtist = String.Empty;

                                    if (tempRegFilename.Contains("-"))
                                    {
                                        Log.Info("Artist data missing...");
                                        string[] words = tempRegFilename.Split('-');
                                        {
                                            words[0] = words[0].Trim();
                                            string perf = words[0];
                                            mp3tag.Tag.Performers = new[] { perf };
                                            Log.Info("Artists changed from \'" + prevArtist + "\' to " + "'" + perf + "'" + "\r\n");
                                            mp3tag.Save();
                                        }
                                    }
                                    mp3tag.Save();
                                }

                                // Get title from filename
                                if (mp3tag.Tag.Title == null || title.Length < 2)
                                {
                                    mp3tag.Tag.Title = "";

                                    if (tempRegFilename.Contains("-"))
                                    {
                                        Log.Info("Title data missing...");
                                        string[] words = tempRegFilename.Split('-');
                                        {
                                            string prevTitle = mp3tag.Tag.Title;
                                            mp3tag.Tag.Title = words[1].Trim();
                                            Log.Info("Title changed from \'" + prevTitle + "\' to " + "'" + words[1].Trim() +
                                                     "'" + "\r\n");
                                        }
                                    }
                                    mp3tag.Save();
                                }
                                mp3tag.Dispose();
                        }

                    catch (Exception ex)
                    {
                        Log.Error("TAG EXCEPTION: " + ex.Message + "Data: " + "'" + ex.Data + "'" + " for " + fileName + "\r\n" + ex.HelpLink);
                    }

                    try
                    {
                        if (!file.Contains("INCOMPLETE~"))
                        {
                            string tempExt = "";

                            if (file.Contains(".mp3"))
                            {
                                tempExt = ".mp3";
                                fileName = regexFilename(fileName, tempExt);
                            }
                            if (file.Contains(".m4a"))
                            {
                                tempExt = ".m4a";
                                fileName = regexFilename(fileName, tempExt);
                            }

                            if (tagArtist.Length > 2 && tagTitle.Length > 2)
                            {
                                string tagFull = tagArtist + " - " + tagTitle;
                                tagFull = regexFilename(tagFull, tempExt);
                                fileName = tagFull;
                                Log.Info("NEW FILENAME: " + tagFull);
                            }
                            
                            //Publish tracks to network
                            string networkFullPath = Path.Combine(NetworkPath, fileName);
                            string localFullPath = Path.Combine(LocalPath, fileName);
                            string desktopFullPath = Path.Combine(desktopPath, fileName);

                            FileInfo fileInfo = new FileInfo(file);
                            if (fileInfo.Length < 40000000)
                            {
                                if (!File.Exists(localFullPath))
                                {
                                    File.Copy(file, localFullPath, true);
                                }

                                if (IncludeShare.Equals("true", StringComparison.InvariantCultureIgnoreCase))
                                {
                                    copyShare(file, networkFullPath);
                                }
                            }
                            //Publish sets to desktop
                            if (fileInfo.Length > 40000000)
                            {
                                bugPath = desktopFullPath;
                                Log.Info("Publishing set: " + file + " desktopFullPath: " + desktopFullPath);
                                File.Copy(file, desktopFullPath, true);
                            }
                            if (File.Exists(desktopFullPath))
                            {
                                File.Delete(file);
                            }
                            else if (File.Exists(localFullPath))
                            {
                                File.Delete(file);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Error(string.Format("Replace copy error: {0}",
                                                ex.Message + "\r\n" + "DesktopPath: " + bugPath + "\r\n"));
                    }
                }
            }
        }

        public void copyShare(string file, string networkFullPath)
        {
            ILog Log = log4net.LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

            DirectoryInfo di = new DirectoryInfo(NetworkPath);
            bool networkExists = di.Exists;

            FileInfo fi = new FileInfo(networkFullPath);
            bool fileExistsOnShare = fi.Exists;

            if (networkExists)
            {
                if (!fileExistsOnShare)
                {
                    File.Copy(file, networkFullPath);
                    Log.Info("NETWORK COPY: " + file);
                }
            }
            else
            {
                Log.Error("Network copy error: " + file + " NetworkPath: " + networkFullPath);
            }
        }

        //Apply file extention
        private string regexFilename(string fileName, string extention)
            {
                fileName = Regex.Replace(fileName, RegexPattern1, RegexReplace1);
                fileName = Regex.Replace(fileName, RegexPattern2, RegexReplace2);
                fileName = Regex.Replace(fileName, RegexPattern4, RegexReplace4);
                fileName = Regex.Replace(fileName, RegexPattern5, RegexReplace5);
                fileName = Regex.Replace(fileName, RegexPattern6, RegexReplace6);
                fileName = Regex.Replace(fileName, RegexPattern7, RegexReplace7);

                fileName = CultureInfo.CurrentCulture.TextInfo.ToTitleCase(Path.GetFileNameWithoutExtension(fileName));
                fileName = fileName + extention;

                return fileName;
            }

            //Remove redundant subdirs
            private void RemoveDirectories(string test1)
            {
                ILog Log = log4net.LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);
          
                    try
                    {                 
                        foreach (var d in Directory.GetDirectories(test1))
                        {
                            var files = Directory.GetFiles(d)
                                       .Where(name => (!name.StartsWith("INCOMPLETE~") && !name.EndsWith(".mp3") && !name.EndsWith(".m4a") && !name.EndsWith(".txt")));
                        
                            foreach (var file in files)
                            {
                                File.Delete(file);
                            }
                            if (Directory.GetFiles(d).Length == 0)
                            {
                                Log.Info("REMOVING SUBDIR: " + d + "\r\n");
                                Directory.Delete(d);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Error("DIR REMOVE ERROR: " + ex.Message);
                    }
                }
        }
}




        // Read file from the last UpdateCurrentList 
        /*private List<string> ReadCurrentList(string currentListPath)
        {
            currentListPath = BasePath + "\\currentList.txt";
            string[] filesRead = File.ReadAllLines(currentListPath);
            var currentList = new List<string>(filesRead);

            return currentList;
        }*/



/* Network upload
 */ 



/*DateTime lastChecked = DateTime.Now;
    TimeSpan ts = DateTime.Now.Subtract(lastChecked);
    TimeSpan maxWaitTime = TimeSpan.FromMinutes(2);

    if (maxWaitTime.Subtract(ts).CompareTo(TimeSpan.Zero) > -1)
        timer.Interval = maxWaitTime.Subtract(ts).TotalMilliseconds;
    else
        timer.Interval = 1;
     */
//timer.Start();
    


        //mStreamWriter.WriteLine("mp3Service: Started at " + DateTime.Now.ToShortDateString() + " " + DateTime.Now.ToShortTimeString() + "\n")