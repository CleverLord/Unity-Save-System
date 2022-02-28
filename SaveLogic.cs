using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;
using UnityEngine;

namespace SaveSystem
{
    public class SaveLogic
    {
        private static readonly string DataPath = Application.persistentDataPath + "/Saves/";
        private static readonly string TempPath = Application.temporaryCachePath + "/SaveTemp/";
        private static int _userID = -1, _snapshotID = -1;
        //private static int __userID => UserData == null ? -1 : UserData.id;

        public static SaveData GlobalData, UserData, SnapshotData;

        static SaveLogic() {
            if (!Directory.Exists(DataPath)) {
                Directory.CreateDirectory(DataPath);
            }

            if (!Directory.Exists(TempPath)) {
                Directory.CreateDirectory((TempPath));
            }

            Debug.Log($"SaveLogic \nDataPath - {DataPath}");
            Debug.Log($"SaveLogic \nTempPath - {TempPath}");
        }


        /// <summary>
        /// Finds all created users and downloads only their stored info. Does not load user.
        /// </summary>
        /// <returns>List of ProfileInfo structs containing infos on name, time of creation, ids and others</returns>
        public static List<ProfileInfo> GetUsersInfo() {
            var returnList = new List<ProfileInfo>();
            var files = Directory.GetFiles(DataPath);
            foreach (var file in files) {
                if (file.Contains("Global"))
                    continue;
                using (var archive = ZipFile.OpenRead(file)) {
                    string objectName = Path.GetFileName(file); //file.Split('/').Last().Replace(".sav", "");
                    objectName = objectName.Substring(objectName.IndexOf('.'));
                    if (archive == null)
                        throw new NullReferenceException($"Archive at path {file} does not exist!");
                    var entry = archive.GetEntry($"{objectName}.dat");
                    if (entry == null)
                        throw new NullReferenceException(
                            $"Entry named {objectName}.dat does not exist in the current context!");

                    SaveData e = BinaryBaseFormatter.Formatter.Deserialize(entry.Open()) as SaveData;

                    returnList.Add(e.FirstOrDefault(x => x.key == "Profile Info").data as ProfileInfo);
                }
            }
            return returnList;
        }

        /// <summary>
        /// Finds all snapshot for the selected user and downloads their stored info. Does not load snapshots.
        /// </summary>
        /// <param name="userID">User ID to download snapshot infos from</param>
        /// <returns>List of SnapshotInfo structs containing infos on name, icon, time of creation, ids, and others</returns>
        public static List<SnapshotInfo> GetSnapshotsInfo(int userID) {
            var returnList = new List<SnapshotInfo>();
            try {
                //Get base data file names
                var saveName = $"profile{userID}";

                //Process base data file name into file path
                var zipSaveFile = DataPath + $"{saveName}.sav";

                //Open user profile
                using (var archive = ZipFile.OpenRead(zipSaveFile)) {
                    //Get snapshot save
                    var entries = archive.Entries;
                    foreach (var entry in entries) {
                        if (!entry.Name.Contains(".snapshot"))
                            continue;

                        //Create snapshotInfo object
                        var sInfo = new SnapshotInfo();

                        //Extract snapshot save to temporary directory
                        Directory.CreateDirectory(TempPath + "Extracted");
                        entry.ExtractToFile(TempPath + $"Extracted/T.snapshot");

                        //Open snapshot save
                        using (var snapshotArchive = ZipFile.OpenRead(TempPath + $"Extracted/T.snapshot")) {
                            //Get data from snapshot
                            var dataEntry = snapshotArchive.GetEntry(entry.Name.Replace(".snapshot", ".dat"));
                            using (var stream = dataEntry.Open()) {
                                var e = (BinaryBaseFormatter.Formatter.Deserialize(stream) as SaveData);
                                sInfo = (SnapshotInfo)e.Find(x => x.key == "Snapshot Info").data;
                            }

                            //Get texture from snapshot
                            byte[] data;
                            var textureEntry = snapshotArchive.GetEntry($"{entry.Name.Replace(".snapshot", "")}.ico");
                            using (Stream s = textureEntry.Open()) {
                                using (MemoryStream memoryStream = new MemoryStream()) {
                                    s.CopyTo(memoryStream);
                                    data = memoryStream.ToArray();
                                }
                            }
                            Texture2D ico = new Texture2D(2, 2);
                            ImageConversion.LoadImage(ico, data);
                            sInfo.preview = ico;
                        }

                        //Remove the temporary folder
                        foreach (string file in Directory.EnumerateFiles(TempPath + "Extracted"))
                            File.Delete(file);
                        foreach (string file in Directory.GetDirectories(TempPath + "Extracted"))
                            Directory.Delete(file);
                        Directory.Delete(TempPath + "Extracted");

                        returnList.Add(sInfo);
                    }
                }
            } catch {
                Debug.LogError("Failed to gather snapshot infos!");
            }
            return returnList;
        }

        /// <summary>
        /// Loads global data and stores it in GlobalData, if this was loaded, it will just return it's content
        /// </summary>
        /// <returns>SaveData with global content</returns>
        public static SaveData GetGlobalData() {
            if (GlobalData == null) {
                LoadGlobalData();
            }
            if(GlobalData == null) {
                CreateNewGlobalSaveData();
            }
            return GlobalData;
        }

        /// <summary>
        /// Loads user with selected ID into UserData, if this user was loaded, will just return it's content
        /// </summary>
        /// <param name="userID">User ID to load</param>
        /// <returns>SaveData with user content</returns>
        public static SaveData GetUserData(int userID) {
            if (UserData == null || userID != _userID) {
                _userID = userID;
                LoadUserData();
            }
            if(UserData == null) {
                CreateNewUserData();
            }
            return UserData;
        }

        /// <summary>
        /// Loads a snapshot with selected parameters into SnapshotData, if this snapshot was loaded, will just return it's content
        /// </summary>
        /// <param name="userID">User ID to load snapshot from</param>
        /// <param name="snapshotID">Snapshot ID to load data from</param>
        /// <returns>SaveData with snapshot content</returns>
        public static SaveData GetSnapshotSaveData(int userID, int snapshotID) {
            if (SnapshotData == null || userID != _userID || snapshotID != _snapshotID) {
                _snapshotID = snapshotID;
                _userID = userID;
                LoadSnapshotData();
            }
            if(SnapshotData == null) {
                CreateNewSnapshotData();
            }

            return SnapshotData;
        }

        /// <summary>
        /// Loads latest snapshot from selected user into SnapshotData, if this snapshot was loaded, it will just return it's content
        /// </summary>
        /// <param name="userID">User ID to load snapshot from</param>
        /// <returns>SaveData with snapshot content</returns>
        public static SaveData UseLatestSnapshotSaveData(int userID) {
            return GetSnapshotSaveData(userID, GetLatestSnapshotIndex(userID));
        }

        /// <summary>
        /// Creates new GlobalData. It does not delete previous one until saved
        /// </summary>
        /// <returns>Empty SaveData with global content</returns>
        public static SaveData CreateNewGlobalSaveData() {
            GlobalData = new SaveData();
            return GlobalData;
        }

        /// <summary>
        /// Creates new UserData. It does not delete previous one until saved
        /// </summary>
        /// <param name="userID">User ID to create, if -1, find new ID</param>
        /// <returns>Empty SaveData with user content</returns>
        public static SaveData CreateNewUserData(int userID = -1) {
            if(userID!=-1)
                _userID = userID;
            UserData = new SaveData();
            return UserData;
        }

        /// <summary>
        /// Creates new GlobalData. It does not delete previous one until saved
        /// </summary>
        /// <param name="userID">User ID to create snapshot for</param>
        /// <param name="snapshotID">Snapshot ID to create, if -1, find new ID</param>
        /// <returns>Empty SaveData with global content</returns>
        public static SaveData CreateNewSnapshotData(int userID=-1, int snapshotID = -1) {
            if (userID != -1)
                _userID = userID;
            _snapshotID = snapshotID == -1 ? GetNextSnapshotIndex() : snapshotID;
            SnapshotData = new SaveData();
            return SnapshotData;
        }
        public static int GetNextSnapshotIndex() {
            return GetLatestSnapshotIndex(_userID) + 1;
        }
        /// <summary>
        /// Save global data.
        /// </summary>
        public static void SaveGlobalData() {
            var saveFile = TempPath + "Global.dat";

            using (var f = File.Create(saveFile)) {
                BinaryBaseFormatter.Formatter.Serialize(f, GlobalData);
            }
            var zipSaveFile = DataPath + "Global.sav";
            if (File.Exists(zipSaveFile))
                File.Delete(zipSaveFile);

            ZipFile.CreateFromDirectory(TempPath, zipSaveFile, System.IO.Compression.CompressionLevel.Fastest, false);
            File.Delete(saveFile);
        }

        /// <summary>
        /// Saves user data.
        /// </summary>
        /// <param name="overrideUser">If provided save to specified user</param>
        public static void SaveUserData(int overrideUser = -1) {
            var index = overrideUser == -1 ? _userID : overrideUser;

            //Get base data file name
            var objectName = $"profile{index}";

            //Process base data file name into temporary file path
            var saveFile = TempPath + $"{objectName}.dat";

            //Serialize data into a file
            using (var f = File.Create(saveFile))
                BinaryBaseFormatter.Formatter.Serialize(f, UserData);

            //Process base data file name into zip file path
            var zipSaveFile = DataPath + $"{objectName}.sav";

            if (File.Exists(zipSaveFile)) //If zip file exists, update it
            {
                //Extract the file to a directory
                Directory.CreateDirectory(TempPath + "Extracted");
                using (var a = ZipFile.OpenRead(zipSaveFile))
                    a.ExtractToDirectory(TempPath + "Extracted");

                //Remove user entry in the file and replace it with the newly created one
                File.Delete(TempPath + $"Extracted/{objectName}.dat");
                File.Move(saveFile, TempPath + $"Extracted/{objectName}.dat");

                //Remove the save file
                File.Delete(zipSaveFile);

                //Create new one from this temporary folder
                ZipFile.CreateFromDirectory(TempPath + "Extracted", zipSaveFile, System.IO.Compression.CompressionLevel.Fastest, false);

                //Remove the temporary folder
                foreach (string file in Directory.EnumerateFiles(TempPath + "Extracted"))
                    File.Delete(file);
                foreach (string file in Directory.GetDirectories(TempPath + "Extracted"))
                    Directory.Delete(file);
                Directory.Delete(TempPath + "Extracted");
            } else //If it doesn't, create a new one
              {
                ZipFile.CreateFromDirectory(TempPath, zipSaveFile, System.IO.Compression.CompressionLevel.Fastest, false);
            }

            //Delete temporary data path
            File.Delete(saveFile);
        }

        /// <summary>
        /// Saves snapshot data to a user.
        /// </summary>
        /// <param name="overrideUser">If provided save to specified user</param>
        /// <param name="overrideSnapshot">If provided save to specified snapshot, if code -2, save to new snapshot</param>
        /// <param name="saveName">If provided, will save it as a save display name</param>
        public static void SaveSnapshotData(int overrideUser = -1, int overrideSnapshot = -1, string saveName = "Auto-Snapshot") {
            //Input snapshot info into entries
            var userIndex = overrideUser == -1 ? _userID : overrideUser;
            var snapshotIndex = overrideSnapshot == -1 ? _snapshotID : overrideSnapshot;
            if (overrideSnapshot == -2) {
                snapshotIndex = GetLatestSnapshotIndex(userIndex) + 1;
            }

            //AddEntry -> AddOrReplace
            SnapshotData.AddEntry("Snapshot Info", new SnapshotInfo() {
                name = saveName, //TODO: input real name
                time = DateTime.Now,
                userID = userIndex,
                snapshotID = snapshotIndex,
                fullPath = "",
            });

            //Get user profile path
            var userSaveFile = DataPath + $"profile{userIndex}.sav";

            if (File.Exists(userSaveFile)) {
                //Get base data file name
                var objectName = $"snapshot{snapshotIndex}";

                //Process base data file name into file paths
                var saveFile = TempPath + $"snapshot/{objectName}.dat";
                var saveFileIco = TempPath + $"snapshot/{objectName}.ico";

                //Create temporary data directory
                if (!Directory.Exists(TempPath + "snapshot/"))
                    Directory.CreateDirectory(TempPath + "snapshot/");

                //Serialize data into a file
                using (var f = File.Create(saveFile))
                    BinaryBaseFormatter.Formatter.Serialize(f, SnapshotData);

                //Create a screen shot data file
                {
                    int w = 800, h = 600;
                    var rt = new RenderTexture(w, h, 24);
                    var cam = Camera.main;
                    if (cam != null) {
                        cam.targetTexture = rt;
                        var screenShot = new Texture2D(w, h, TextureFormat.RGB24, false);
                        cam.Render();
                        RenderTexture.active = rt;
                        screenShot.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                        cam.targetTexture = null;
                        RenderTexture.active = null;
                        rt.Release();

                        File.WriteAllBytes(saveFileIco, screenShot.EncodeToPNG());
                    }
                }

                //Compress data files into zip
                var zipSaveFile = TempPath + $"{objectName}.snapshot";
                ZipFile.CreateFromDirectory(TempPath + "snapshot/", zipSaveFile,
                    System.IO.Compression.CompressionLevel.Fastest, false);

                //Delete temporary data files
                File.Delete(saveFile);
                File.Delete(saveFileIco);
                Directory.Delete(TempPath + "snapshot/");


                //Extract the user file to a directory
                Directory.CreateDirectory(TempPath + "Extracted");
                using (var a = ZipFile.OpenRead(userSaveFile))
                    a.ExtractToDirectory(TempPath + "Extracted");

                //Remove user entry in the file and replace it with the newly created one
                File.Delete(TempPath + $"Extracted/{objectName}.snapshot");
                File.Move(zipSaveFile, TempPath + $"Extracted/{objectName}.snapshot");

                //Remove the user save file
                File.Delete(userSaveFile);

                //Create new one from this temporary folder
                ZipFile.CreateFromDirectory(TempPath + "Extracted", userSaveFile, System.IO.Compression.CompressionLevel.Fastest, false);

                //Remove the temporary folder
                foreach (string file in Directory.EnumerateFiles(TempPath + "Extracted"))
                    File.Delete(file);
                foreach (string file in Directory.GetDirectories(TempPath + "Extracted"))
                    Directory.Delete(file);
                Directory.Delete(TempPath + "Extracted");

                //Delete temporary zip file
                File.Delete(zipSaveFile);
            } else {
                //Something went wrong, most likely, you used this event before generating user profile
                //Logger.Exception("SaveSystem", "User File does not exist, exiting snapshot save");
            }
        }


        /// <summary>
        /// Deletes user with specified ID
        /// </summary>
        /// <param name="userID">User ID to delete</param>
        public static void DeleteUser(int userID) {
            try {
                Debug.Log(DataPath + $"profile{userID}.sav");
                File.Delete(DataPath + $"profile{userID}.sav");
            } catch {
                //Logger.Exception("SaveSystem", "User file does not exist");
            }
        }

        /// <summary>
        /// Deletes snapshot for a user with ID
        /// </summary>
        /// <param name="userID">User ID to delete snapshot from</param>
        /// <param name="snapshotID">Snapshot ID to delete</param>
        public static void DeleteSnapshot(int userID, int snapshotID) {
            //Get user profile path
            var userSaveFile = DataPath + $"profile{userID}.sav";

            if (File.Exists(userSaveFile)) {
                //Get base data file name
                var objectName = $"snapshot{snapshotID}";



                //Extract the user file to a directory
                Directory.CreateDirectory(TempPath + "Extracted");
                using (var a = ZipFile.OpenRead(userSaveFile))
                    a.ExtractToDirectory(TempPath + "Extracted");

                //Remove user entry in the file and replace it with the newly created one
                File.Delete(TempPath + $"Extracted/{objectName}.snapshot");

                //Remove the user save file
                File.Delete(userSaveFile);

                //Create new one from this temporary folder
                ZipFile.CreateFromDirectory(TempPath + "Extracted", userSaveFile, System.IO.Compression.CompressionLevel.Fastest, false);

                //Remove the temporary folder
                foreach (string file in Directory.EnumerateFiles(TempPath + "Extracted"))
                    File.Delete(file);
                foreach (string file in Directory.GetDirectories(TempPath + "Extracted"))
                    Directory.Delete(file);
                Directory.Delete(TempPath + "Extracted");
            } else {
                //Something went wrong, most likely, you used this event before generating user profile
                //Logger.Exception("SaveSystem", "User File does not exist, exiting snapshot deletion");
            }
        }

        /// <summary>
        /// Loads global data into GlobalData
        /// </summary>
        private static void LoadGlobalData() {
            try {
                var zipSaveFile = DataPath + "Global.sav";
                using (var archive = ZipFile.OpenRead(zipSaveFile)) {
                    var entry = archive.GetEntry("Global.dat");
                    if (entry == null) return;

                    using (var stream = entry.Open()) {
                        GlobalData = (BinaryBaseFormatter.Formatter.Deserialize(stream) as SaveData);
                    }
                }
            } catch (Exception ex) {

            }
        }

        /// <summary>
        /// Loads user data into UserData - uses userID provided in UseUserSaveData(int userID)
        /// </summary>
        /// <exception cref="NullReferenceException">Throws null ref if there was no user with that ID</exception>
        private static void LoadUserData() {
            try {
                var objectName = $"profile{_userID}";

                var zipSaveFile = DataPath + $"{objectName}.sav";
                using (var archive = ZipFile.OpenRead(zipSaveFile)) {
                    if (archive == null)
                        throw new NullReferenceException($"Archive at path {zipSaveFile} does not exist!");
                    var entry = archive.GetEntry($"{objectName}.dat");
                    if (entry == null)
                        throw new NullReferenceException(
                            $"Entry named {objectName}.dat does not exist in the current context!");

                    using (var stream = entry.Open()) {
                        UserData = (BinaryBaseFormatter.Formatter.Deserialize(stream) as SaveData);
                    }
                }
            } catch (Exception e) { Debug.LogWarning($"Failed to LoadUserData()\n{e}"); }
        }

        /// <summary>
        /// Loads snapshot data into SnapshotData - uses userID and snapshotID provided in GetSnapshotSaveData(int userID, int snapshotID)
        /// </summary>
        private static void LoadSnapshotData() {
            try {
                //Get base data file names
                var saveName = $"profile{_userID}";
                var objectName = $"snapshot{_snapshotID}";

                //Process base data file name into file path
                var zipSaveFile = DataPath + $"{saveName}.sav";

                //Open user profile
                using (var archive = ZipFile.OpenRead(zipSaveFile)) {
                    //Get snapshot save
                    var entry = archive.GetEntry($"{objectName}.snapshot");

                    //Extract snapshot save to temporary directory
                    Directory.CreateDirectory(TempPath + "Extracted");
                    entry.ExtractToFile(TempPath + $"Extracted/{objectName}.snapshot");

                    //Open snapshot save
                    using (var snapshotArchive = ZipFile.OpenRead(TempPath + $"Extracted/{objectName}.snapshot")) {
                        //Get data from snapshot
                        var dataEntry = snapshotArchive.GetEntry($"{objectName}.dat");
                        using (var stream = dataEntry.Open()) {
                            SnapshotData = (BinaryBaseFormatter.Formatter.Deserialize(stream) as SaveData);
                        }
                    }

                    //Remove the temporary folder
                    foreach (string file in Directory.EnumerateFiles(TempPath + "Extracted"))
                        File.Delete(file);
                    foreach (string file in Directory.GetDirectories(TempPath + "Extracted"))
                        Directory.Delete(file);
                    Directory.Delete(TempPath + "Extracted");
                }
            } catch (Exception e) { Debug.LogWarning($"Failed to LoadSnapshotData()\n{e}"); }
        }

        /// <summary>
        /// Finds highest user index.
        /// </summary>
        /// <returns>Highest index from users</returns>
        private static int GetLatestUserIndex() {
            var index = -1;
            try {
                var files = Directory.EnumerateFiles(DataPath, "*.snapshot", SearchOption.AllDirectories);
                foreach (var file in files) {
                    var indexString = Path.PathSeparator;
                }
            } catch(Exception e) { Debug.LogWarning($"Failed to GetLatestUserIndex()\n{e}"); }

            return index;
        }

        /// <summary>
        /// Finds latest snapshot in user file based on data file creation dateTime.
        /// </summary>
        /// <param name="userID">User ID to find latest snapshot id for</param>
        /// <returns>Index of latest snapshot, -1 if none</returns>
        private static int GetLatestSnapshotIndex(int userID) {
            var latestTime = DateTime.MinValue;
            int latestIndex = -1;
            try {
                //Get base data file names
                var saveName = $"profile{userID}";

                //Process base data file name into file path
                var zipSaveFile = DataPath + $"{saveName}.sav";

                //Open user profile
                using (var archive = ZipFile.OpenRead(zipSaveFile)) {
                    //Get snapshot save
                    var entries = archive.Entries;
                    foreach (var entry in entries) {
                        if (!entry.Name.Contains(".snapshot"))
                            continue;

                        //Create snapshotInfo object
                        var sInfo = new SnapshotInfo();

                        //Extract snapshot save to temporary directory
                        Directory.CreateDirectory(TempPath + "Extracted");
                        entry.ExtractToFile(TempPath + $"Extracted/T.snapshot");

                        //Open snapshot save
                        using (var snapshotArchive = ZipFile.OpenRead(TempPath + $"Extracted/T.snapshot")) {
                            //Get data from snapshot
                            var dataEntry = snapshotArchive.GetEntry($"{entry.Name.Replace(".snapshot", "")}.dat");
                            using (var stream = dataEntry.Open()) {
                                var e = (BinaryBaseFormatter.Formatter.Deserialize(stream) as SaveData);
                                if (e.GetEntry("Snapshot Info", out SnapshotInfo si)) {
                                    if (si.time.CompareTo(latestTime) > 0) {
                                        latestTime = si.time;
                                        latestIndex = si.snapshotID;
                                    }
                                }
                            }
                        }

                        //Remove the temporary folder
                        foreach (string file in Directory.EnumerateFiles(TempPath + "Extracted"))
                            File.Delete(file);
                        foreach (string file in Directory.GetDirectories(TempPath + "Extracted"))
                            Directory.Delete(file);
                        Directory.Delete(TempPath + "Extracted");
                    }
                }
            } catch {
                Debug.LogError("Failed to gather snapshot infos!");

            }

            return latestIndex;
        }

        [Serializable]
        public class ProfileInfo
        {
            public int userID;

            public string name;
            public string levelName;
            public int stars;
            public double progress;
            public TimeSpan playtime;

            [NonSerialized] public Texture2D preview;
        }
        [Serializable]
        public struct SnapshotInfo
        {
            public int userID;
            public int snapshotID;

            public string name;

            public string fullPath;
            public DateTime time;

            [NonSerialized] public Texture2D preview;
        }
        [Serializable]
        public struct SaveEntry
        {
            public string key;
            public object data;

            public SaveEntry(string key, object data) {
                this.key = key;
                this.data = data;
            }
        }
        [Serializable]
        public class SaveData : List<SaveEntry>
        {
            public bool GetEntry<Te>(string key, out Te data) {
                try {

                    var r = Find((j) => string.CompareOrdinal(j.key, key) == 0);

                    if (r.data.GetType() == typeof(Te)) {
                        data = (Te)r.data;
                        return true;
                    }


                } catch {
                    //Logger.Exception("SaveLogic", $"No entry with given key: {key}");
                }

                data = default;
                return false;
            }

            public void AddEntry(string key, object data) {
                var r = new SaveLogic.SaveEntry(key, data);

                for (var i = 0; i < Count; i++) {
                    if (key != this[i].key) continue;
                    this[i] = r;
                    return;
                }
                Add(r);
            }
        }
    }

    #region [Formatter]
    public static class BinaryBaseFormatter
    {
        public static BinaryFormatter Formatter { get; private set; }

        static BinaryBaseFormatter() {
            var ss = new SurrogateSelector ();
            var vector2Serialization = new Vector2SerializationSurrogate ();
            var vector2IntSerialization = new Vector2IntSerializationSurrogate ();
            var vector3Serialization = new Vector3SerializationSurrogate ();
            var vector3IntSerialization = new Vector3IntSerializationSurrogate ();
            var quaternionSerialization = new QuaternionSerializationSurrogate ();

            ss.AddSurrogate(typeof(Vector2), new StreamingContext(StreamingContextStates.All), vector2Serialization);
            ss.AddSurrogate(typeof(Vector2Int), new StreamingContext(StreamingContextStates.All), vector2IntSerialization);
            ss.AddSurrogate(typeof(Vector3), new StreamingContext(StreamingContextStates.All), vector3Serialization);
            ss.AddSurrogate(typeof(Vector3Int), new StreamingContext(StreamingContextStates.All), vector3IntSerialization);
            ss.AddSurrogate(typeof(Quaternion), new StreamingContext(StreamingContextStates.All), quaternionSerialization);

            Formatter = new BinaryFormatter {
                SurrogateSelector = ss
            };
        }
    }
    internal sealed class Vector3SerializationSurrogate : ISerializationSurrogate
    {

        public void GetObjectData(System.Object obj, SerializationInfo info, StreamingContext context) {

            var v3 = (Vector3) obj;
            info.AddValue("x", v3.x);
            info.AddValue("y", v3.y);
            info.AddValue("z", v3.z);
        }

        public System.Object SetObjectData(System.Object obj, SerializationInfo info, StreamingContext context, ISurrogateSelector selector) {

            var v3 = (Vector3) obj;
            v3.x = (float)info.GetValue("x", typeof(float));
            v3.y = (float)info.GetValue("y", typeof(float));
            v3.z = (float)info.GetValue("z", typeof(float));
            obj = v3;
            return obj;
        }
    }
    internal sealed class Vector3IntSerializationSurrogate : ISerializationSurrogate
    {

        public void GetObjectData(System.Object obj, SerializationInfo info, StreamingContext context) {

            var v3 = (Vector3Int) obj;
            info.AddValue("x", v3.x);
            info.AddValue("y", v3.y);
            info.AddValue("z", v3.z);
        }

        public System.Object SetObjectData(System.Object obj, SerializationInfo info, StreamingContext context, ISurrogateSelector selector) {

            var v3 = (Vector3Int) obj;
            v3.x = (int)info.GetValue("x", typeof(int));
            v3.y = (int)info.GetValue("y", typeof(int));
            v3.z = (int)info.GetValue("z", typeof(int));
            obj = v3;
            return obj;
        }
    }
    internal sealed class Vector2SerializationSurrogate : ISerializationSurrogate
    {

        public void GetObjectData(System.Object obj, SerializationInfo info, StreamingContext context) {

            var v2 = (Vector2) obj;
            info.AddValue("x", v2.x);
            info.AddValue("y", v2.y);
        }

        public System.Object SetObjectData(System.Object obj, SerializationInfo info, StreamingContext context, ISurrogateSelector selector) {

            var v2 = (Vector2) obj;
            v2.x = (float)info.GetValue("x", typeof(float));
            v2.y = (float)info.GetValue("y", typeof(float));
            obj = v2;
            return obj;
        }
    }
    internal sealed class Vector2IntSerializationSurrogate : ISerializationSurrogate
    {

        public void GetObjectData(System.Object obj, SerializationInfo info, StreamingContext context) {

            var v2 = (Vector2Int) obj;
            info.AddValue("x", v2.x);
            info.AddValue("y", v2.y);
        }

        public System.Object SetObjectData(System.Object obj, SerializationInfo info, StreamingContext context, ISurrogateSelector selector) {

            var v2 = (Vector2Int) obj;
            v2.x = (int)info.GetValue("x", typeof(int));
            v2.y = (int)info.GetValue("y", typeof(int));
            obj = v2;
            return obj;
        }
    }
    internal sealed class QuaternionSerializationSurrogate : ISerializationSurrogate
    {

        public void GetObjectData(System.Object obj, SerializationInfo info, StreamingContext context) {

            var q = (Quaternion) obj;
            info.AddValue("x", q.x);
            info.AddValue("y", q.y);
            info.AddValue("z", q.z);
            info.AddValue("w", q.w);
        }

        public System.Object SetObjectData(System.Object obj, SerializationInfo info, StreamingContext context, ISurrogateSelector selector) {

            var q = (Quaternion) obj;
            q.x = (float)info.GetValue("x", typeof(float));
            q.y = (float)info.GetValue("y", typeof(float));
            q.z = (float)info.GetValue("z", typeof(float));
            q.w = (float)info.GetValue("w", typeof(float));
            obj = q;
            return obj;
        }
    }

    [Serializable]
    public struct SerializableVector3
    {
        public float x, y, z;

        public SerializableVector3(float x, float y, float z) {
            this.x = x;
            this.y = y;
            this.z = z;
        }

        public override string ToString() => $"[{x}, {y}, {z}]";

        public static implicit operator Vector3(SerializableVector3 v) => new Vector3(v.x, v.y, v.z);
        public static implicit operator SerializableVector3(Vector3 v) => new SerializableVector3(v.x, v.y, v.z);
    }
    [Serializable]
    public struct SerializableVector2
    {
        public float x, y;

        public SerializableVector2(float x, float y) {
            this.x = x;
            this.y = y;
        }

        public override string ToString() => $"[{x}, {y}]";

        public static implicit operator Vector2(SerializableVector2 v) => new Vector2(v.x, v.y);
        public static implicit operator SerializableVector2(Vector2 v) => new SerializableVector2(v.x, v.y);
    }
    [Serializable]
    public struct SerializableVector3Int
    {
        public int x, y ,z;

        public SerializableVector3Int(int x, int y, int z) {
            this.x = x;
            this.y = y;
            this.z = z;
        }

        public override string ToString() => $"[{x}, {y}, {z}]";

        public static implicit operator Vector3Int(SerializableVector3Int v) => new Vector3Int(v.x, v.y, v.z);
        public static implicit operator SerializableVector3Int(Vector3Int v) => new SerializableVector3Int(v.x, v.y, v.z);
    }
    [Serializable]
    public struct SerializableVector2Int
    {
        public int x, y;

        public SerializableVector2Int(int x, int y) {
            this.x = x;
            this.y = y;
        }

        public override string ToString() => $"[{x}, {y}]";

        public static implicit operator Vector2Int(SerializableVector2Int v) => new Vector2Int(v.x, v.y);
        public static implicit operator SerializableVector2Int(Vector2Int v) => new SerializableVector2Int(v.x, v.y);
    }
    [Serializable]
    public struct SerializableQuaternion
    {
        public float x, y, z, w;

        public SerializableQuaternion(float x, float y, float z, float w) {
            this.x = x;
            this.y = y;
            this.z = z;
            this.w = w;
        }

        public override string ToString() => $"[{x}, {y}, {z}, {w}]";

        public static implicit operator Quaternion(SerializableQuaternion v) => new Quaternion(v.x, v.y, v.z, v.w);
        public static implicit operator SerializableQuaternion(Quaternion v) => new SerializableQuaternion(v.x, v.y, v.z, v.w);
    }

    #endregion
}