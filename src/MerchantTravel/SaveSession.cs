using System;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace Jinke.DredgeMerchantTravel
{
    // Identity only. Cooldown belongs exclusively to native SaveData.abilityHistory["manifest"].
    internal sealed class SaveSession : IDisposable
    {
        private static readonly FieldInfo ActiveSlot = typeof(SaveManager).GetField("activeSaveSlot", BindingFlags.NonPublic | BindingFlags.Instance);
        private SaveManager manager;
        internal SaveData Data { get; private set; }
        internal string SavePath { get; private set; }
        internal string FileHash { get; private set; }
        internal string Error { get; private set; }
        internal bool Matches(SaveManager candidate, SaveData data) { return manager == candidate && ReferenceEquals(Data, data); }
        internal void Bind(SaveManager candidate, SaveData data)
        {
            Dispose();
            Error = null; Data = data; manager = candidate;
            try
            {
                if (manager == null || data == null || ActiveSlot == null || manager.SaveStrategy == null) throw new InvalidOperationException("Save contract unavailable.");
                int slot = (int)ActiveSlot.GetValue(manager);
                if (slot < 0 || slot > 3) throw new InvalidDataException("Unexpected save slot.");
                SavePath = ProfileContract.FullPath(manager.SaveStrategy.GetFilePath(slot));
                ProfileContract.RequireEqual(SavePath, Path.Combine(Application.persistentDataPath, "saves/dredge-save" + slot + ".bin"), "active save path");
                FileHash = ProfileContract.HashFile(SavePath, 16L * 1024 * 1024);
                TravelLog.Info("SAVE_BOUND slot=" + slot + " checkpoint=" + FileHash + " cooldown=native-manifest");
            }
            catch (Exception exception) { Error = exception.Message; TravelLog.Error("SAVE_IDENTITY_UNAVAILABLE " + exception); }
        }
        public void Dispose() { manager = null; Data = null; SavePath = FileHash = null; }
    }
}
