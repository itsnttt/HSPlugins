using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace TheBirdOfHermes
{
    public static class LoadedFilesRegistry
    {
        public class Entry
        {
            public string Name;
            public string Path;
        }

        private static readonly List<Entry> _entries = new List<Entry>();
        public static List<Entry> Entries => new List<Entry>(_entries);

        public static void Record(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            if (_entries.Any(e => e.Path == path)) return;
            _entries.Add(new Entry { Name = Path.GetFileName(path), Path = path });
        }

        public static void Remove(string path)
        {
            _entries.RemoveAll(e => e.Path == path);
        }
    }
}
