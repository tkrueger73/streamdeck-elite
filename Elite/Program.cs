﻿using BarRaider.SdTools;
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Xml.Serialization;
using EliteJournalReader;
using EliteJournalReader.Events;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;


namespace Elite
{

    public class KeyBindingFileEvent : EventArgs
    {

    }

    public class KeyBindingWatcher : FileSystemWatcher
    {
        public event EventHandler KeyBindingUpdated;

        protected KeyBindingWatcher()
        {

        }

        public KeyBindingWatcher(string path, string fileName)
        {
            Filter = fileName;
            NotifyFilter = NotifyFilters.CreationTime | NotifyFilters.LastWrite;
            Path = path;
        }

        public virtual void StartWatching()
        {
            if (EnableRaisingEvents)
            {
                return;
            }

            Changed -= UpdateStatus;
            Changed += UpdateStatus;

            EnableRaisingEvents = true;
        }

        public virtual void StopWatching()
        {
            try
            {
                if (EnableRaisingEvents)
                {
                    Changed -= UpdateStatus;

                    EnableRaisingEvents = false;
                }
            }
            catch (Exception e)
            {
                Trace.TraceError($"Error while stopping Status watcher: {e.Message}");
                Trace.TraceInformation(e.StackTrace);
            }
        }

        protected void UpdateStatus(object sender, FileSystemEventArgs e)
        {
            Thread.Sleep(50);

            KeyBindingUpdated?.Invoke(this, EventArgs.Empty);
        }
       

    }
    

    class Program
    {
        public static KeyBindingWatcher KeyBindingWatcher1;
        public static KeyBindingWatcher KeyBindingWatcher2;
        public static StatusWatcher StatusWatcher;
        public static CargoWatcher CargoWatcher;
        public static JournalWatcher JournalWatcher;

        public static UserBindings Bindings;

        private class UnsafeNativeMethods
        {
            [DllImport("Shell32.dll")]
            public static extern int SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)]Guid rfid, uint dwFlags, IntPtr hToken, out IntPtr ppszPath);
        }

        /// <summary>
        /// The standard Directory of the Player Journal files (C:\Users\%username%\Saved Games\Frontier Developments\Elite Dangerous).
        /// </summary>
        public static DirectoryInfo StandardDirectory
        {
            get
            {
                int result = UnsafeNativeMethods.SHGetKnownFolderPath(new Guid("4C5C32FF-BB9D-43B0-B5B4-2D72E54EAAA4"), 0, new IntPtr(0), out IntPtr path);
                if (result >= 0)
                {
                    try { return new DirectoryInfo(Marshal.PtrToStringUni(path) + @"\Frontier Developments\Elite Dangerous"); }
                    catch { return new DirectoryInfo(Directory.GetCurrentDirectory()); }
                }
                else
                {
                    return new DirectoryInfo(Directory.GetCurrentDirectory());
                }
            }
        }

        public static void HandleKeyBindingEvents(object sender, object evt)
        {
            Logger.Instance.LogMessage(TracingLevel.INFO, $"Reloading Key Bindings");

            GetKeyBindings();
        }

        private static bool IsFileLocked(FileInfo file)
        {
            FileStream stream = null;

            try
            {
                stream = file.Open(FileMode.Open, FileAccess.Read, FileShare.None);
            }
            catch (IOException)
            {
                //the file is unavailable because it is:
                //still being written to
                //or being processed by another thread
                //or does not exist (has already been processed)
                return true;
            }
            finally
            {
                stream?.Close();
            }

            //file is not locked
            return false;
        }


        // copied from https://github.com/MagicMau/EliteJournalReader

        private static FileInfo FileInfo(string cargoPath)
        {
            try
            {
                var info = new FileInfo(cargoPath);
                if (info.Exists)
                {
                    // This info can be cached so force a refresh
                    info.Refresh();
                }
                return info;
            }
            catch { return null; }
        }


        // copied from https://github.com/MagicMau/EliteJournalReader
        public static string ReadStartPreset(string startPresetPath)
        {
            try
            {
                Thread.Sleep(100);

                FileInfo fileInfo = null;
                try
                {
                    fileInfo = FileInfo(startPresetPath);
                }
                catch (NotSupportedException)
                {
                    // do nothing
                }

                if (fileInfo != null)
                {
                    var maxTries = 6;
                    while (IsFileLocked(fileInfo))
                    {
                        Thread.Sleep(100);
                        maxTries--;
                        if (maxTries == 0)
                        {
                            return null;
                        }
                    }

                    using (var fs = new FileStream(fileInfo.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    using (var reader = new StreamReader(fs, Encoding.UTF8))
                    {
                        fs.Seek(0, SeekOrigin.Begin);
                        var bindsName = reader.ReadToEnd();

                        if (string.IsNullOrEmpty(bindsName))
                        {
                            return null;
                        }
                        return bindsName;

                    }
                }
            }
            catch (Exception e)
            {
                Logger.Instance.LogMessage(TracingLevel.FATAL, e.ToString());
            }

            return null;
        }

        public static void GetKeyBindings()
        {
            if (KeyBindingWatcher1 != null)
            {
                KeyBindingWatcher1.StopWatching();
                KeyBindingWatcher1.Dispose();
                KeyBindingWatcher1 = null;
            }

            if (KeyBindingWatcher2 != null)
            {
                KeyBindingWatcher2.StopWatching();
                KeyBindingWatcher2.Dispose();
                KeyBindingWatcher2 = null;
            }

            var bindingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Frontier Developments\Elite Dangerous\Options\Bindings");

            if (!Directory.Exists(bindingsPath))
            {
                Logger.Instance.LogMessage(TracingLevel.FATAL, $"Directory doesn't exist {bindingsPath}");
            }


            var startPresetPath = Path.Combine(bindingsPath, "StartPreset.start");

            //Logger.Instance.LogMessage(TracingLevel.INFO, "bindings path " + bindingsPath);

            var bindsName = ReadStartPreset(startPresetPath);

            //var bindsName = File.ReadAllText(startPresetPath);

            var keyBindingPath = Path.GetDirectoryName(startPresetPath);
            Logger.Instance.LogMessage(TracingLevel.INFO, "monitoring key binding path #1 " + keyBindingPath);
            var keyBindingFileName = Path.GetFileName(startPresetPath);
            Logger.Instance.LogMessage(TracingLevel.INFO, "monitoring key binding file name #1 " + keyBindingFileName);
            KeyBindingWatcher1 = new KeyBindingWatcher(keyBindingPath, keyBindingFileName);
            KeyBindingWatcher1.KeyBindingUpdated += HandleKeyBindingEvents;
            KeyBindingWatcher1.StartWatching();

            var fileName = Path.Combine(bindingsPath, bindsName + ".3.0.binds");

            if (!File.Exists(fileName))
            {
                Logger.Instance.LogMessage(TracingLevel.ERROR, "file not found " + fileName);

                fileName = fileName.Replace(".3.0.binds", ".binds");

                if (!File.Exists(fileName))
                {
                    Logger.Instance.LogMessage(TracingLevel.ERROR, "file not found " + fileName);
                }
            }

            // steam
            if (!File.Exists(fileName))
            {
                bindingsPath = SteamPath.FindSteamEliteDirectory();

                if (!string.IsNullOrEmpty(bindingsPath))
                {
                    fileName = Path.Combine(bindingsPath, bindsName + ".3.0.binds");

                    if (!File.Exists(fileName))
                    {
                        Logger.Instance.LogMessage(TracingLevel.ERROR, "steam file not found " + fileName);

                        fileName = fileName.Replace(".3.0.binds", ".binds");

                        if (!File.Exists(fileName))
                        {
                            Logger.Instance.LogMessage(TracingLevel.ERROR, "steam file not found " + fileName);
                        }
                    }
                }
            }

            // epic
            if (!File.Exists(fileName))
            {
                bindingsPath = EpicPath.FindEpicEliteDirectory();

                if (!string.IsNullOrEmpty(bindingsPath))
                {
                    fileName = Path.Combine(bindingsPath, bindsName + ".3.0.binds");

                    if (!File.Exists(fileName))
                    {
                        Logger.Instance.LogMessage(TracingLevel.ERROR, "epic file not found " + fileName);

                        fileName = fileName.Replace(".3.0.binds", ".binds");

                        if (!File.Exists(fileName))
                        {
                            Logger.Instance.LogMessage(TracingLevel.ERROR, "epic file not found " + fileName);
                        }
                    }
                }
            }

            if (File.Exists(fileName))
            {
                var serializer = new XmlSerializer(typeof(UserBindings));

                //Logger.Instance.LogMessage(TracingLevel.INFO, "using " + fileName);

                var reader = new StreamReader(fileName);
                Bindings = (UserBindings) serializer.Deserialize(reader);
                reader.Close();


                keyBindingPath = Path.GetDirectoryName(fileName);
                Logger.Instance.LogMessage(TracingLevel.INFO, "monitoring key binding path #2 " + keyBindingPath);
                keyBindingFileName = Path.GetFileName(fileName);
                Logger.Instance.LogMessage(TracingLevel.INFO,
                    "monitoring key binding file name #2 " + keyBindingFileName);
                KeyBindingWatcher2 = new KeyBindingWatcher(keyBindingPath, keyBindingFileName);
                KeyBindingWatcher2.KeyBindingUpdated += HandleKeyBindingEvents;
                KeyBindingWatcher2.StartWatching();
            }
        }

        static void Main(string[] args)
        {
            // Uncomment this line of code to allow for debugging
            //while (!System.Diagnostics.Debugger.IsAttached) { System.Threading.Thread.Sleep(100); }

            Logger.Instance.LogMessage(TracingLevel.INFO, "Init Elite Api");

            try
            {
                GetKeyBindings();


                var journalPath = StandardDirectory.FullName;

                Logger.Instance.LogMessage(TracingLevel.INFO, "journal path " + journalPath);

                if (!Directory.Exists(journalPath))
                {
                    Logger.Instance.LogMessage(TracingLevel.FATAL, $"Directory doesn't exist {journalPath}");
                }

                StatusWatcher = new StatusWatcher(journalPath);

                StatusWatcher.StatusUpdated += EliteData.HandleStatusEvents;

                StatusWatcher.StartWatching();

                JournalWatcher = new JournalWatcher(journalPath);

                JournalWatcher.AllEventHandler += EliteData.HandleEliteEvents;

                JournalWatcher.StartWatching().Wait();

                CargoWatcher = new CargoWatcher(journalPath);

                CargoWatcher.CargoUpdated += EliteData.HandleCargoEvents;

                CargoWatcher.StartWatching();

            }
            catch (Exception ex)
            {
                Logger.Instance.LogMessage(TracingLevel.FATAL, $"Elite Api: {ex}");
            }


            //EliteAPI.Events.AllEvent += (sender, e) => Console.Beep();

            Profile.ReadProfiles();


            SDWrapper.Run(args);
        }


    }
}
