using System.Collections.Generic;
using TheBirdOfHermes;
using UnityEngine;

namespace Timeline
{
    /// <summary>
    /// What The Bird of Hermes, merged in, reads of the timeline to play its audio along, and the few
    /// changes it makes back. It used to reach these through a publicized Timeline; here they are its own.
    /// Its clips are part of Timeline's undo too: a snapshot remembers where every clip sat and which lane
    /// held it, and a clip taken off its lane is kept, silent, while the history can still bring it back.
    /// </summary>
    public partial class Timeline
    {
        internal static float AudioClockTime
        {
            get { return _self == null ? 0f : _self._playbackTime; }
        }

        internal static bool AudioClockPlaying
        {
            get { return _self != null && _self._isPlaying; }
        }

        /// <summary>Whether the Timeline window is showing; the audio window closes with it.</summary>
        internal static bool WindowShown
        {
            get { return _self != null && _self.UiVisible; }
        }

        internal static void AudioSeek(float time)
        {
            if (_self != null)
                _self.SeekPlaybackTime(time);
        }

        /// <summary>The scene ends where the last audio ends, as Hermes' Sync Time did; undoable.</summary>
        internal static void FitLengthToAudio(float end)
        {
            if (_self == null || end <= 0f)
                return;
            _self.RecordUndo("Scene length");
            _self._duration = end;
            _self.UpdateGrid();
        }

        private static TrackManager Audio
        {
            get { return HermesDAW.Instance == null ? null : HermesDAW.Instance.TrackManager; }
        }

        private void InitAudio()
        {
            HermesDAW.Init(this, Config, Logger);
            if (Audio != null)
                Audio.Retire = RetireAudioTrack;
        }

        #region Undo
        /// <summary>Clips taken off their lanes and kept for the history; destroyed once nothing refers to them.</summary>
        private readonly HashSet<AudioTrack> _retiredAudio = new HashSet<AudioTrack>();

        private void RetireAudioTrack(AudioTrack track)
        {
            if (track.Audio != null && track.Audio.Source != null)
                track.Audio.Source.Stop();
            track.IsSelected = false;
            _retiredAudio.Add(track);
        }

        private sealed class AudioClipState
        {
            public AudioTrack track;
            public float offset, trimStart, trimEnd, fadeIn, fadeOut;
            public string name;
            public Color color;
        }

        private sealed class AudioLaneState
        {
            public AudioLane lane;
            public float volume;
            public bool muted;
            public string name;
            public bool hasColor;
            public Color color;
            public List<AudioClipState> clips;
        }

        private sealed class AudioState
        {
            public float master;
            public List<AudioLaneState> lanes;
        }

        private static AudioState CaptureAudio()
        {
            TrackManager audio = Audio;
            if (audio == null)
                return null;
            var state = new AudioState { master = audio.MasterVolume, lanes = new List<AudioLaneState>(audio.Lanes.Count) };
            foreach (AudioLane lane in audio.Lanes)
            {
                var ls = new AudioLaneState
                {
                    lane = lane, volume = lane.Volume, muted = lane.IsMuted, name = lane.Name, hasColor = lane.HasColor, color = lane.Color,
                    clips = new List<AudioClipState>(lane.Tracks.Count)
                };
                foreach (AudioTrack t in lane.Tracks)
                {
                    ls.clips.Add(new AudioClipState
                    {
                        track = t, offset = t.Offset, trimStart = t.TrimStart, trimEnd = t.TrimEnd,
                        fadeIn = t.FadeInDuration, fadeOut = t.FadeOutDuration, name = t.Name, color = t.TrackColor
                    });
                }
                state.lanes.Add(ls);
            }
            return state;
        }

        private void RestoreAudio(AudioState state)
        {
            TrackManager audio = Audio;
            if (audio == null || state == null)
                return;
            var kept = new HashSet<AudioTrack>();
            foreach (AudioLaneState ls in state.lanes)
            {
                foreach (AudioClipState cs in ls.clips)
                {
                    // A clip Hermes' own window destroyed since has nothing left to play.
                    if (cs.track.Audio != null)
                        kept.Add(cs.track);
                }
            }
            foreach (AudioLane lane in audio.Lanes)
            {
                foreach (AudioTrack t in lane.Tracks)
                {
                    if (kept.Contains(t) == false)
                        RetireAudioTrack(t);
                }
            }

            audio.Lanes.Clear();
            foreach (AudioLaneState ls in state.lanes)
            {
                AudioLane lane = ls.lane;
                lane.Volume = ls.volume;
                lane.IsMuted = ls.muted;
                lane.Name = ls.name;
                lane.HasColor = ls.hasColor;
                lane.Color = ls.color;
                lane.Tracks.Clear();
                foreach (AudioClipState cs in ls.clips)
                {
                    AudioTrack t = cs.track;
                    if (kept.Contains(t) == false)
                        continue;
                    t.Offset = cs.offset;
                    t.TrimStart = cs.trimStart;
                    t.TrimEnd = cs.trimEnd;
                    t.FadeInDuration = cs.fadeIn;
                    t.FadeOutDuration = cs.fadeOut;
                    t.Name = cs.name;
                    t.TrackColor = cs.color;
                    t.Lane = lane;
                    lane.Tracks.Add(t);
                    _retiredAudio.Remove(t);
                }
                audio.Lanes.Add(lane);
            }
            audio.MasterVolume = state.master;
            audio.Normalize();
            foreach (AudioTrack t in new List<AudioTrack>(audio.SelectedTracks))
            {
                if (kept.Contains(t) == false)
                    audio.SelectTrack(t, true);
            }
            audio.SeekAll(_playbackTime);
        }

        /// <summary>Destroys the kept clips no snapshot and no lane refers to any more.</summary>
        private void PruneRetiredAudio()
        {
            if (_retiredAudio.Count == 0)
                return;
            var referenced = new HashSet<AudioTrack>();
            foreach (List<HistoryState> stack in new[] { _undoStack, _redoStack })
            {
                foreach (HistoryState h in stack)
                {
                    if (h.audio == null)
                        continue;
                    foreach (AudioLaneState ls in h.audio.lanes)
                        foreach (AudioClipState cs in ls.clips)
                            referenced.Add(cs.track);
                }
            }
            foreach (AudioTrack t in new List<AudioTrack>(_retiredAudio))
            {
                if (referenced.Contains(t) || (t.Lane != null && t.Lane.Tracks.Contains(t)))
                    continue;
                _retiredAudio.Remove(t);
                t.Destroy();
            }
        }
        #endregion
    }
}
