// Tests for Cloud Save's local-copy logic (src/CloudCopy.cs), run by the Build workflow before the launcher is built.
// The cloud is an in-memory store, so no account or network is needed. Compiled on its own with CloudCopy.cs and a small
// stand-in for the one Util method it uses (tests/run-tests.ps1). Exit code 0: all passed.
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace AzerothLauncher
{
    // the only part of Util that CloudCopy.cs calls
    public static class Util
    {
        public static void DeleteTreeQuiet(string path) { try { Directory.Delete(path, true); } catch (Exception) { } }
    }

    sealed class MemoryStore : ICloudStore
    {
        public readonly Dictionary<string, byte[]> Files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        public bool Offline;
        public int Uploads;
        public string Name { get { return "TestCloud"; } }

        public Dictionary<string, string> List()
        {
            if (Offline) throw new IOException("offline");
            var list = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var kv in Files) list[kv.Key] = Hash(kv.Value);
            return list;
        }

        public string LocalHash(string file) { return Hash(File.ReadAllBytes(file)); }
        public byte[] Download(string path) { if (Offline) throw new IOException("offline"); return Files[path]; }
        public void Upload(string path, byte[] data) { if (Offline) throw new IOException("offline"); Files[path] = data; Uploads++; }
        public void Delete(string path) { if (Offline) throw new IOException("offline"); Files.Remove(path); }

        static string Hash(byte[] data) { using (var md5 = MD5.Create()) return CloudCopy.Hex(md5.ComputeHash(data)); }
    }

    static class Tests
    {
        static int failed, passed;
        static string root;

        static void Check(bool ok, string what)
        {
            if (ok) passed++; else { failed++; Console.WriteLine("FAIL: " + what); }
        }

        static string NewCopy(string name)
        {
            string dir = Path.Combine(root, name, "copy");
            Directory.CreateDirectory(dir);
            return dir;
        }

        static void Write(string copy, string rel, string text)
        {
            string full = Path.Combine(copy, rel.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(full));
            File.WriteAllText(full, text);
        }

        static string Read(string copy, string rel)
        {
            string full = Path.Combine(copy, rel.Replace('/', Path.DirectorySeparatorChar));
            return File.Exists(full) ? File.ReadAllText(full) : null;
        }

        static readonly Action<string> Quiet = delegate { };

        static int Main()
        {
            root = Path.Combine(Path.GetTempPath(), "asl-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                FirstDownload();
                UploadThenNoChange();
                OfflineUploadIsPending();
                PendingWhileCloudChanged(true);
                PendingWhileCloudChanged(false);
                ClosedBeforeUpload();
                CaseOnlyRename();
                DeletedLocallyIsDeletedInCloud();
            }
            catch (Exception e) { failed++; Console.WriteLine("FAIL: unexpected " + e); }
            finally { try { Directory.Delete(root, true); } catch (Exception) { } }
            Console.WriteLine(passed + " passed, " + failed + " failed");
            return failed == 0 ? 0 : 1;
        }

        // a new device takes what the cloud has
        static void FirstDownload()
        {
            var store = new MemoryStore();
            store.Files["Saves/hero.sav"] = Encoding.UTF8.GetBytes("cloud");
            string copy = NewCopy("first");
            CloudCopy.Down(store, copy, delegate { return true; }, Quiet);
            Check(Read(copy, "Saves/hero.sav") == "cloud", "first sync downloads the cloud's file");
        }

        // a change goes up once; nothing changed means no upload at all
        static void UploadThenNoChange()
        {
            var store = new MemoryStore();
            string copy = NewCopy("upload");
            CloudCopy.Down(store, copy, delegate { return true; }, Quiet);
            Write(copy, "Saves/hero.sav", "v1");
            CloudCopy.Up(store, copy, Quiet);
            Check(Encoding.UTF8.GetString(store.Files["Saves/hero.sav"]) == "v1", "a change is uploaded");
            int before = store.Uploads;
            CloudCopy.Up(store, copy, Quiet);
            Check(store.Uploads == before, "an unchanged copy uploads nothing");
        }

        // offline: the upload fails, stays pending and is sent at the next sync
        static void OfflineUploadIsPending()
        {
            var store = new MemoryStore();
            string copy = NewCopy("offline");
            CloudCopy.Down(store, copy, delegate { return true; }, Quiet);
            Write(copy, "Saves/hero.sav", "played offline");
            store.Offline = true;
            bool threw = false;
            try { CloudCopy.Up(store, copy, Quiet); } catch (IOException) { threw = true; }
            Check(threw, "an offline upload reports its failure");
            store.Offline = false;
            CloudCopy.Down(store, copy, delegate { return true; }, Quiet);
            Check(store.Files.ContainsKey("Saves/hero.sav") && Encoding.UTF8.GetString(store.Files["Saves/hero.sav"]) == "played offline",
                  "the pending upload is sent at the next sync instead of being replaced");
        }

        // pending upload, and another device synced meanwhile: the player's choice wins, the other is kept aside
        static void PendingWhileCloudChanged(bool keepDevice)
        {
            var store = new MemoryStore();
            string copy = NewCopy("conflict-" + keepDevice);
            CloudCopy.Down(store, copy, delegate { return true; }, Quiet);
            Write(copy, "Saves/hero.sav", "this device");
            store.Offline = true;
            try { CloudCopy.Up(store, copy, Quiet); } catch (IOException) { }
            store.Offline = false;
            store.Files["Saves/hero.sav"] = Encoding.UTF8.GetBytes("other device");
            bool asked = false;
            CloudCopy.Down(store, copy, delegate { asked = true; return keepDevice; }, Quiet, "kept");
            Check(asked, "a conflict asks the player");
            string expect = keepDevice ? "this device" : "other device";
            Check(Encoding.UTF8.GetString(store.Files["Saves/hero.sav"]) == expect, "the cloud holds the chosen progress (" + keepDevice + ")");
            Check(Read(copy, "Saves/hero.sav") == expect, "the copy holds the chosen progress (" + keepDevice + ")");
            string kept = Path.Combine(Path.GetDirectoryName(copy), "kept");
            Check(Directory.Exists(kept) && Directory.GetFiles(kept, "hero.sav", SearchOption.AllDirectories).Length == 1,
                  "the other progress is kept aside (" + keepDevice + ")");
        }

        // the launcher was closed after the game but before uploading: noticed and uploaded at the next sync
        static void ClosedBeforeUpload()
        {
            var store = new MemoryStore();
            string copy = NewCopy("closed");
            CloudCopy.Down(store, copy, delegate { return true; }, Quiet);
            Write(copy, "Saves/hero.sav", "never uploaded");
            CloudCopy.Down(store, copy, delegate { return true; }, Quiet);   // next start: Down runs first
            Check(store.Files.ContainsKey("Saves/hero.sav") && Encoding.UTF8.GetString(store.Files["Saves/hero.sav"]) == "never uploaded",
                  "progress that never reached the cloud is uploaded, not overwritten");
        }

        // a name that changed only in case is one file: never downloaded and deleted in the same pass
        static void CaseOnlyRename()
        {
            var store = new MemoryStore();
            store.Files["Saves/Hero.sav"] = Encoding.UTF8.GetBytes("x");
            string copy = NewCopy("case");
            Write(copy, "Saves/hero.sav", "x");
            CloudCopy.Down(store, copy, delegate { return true; }, Quiet);
            bool present = Directory.GetFiles(Path.Combine(copy, "Saves")).Length == 1;
            Check(present, "a case-only difference leaves one file in the copy");
        }

        // a file removed on this device is removed from the cloud too
        static void DeletedLocallyIsDeletedInCloud()
        {
            var store = new MemoryStore();
            store.Files["Saves/old.sav"] = Encoding.UTF8.GetBytes("old");
            string copy = NewCopy("delete");
            CloudCopy.Down(store, copy, delegate { return true; }, Quiet);
            File.Delete(Path.Combine(copy, "Saves", "old.sav"));
            CloudCopy.Up(store, copy, Quiet);
            Check(!store.Files.ContainsKey("Saves/old.sav"), "a file deleted on this device is deleted in the cloud");
        }
    }
}
