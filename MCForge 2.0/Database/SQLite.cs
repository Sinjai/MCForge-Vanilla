﻿/*
Copyright 2012 MCForge
Dual-licensed under the Educational Community License, Version 2.0 and
the GNU General Public License, Version 3 (the "Licenses"); you may
not use this file except in compliance with the Licenses. You may
obtain a copy of the Licenses at
http://www.opensource.org/licenses/ecl2.php
http://www.gnu.org/licenses/gpl-3.0.html
Unless required by applicable law or agreed to in writing,
software distributed under the Licenses are distributed on an "AS IS"
BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express
or implied. See the Licenses for the specific language governing
permissions and limitations under the Licenses.
*/
#define NOT_USING_SQLite_Interop_dll
using System;
using System.Windows.Forms;
using System.Data;
using System.Data.SQLite;
using System.Diagnostics;
using System.Collections.Specialized;
using System.IO;
using System.Timers;
using MCForge.Utils.Settings;
using MCForge.Utils;
using Timer = System.Timers.Timer;

namespace MCForge.SQL {
    /// <summary>
    /// Description of SQLite.
    /// </summary>
    internal class SQLite : ISQL {
        protected string connString;
        Timer backup;
        protected SQLiteConnection conn;
        protected bool _closed = true;
        public override void onLoad() {
            if (ServerSettings.GetSettingBoolean("SQLite-InMemory")) {
                Logger.Log("Using memory database");
                string dbpath = Application.StartupPath + "/" + ServerSettings.GetSetting("SQLite-Filepath");
                connString = "Data Source = :memory:; Version = 3; Pooling =" + ServerSettings.GetSetting("SQLite-Pooling") + "; Max Pool Size =1000;";
                Open();
                if (File.Exists(dbpath)) {
                    SQLiteConnection loader = new SQLiteConnection("Data Source =\"" + dbpath + "\"; Version =3; Pooling =" + ServerSettings.GetSetting("SQLite-Pooling") + "; Max Pool Size =1000;");
                    loader.Open();
                    SaveTo(loader, conn);
                    loader.Close();
                }
                backup = new Timer();
                
                try {
                    int interval = int.Parse(ServerSettings.GetSetting("BackupInterval"));
                    backup.Interval = interval * 1000;
                }
                catch {
                    backup.Interval = 300 * 1000;
                }
                backup.Elapsed += new ElapsedEventHandler(backup_Tick);
                backup.Start();
                backup.Enabled = true;
            }
            else {
                Logger.Log("Using file database");
                connString = "Data Source =\"" + Application.StartupPath + "/" + ServerSettings.GetSetting("SQLite-Filepath") + "\"; Version =3; Pooling =" + ServerSettings.GetSetting("SQLite-Pooling") + "; Max Pool Size =1000;";
                Open();
            }
            string[] commands = new string[3];
            commands[0] = "CREATE TABLE if not exists _players (UID INTEGER not null PRIMARY KEY AUTOINCREMENT, Name VARCHAR(20), IP VARCHAR(20), firstlogin DATETIME, lastlogin DATETIME, money MEDIUMINT, totallogin MEDIUMINT, totalblocks MEDIUMINT, color VARCHAR(5));";
            commands[1] = "CREATE TABLE if not exists extra (key VARCHAR(1000), value VARCHAR(1000), UID INTEGER);";
            commands[2] = "CREATE TABLE if not exists Blocks (UID INTEGER, X MEDIUMINT, Y MEDIUMINT, Z MEDIUMINT, Level VARCHAR(100),  Deleted VARCHAR(30), Block TEXT, Date DATETIME, Was TEXT);";
            executeQuery(commands);
        }

        void backup_Tick(object sender, EventArgs e) {
            backup.Stop();
            Logger.Log("Database backup...", LogType.Debug);
            Save();
            backup.Start();
        }
        private void SaveTo(SQLiteConnection source, SQLiteConnection destination) {

#if USING_SQLite_Interop_dll
            bool saving = source.DataSource == "";
            SQLiteCommand cmdSource = new SQLiteCommand(source);
            cmdSource.CommandText = "Select * FROM sqlite_master WHERE type=='table'";
            SQLiteDataReader masterReader = cmdSource.ExecuteReader();
            while (masterReader.Read()) {
                //sqlite_sequence,
                NameValueCollection nvc = masterReader.GetValues();
                if (nvc["name"] != "sqlite_sequence") {
                    Stopwatch s = new Stopwatch();
                    s.Start();
                    SQLiteCommand cmdDest = new SQLiteCommand(destination);
                    cmdDest.CommandText = "DROP TABLE IF EXISTS " + nvc["name"];
                    cmdDest.ExecuteNonQuery();
                    cmdDest.CommandText = nvc["sql"];
                    cmdDest.ExecuteNonQuery();
                    SQLiteCommand cmdSrc = new SQLiteCommand(source);
                    cmdSrc.CommandText = "SELECT * FROM " + nvc["name"] + " ORDER BY _ROWID_";
                    SQLiteDataReader dataReader = cmdSrc.ExecuteReader();
                    while (dataReader.Read()) {
                        NameValueCollection nvcRow = dataReader.GetValues();
                        string insert = "INSERT INTO " + nvc["name"] + " VALUES (";
                        bool valid = false;
                        for (int i = 0; i < nvcRow.Keys.Count; i++) {
                            insert += "\"" + nvcRow[i] + "\"";
                            if (i + 1 != nvcRow.Keys.Count)
                                insert += ", ";
                            if (nvcRow[i] != "") valid = true;
                        }
                        if (valid) {
                            cmdDest.CommandText = insert + ")";
                            cmdDest.ExecuteNonQuery();
                        }
                    }
                    s.Stop();
                    Logger.Log("Table " + nvc["name"] + ((saving)?" saved (":" loaded (") + s.Elapsed + ")");
                }
            }
#else
            Logger.Log("Can't save DB from memory to filesystem");
#endif
        }
        public void Save() {
            if (ServerSettings.GetSettingBoolean("SQLite-InMemory")) {
                string dbpath = ServerSettings.GetSetting("SQLite-Filepath");
                SQLiteConnection saver = new SQLiteConnection("Data Source =\"" + dbpath + "\"; Version =3; Pooling =" + ServerSettings.GetSetting("SQLite-Pooling") + "; Max Pool Size =1000;");
                saver.Open();
                SaveTo(conn, saver);
                saver.Close();
                Logger.Log("Database saved");
            }
        }

        public override void executeQuery(string[] queryString) {
            try {
                for (int i = 0; i < queryString.Length; i++) {
                    using (SQLiteCommand cmd = new SQLiteCommand(queryString[i], conn)) {
                        cmd.ExecuteNonQuery();
                    }
                }
            }
            catch (Exception e) {
                Logger.LogError(e);
                Logger.Log("Error in SQLite..", LogType.Critical);
                Logger.Log("" + e);
            }
        }
        public override void executeQuery(string queryString) {
            try {
                using (SQLiteCommand cmd = new SQLiteCommand(queryString, conn)) {
                    cmd.ExecuteNonQuery();
                }
            }
            catch (Exception e) {
                Logger.LogError(e);
                Logger.Log("Error in SQLite..", LogType.Critical);
                Logger.Log("" + e);
            }
        }

        public override DataTable fillData(string queryString) {
            DataTable db = new DataTable("toReturn");
            try {
                using (SQLiteDataAdapter da = new SQLiteDataAdapter(queryString, conn)) {
                    da.Fill(db);
                }
                return db;
            }
            catch (Exception e) {
                Logger.LogError(e);
                Logger.Log("Error in SQLite..", LogType.Critical);
                Logger.Log("" + e);
                return db;
            }
        }

        public void Open() {
            if (_closed) {
                try {
                    conn = new SQLiteConnection(connString);
                    conn.Open();
                    _closed = false;
                }
                catch (Exception e) { Logger.Log(e.Message); Logger.Log(e.StackTrace); }
            }
        }

        public void Close(bool dispose) {
            if (!_closed) {
                if (backup != null) {
                    backup.Dispose();
                    backup = null;
                }
                conn.Close();
                if (dispose)
                    conn.Dispose();
                _closed = true;
            }
        }

        public override void Dispose() {
            if (!_disposed) {
                if (backup != null) {
                    backup.Dispose();
                    backup = null;
                }
                Close(true);
                base.Dispose();
            }
        }

    }
}
