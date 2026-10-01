using System.Collections.Generic;

namespace TheBirdOfHermes
{
    public static class TrackClipboard
    {
        private static readonly List<ClipboardTrackData> Items = new List<ClipboardTrackData>();
        public static bool HasData => Items.Count > 0;
        public static ClipboardTrackData[] Data => Items.ToArray();

        public static void Copy(IEnumerable<AudioTrack> tracks)
        {
            Items.Clear();
            foreach (var track in tracks)
            {
                if (!track.HasAudio || track.Audio?.Data == null) continue;
                byte[] baked = track.GetBakedWavBytes();
                if (baked == null) continue;
                Items.Add(new ClipboardTrackData { BakedWavBytes = baked, Name = track.Name });
            }
        }

        public static void Clear()
        {
            Items.Clear();
        }
    }

    public class ClipboardTrackData
    {
        public byte[] BakedWavBytes { get; set; }
        public string Name { get; set; }
    }
}
