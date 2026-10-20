using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Jinke.DredgeMerchantTravel;
using UnityEngine;

namespace Jinke.DredgeVoyage
{
    // Preferences follow exact save checkpoints. An unrelated/new/restored save never inherits another game's rules.
    // Cargo ownership is deliberately not stored here: it is committed with native progress and its purchase cost.
    internal sealed class VoyageStore : IDisposable
    {
        private sealed class Records
        {
            public int Schema = 1;
            public Dictionary<string, VoyageSettings> Checkpoints = new Dictionary<string, VoyageSettings>();
        }
        private readonly SaveSession session = new SaveSession();
        private Records records;
        private SaveManager manager;
        private string path;
        internal VoyageSettings Settings { get; private set; }
        internal SaveData Data { get { return session.Data; } }
        internal bool Ready { get { return manager != null && session.Error == null && Settings != null; } }

        internal void Bind(SaveManager candidate, SaveData data)
        {
            Dispose();
            session.Bind(candidate, data);
            if (session.Error != null) throw new InvalidOperationException(session.Error);
            path = Path.Combine(Application.persistentDataPath, "voyage-preferences-v1.json");
            ProfileContract.NoLinks(path);
            records = new Records();
            if (File.Exists(path))
            {
                if (new FileInfo(path).Length > 1024 * 1024) throw new InvalidDataException("航行设置文件过大。");
                records = JsonConvert.DeserializeObject<Records>(File.ReadAllText(path));
                if (records == null || records.Schema != 1 || records.Checkpoints == null || records.Checkpoints.Count > 2048)
                    throw new InvalidDataException("无法识别航行设置记录。");
                foreach (var pair in records.Checkpoints)
                {
                    if (!ProfileContract.IsHash(pair.Key) || pair.Value == null) throw new InvalidDataException("航行设置记录损坏。");
                    pair.Value.Validate();
                }
            }
            VoyageSettings found;
            Settings = records.Checkpoints.TryGetValue(session.FileHash, out found) ? found.Copy() : new VoyageSettings();
            manager = candidate;
            manager.OnSaveComplete += OnSaveComplete;
        }

        internal void Change(VoyageSettings next)
        {
            if (!Ready) throw new InvalidOperationException("请进入航行后调整设置。");
            next.Validate();
            string hash = ProfileContract.HashFile(session.SavePath, 16L * 1024 * 1024);
            Record(hash, next);
            Settings = next.Copy();
        }

        private void OnSaveComplete()
        {
            try
            {
                if (!Ready || !ReferenceEquals(manager.ActiveSaveData, Data)) return;
                if (Settings.IsDefault && records.Checkpoints.Count == 0) return;
                Record(ProfileContract.HashFile(session.SavePath, 16L * 1024 * 1024), Settings);
            }
            catch (Exception ex) { Debug.LogError("[Voyage] PREFERENCE_CHECKPOINT_FAILED " + ex); }
        }

        private void Record(string hash, VoyageSettings value)
        {
            var updated = new Records { Checkpoints = new Dictionary<string, VoyageSettings>(records.Checkpoints) };
            updated.Checkpoints[hash] = value.Copy();
            // Existing backups remain addressable. Refuse rather than silently discard recovery records.
            if (updated.Checkpoints.Count > 2048) throw new InvalidDataException("航行设置记录已满，请保留备份后整理。");
            ProfileContract.NoLinks(path);
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
                { writer.Write(JsonConvert.SerializeObject(updated, Formatting.Indented)); writer.Flush(); stream.Flush(true); }
                if (File.Exists(path)) File.Replace(temporary, path, path + ".bak");
                else File.Move(temporary, path);
                records = updated;
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        public void Dispose()
        {
            if (manager != null) manager.OnSaveComplete -= OnSaveComplete;
            manager = null; Settings = null; records = null; path = null; session.Dispose();
        }
    }
}
