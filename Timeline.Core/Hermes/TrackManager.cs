using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TheBirdOfHermes.Audio;
using TheBirdOfHermes.UI;
using UnityEngine;

namespace TheBirdOfHermes
{
    public class TrackManager
    {
        private readonly MonoBehaviour _owner;
        private readonly List<AudioLane> _lanes = new List<AudioLane>();
        private int _nextColorIndex;

        public List<AudioLane> Lanes => _lanes;
        public IEnumerable<AudioTrack> AllTracks => _lanes.SelectMany(l => l.Tracks);
        public float MasterVolume { get; set; } = 1f;

        public HashSet<AudioTrack> SelectedTracks { get; } = new HashSet<AudioTrack>();
        public AudioTrack PrimarySelectedTrack { get; private set; }
        public AudioTrack SelectionAnchor { get; private set; }
        public AudioLane ActiveLane { get; set; }

        public int SnapPixelDistance { get; set; } = 10;

        public TrackManager(MonoBehaviour owner)
        {
            _owner = owner;
            EnsureEmptyLane();
        }

        public AudioTrack AddTrackFromFile(string path)
        {
            LoadedFilesRegistry.Record(path);

            var lane = new AudioLane();
            var track = new AudioTrack(_owner);

            track.LoadFromFileAsync(path);

            track.TrackColor = WindowStyles.GetTrackColor(_nextColorIndex++);
            track.Lane = lane;
            lane.Tracks.Add(track);

            int insertIndex = _lanes.Count > 0 && _lanes[_lanes.Count - 1].Tracks.Count == 0
                ? _lanes.Count - 1
                : _lanes.Count;
            _lanes.Insert(insertIndex, lane);

            if (PrimarySelectedTrack == null)
                SelectTrack(track, false);

            EnsureEmptyLane();
            return track;
        }

        public AudioTrack AddTrackFromBytes(byte[] audioBytes, string fileName)
        {
            var lane = new AudioLane();
            var track = new AudioTrack(_owner);

            track.LoadFromBytesAsync(audioBytes, fileName);

            track.TrackColor = WindowStyles.GetTrackColor(_nextColorIndex++);
            track.Lane = lane;
            lane.Tracks.Add(track);

            int insertIndex = _lanes.Count > 0 && _lanes[_lanes.Count - 1].Tracks.Count == 0
                ? _lanes.Count - 1
                : _lanes.Count;
            _lanes.Insert(insertIndex, lane);

            if (PrimarySelectedTrack == null)
                SelectTrack(track, false);

            EnsureEmptyLane();
            return track;
        }

        public void RemoveTrack(AudioTrack track)
        {
            if (track == null) return;

            var lane = track.Lane;
            lane?.Tracks.Remove(track);
            RetireTrack(track);

            SelectedTracks.Remove(track);
            if (PrimarySelectedTrack == track)
                PrimarySelectedTrack = SelectedTracks.Count > 0 ? SelectedTracks.First() : null;
            if (SelectionAnchor == track)
                SelectionAnchor = null;

            RemoveEmptyLanesExceptLast();
            EnsureEmptyLane();
        }

        public void RemoveSelectedTracks()
        {
            var toRemove = SelectedTracks.ToList();
            SelectedTracks.Clear();
            PrimarySelectedTrack = null;
            SelectionAnchor = null;

            foreach (var track in toRemove)
            {
                track.Lane?.Tracks.Remove(track);
                RetireTrack(track);
            }

            RemoveEmptyLanesExceptLast();
            EnsureEmptyLane();
        }

        /// <summary>
        /// What happens to a track taken off its lane. Timeline sets this to keep it, silenced, for as long as
        /// its undo history can bring it back; without it the track is destroyed straight away, as before.
        /// </summary>
        public Action<AudioTrack> Retire { get; set; }

        private void RetireTrack(AudioTrack track)
        {
            if (Retire != null)
                Retire(track);
            else
                track.Destroy();
        }

        /// <summary>After lanes were put back as they were: no empty lane in the middle, one empty at the end.</summary>
        public void Normalize()
        {
            RemoveEmptyLanesExceptLast();
            EnsureEmptyLane();
        }

        public void ClearAll()
        {
            foreach (var lane in _lanes)
            foreach (var track in lane.Tracks)
                track.Destroy();

            _lanes.Clear();
            SelectedTracks.Clear();
            PrimarySelectedTrack = null;
            SelectionAnchor = null;
            ActiveLane = null;
            _nextColorIndex = 0;
            EnsureEmptyLane();
        }

        public void SelectTrack(AudioTrack track, bool additive)
        {
            if (!additive)
            {
                foreach (var t in SelectedTracks)
                    t.IsSelected = false;
                SelectedTracks.Clear();
            }

            if (track != null)
            {
                if (additive && SelectedTracks.Contains(track))
                {
                    track.IsSelected = false;
                    SelectedTracks.Remove(track);
                    PrimarySelectedTrack = SelectedTracks.Count > 0 ? SelectedTracks.First() : null;
                }
                else
                {
                    track.IsSelected = true;
                    SelectedTracks.Add(track);
                    PrimarySelectedTrack = track;
                    SelectionAnchor = track;
                }
            }
        }

        public void SelectRange(AudioTrack target)
        {
            if (target == null) return;

            var basis = new List<AudioTrack>(SelectedTracks);
            if (SelectionAnchor != null) basis.Add(SelectionAnchor);
            basis.Add(target);

            int minLane = int.MaxValue, maxLane = int.MinValue;
            float minTime = float.MaxValue, maxTime = float.MinValue;
            foreach (var t in basis)
            {
                int li = GetLaneIndex(t.Lane);
                if (li < 0) continue;
                minLane = Mathf.Min(minLane, li);
                maxLane = Mathf.Max(maxLane, li);
                minTime = Mathf.Min(minTime, t.AudibleStart);
                maxTime = Mathf.Max(maxTime, t.AudibleEnd);
            }
            if (minLane > maxLane)
            {
                SelectTrack(target, false);
                return;
            }

            foreach (var t in SelectedTracks)
                t.IsSelected = false;
            SelectedTracks.Clear();

            for (int i = minLane; i <= maxLane; i++)
            {
                var lane = GetLaneAtIndex(i);
                if (lane == null) continue;
                foreach (var tr in lane.Tracks)
                {
                    if (tr.AudibleStart < maxTime && tr.AudibleEnd > minTime)
                    {
                        tr.IsSelected = true;
                        SelectedTracks.Add(tr);
                    }
                }
            }

            if (!target.IsSelected) { target.IsSelected = true; SelectedTracks.Add(target); }
            PrimarySelectedTrack = target;
        }

        public void DeselectAll()
        {
            foreach (var track in SelectedTracks)
                track.IsSelected = false;
            SelectedTracks.Clear();
            PrimarySelectedTrack = null;
            SelectionAnchor = null;
        }

        /// <summary>
        /// Moves a track to a target lane. Handles overlap clamping on the target lane.
        /// </summary>
        public void MoveTrackToLane(AudioTrack track, AudioLane targetLane)
        {
            if (track.Lane == targetLane) return;

            var oldLane = track.Lane;
            oldLane?.Tracks.Remove(track);

            track.Lane = targetLane;
            targetLane.Tracks.Add(track);

            ClampTrackPosition(track, targetLane);

            RemoveEmptyLanesExceptLast();
            EnsureEmptyLane();
        }

        /// <summary>
        /// Clamps a track's offset so it doesn't overlap with other tracks on the same lane.
        /// </summary>
        public void ClampTrackPosition(AudioTrack track, AudioLane lane)
        {
            foreach (var other in lane.Tracks)
            {
                if (other == track) continue;

                if (track.AudibleStart < other.AudibleEnd && track.AudibleEnd > other.AudibleStart)
                {
                    float distToEnd = Mathf.Abs(track.AudibleStart - other.AudibleEnd);
                    float distToStart = Mathf.Abs(track.AudibleEnd - other.AudibleStart);

                    if (distToEnd <= distToStart)
                    {
                        track.Offset = other.AudibleEnd - track.TrimStart;
                    }
                    else
                    {
                        track.Offset = other.AudibleStart - track.EffectiveDuration - track.TrimStart;
                    }

                    track.Offset = Mathf.Max(0f, track.Offset);
                }
            }
        }

        public void MoveLane(int from, int to)
        {
            if (from < 0 || from >= _lanes.Count) return;

            int lastMovable = _lanes.Count - 1;
            if (_lanes.Count > 1 && _lanes[_lanes.Count - 1].Tracks.Count == 0)
                lastMovable = _lanes.Count - 2;

            if (from > lastMovable) return;
            to = Mathf.Clamp(to, 0, lastMovable);
            if (to == from) return;

            var lane = _lanes[from];
            _lanes.RemoveAt(from);
            _lanes.Insert(to, lane);
        }

        public int GetLaneIndex(AudioLane lane) => _lanes.IndexOf(lane);

        public AudioLane GetLaneAtIndex(int index)
        {
            if (index < 0 || index >= _lanes.Count) return null;
            return _lanes[index];
        }

        public void SyncAllPlayback(float playbackTime, bool isPlaying)
        {
            foreach (var lane in _lanes)
            foreach (var track in lane.Tracks)
                track.SyncPlayback(playbackTime, isPlaying, lane.Volume, MasterVolume, lane.IsMuted);
        }

        public void SeekAll(float playbackTime)
        {
            foreach (var lane in _lanes)
            foreach (var track in lane.Tracks)
                track.SeekTo(playbackTime);
        }

        /// <summary>
        /// Polls all tracks for async operation completion. Must be called from Update() on main thread.
        /// </summary>
        public void PollAsyncOperations()
        {
            foreach (var lane in _lanes)
            foreach (var track in lane.Tracks)
                track.PollAsyncCompletion();
        }

        /// <summary>
        /// Pixel-based snapping, computed in audible-edge space so it works correctly for trimmed
        /// tracks. Either audible edge of the dragged clip can snap to: other (non-selected) tracks'
        /// audible edges, the timeline origin (0), or the playhead (<paramref name="cursorTime"/>).
        /// Returns the snapped offset. Pass cursorTime &lt; 0 to skip playhead snapping.
        /// </summary>
        public float TrySnap(AudioTrack dragging, float proposedOffset, float pxPerSecond, float cursorTime)
        {
            float timeThreshold = SnapPixelDistance / pxPerSecond;
            float dragStart = proposedOffset + dragging.TrimStart;
            float dragEnd = proposedOffset + dragging.FullDuration - dragging.TrimEnd;

            float bestOffset = proposedOffset;
            float bestDist = timeThreshold;

            var targets = new List<float>();
            foreach (var lane in _lanes)
            {
                foreach (var other in lane.Tracks)
                {
                    if (other == dragging || SelectedTracks.Contains(other)) continue;
                    targets.Add(other.AudibleStart);
                    targets.Add(other.AudibleEnd);
                }
            }
            targets.Add(0f);
            if (cursorTime >= 0f)
                targets.Add(cursorTime);

            foreach (float t in targets)
            {
                float ds = Mathf.Abs(dragStart - t);
                if (ds < bestDist)
                {
                    bestDist = ds;
                    bestOffset = proposedOffset + (t - dragStart);
                }

                float de = Mathf.Abs(dragEnd - t);
                if (de < bestDist)
                {
                    bestDist = de;
                    bestOffset = proposedOffset + (t - dragEnd);
                }
            }

            float audibleStartResult = bestOffset + dragging.TrimStart;
            if (audibleStartResult < 0f)
                bestOffset -= audibleStartResult;

            return bestOffset;
        }

        public List<float> GetSnapLines(AudioTrack excluding)
        {
            var lines = new List<float>();
            foreach (var lane in _lanes)
            {
                foreach (var track in lane.Tracks)
                {
                    if (track == excluding) continue;
                    if (SelectedTracks.Contains(track)) continue;
                    lines.Add(track.AudibleStart);
                    lines.Add(track.AudibleEnd);
                }
            }
            return lines;
        }

        private void EnsureEmptyLane()
        {
            while (_lanes.Count > 1 &&
                   _lanes[_lanes.Count - 1].Tracks.Count == 0 &&
                   _lanes[_lanes.Count - 2].Tracks.Count == 0)
            {
                _lanes.RemoveAt(_lanes.Count - 1);
            }

            if (_lanes.Count == 0 || _lanes[_lanes.Count - 1].Tracks.Count > 0)
                _lanes.Add(new AudioLane());
        }

        private void RemoveEmptyLanesExceptLast()
        {
            for (int i = _lanes.Count - 1; i >= 0; i--)
            {
                if (_lanes[i].Tracks.Count == 0 && i < _lanes.Count - 1)
                    _lanes.RemoveAt(i);
            }
        }

        public bool HasAudio => _lanes.Any(l => l.Tracks.Count > 0);
        public int TrackCount => _lanes.Sum(l => l.Tracks.Count);
        public int LaneCount => _lanes.Count;

        #region Keybind Operations

        public void SaveTracksToDisk(IEnumerable<AudioTrack> tracks, string directoryPath)
        {
            Directory.CreateDirectory(directoryPath);
            foreach (var track in tracks)
            {
                if (track.IsBusy || !track.HasAudio) continue;
                byte[] baked = track.GetBakedWavBytes();
                if (baked == null) continue;
                string safeName = SanitizeFileName(track.FileName);
                if (string.IsNullOrEmpty(Path.GetExtension(safeName)))
                    safeName += ".wav";
                string path = Path.Combine(directoryPath, safeName);
                path = GetUniqueFilePath(path);
                File.WriteAllBytes(path, baked);
            }
        }

        public AudioTrack JoinTracks(List<AudioTrack> tracks)
        {
            if (tracks == null || tracks.Count < 2) return null;

            var sorted = tracks
                .Where(t => !t.IsBusy && t.HasAudio && t.Audio.Data != null)
                .OrderBy(t => t.AudibleStart)
                .ToList();

            if (sorted.Count < 2) return null;

            var sampleRate = sorted[0].Audio.Data.SampleRate;
            int channels = sorted[0].Audio.Data.Channels;

            var bakedSamples = new List<float[]>();
            var bakedTracks = new List<AudioTrack>();
            foreach (var track in sorted)
            {
                var data = track.Audio.Data;

                int startSample = Mathf.FloorToInt(track.TrimStart * data.SampleRate * channels);
                int endSample = Mathf.FloorToInt((data.Duration - track.TrimEnd) * data.SampleRate * channels);
                startSample = Mathf.Clamp(startSample, 0, data.Samples.Length);
                endSample = Mathf.Clamp(endSample, startSample, data.Samples.Length);

                int count = endSample - startSample;
                if (count <= 0) continue;

                float[] samples = new float[count];
                System.Array.Copy(data.Samples, startSample, samples, 0, count);

                int fadeInFrames = Mathf.FloorToInt(track.FadeInDuration * sampleRate);
                int fadeOutFrames = Mathf.FloorToInt(track.FadeOutDuration * sampleRate);
                int totalFrames = count / channels;

                for (int frame = 0; frame < totalFrames; frame++)
                {
                    float fade = 1f;
                    if (fadeInFrames > 0 && frame < fadeInFrames)
                        fade = (float)frame / fadeInFrames;
                    int fadeOutStart = totalFrames - fadeOutFrames;
                    if (fadeOutFrames > 0 && frame >= fadeOutStart)
                        fade = (float)(totalFrames - 1 - frame) / fadeOutFrames;

                    if (fade < 1f)
                    {
                        int baseIdx = frame * channels;
                        for (int ch = 0; ch < channels; ch++)
                            samples[baseIdx + ch] *= fade;
                    }
                }

                bakedSamples.Add(samples);
                bakedTracks.Add(track);
            }

            if (bakedSamples.Count < 2) return null;

            float firstStart = bakedTracks[0].AudibleStart;
            var allSamples = new List<float>();

            for (int bakedIdx = 0; bakedIdx < bakedSamples.Count; bakedIdx++)
            {
                float[] samples = bakedSamples[bakedIdx];
                AudioTrack track = bakedTracks[bakedIdx];

                int expectedStartSample = Mathf.RoundToInt((track.AudibleStart - firstStart) * sampleRate * channels);

                if (expectedStartSample > allSamples.Count)
                {
                    int padCount = expectedStartSample - allSamples.Count;
                    allSamples.AddRange(new float[padCount]);
                }

                for (int i = 0; i < samples.Length; i++)
                {
                    int dest = expectedStartSample + i;
                    if (dest < allSamples.Count)
                        allSamples[dest] += samples[i];
                    else
                        allSamples.Add(samples[i]);
                }
            }

            var combinedData = new AudioData
            {
                Samples = allSamples.ToArray(),
                SampleRate = sampleRate,
                Channels = channels
            };

            byte[] wavBytes = combinedData.EncodeWav();
            var firstTrack = sorted[0];
            float newOffset = firstTrack.Offset;

            foreach (var t in tracks)
                RemoveTrack(t);

            string firstName = Path.GetFileNameWithoutExtension(sorted[0].FileName);
            var newTrack = AddTrackFromBytes(wavBytes, $"{firstName}_joined.wav");
            newTrack.Offset = newOffset;
            SelectTrack(newTrack, false);

            return newTrack;
        }

        public AudioTrack[] SplitTrack(AudioTrack track, float splitTimelineTime)
        {
            if (track == null || track.IsBusy || !track.HasAudio || track.Audio.Data == null)
                return null;

            var data = track.Audio.Data;
            int channels = data.Channels;

            float splitTimeInAudio = splitTimelineTime - track.Offset;
            if (splitTimeInAudio <= track.TrimStart || splitTimeInAudio >= (data.Duration - track.TrimEnd))
                return null;

            int splitSampleIndex = Mathf.FloorToInt(splitTimeInAudio * data.SampleRate * channels);
            splitSampleIndex = Mathf.Clamp(splitSampleIndex, 0, data.Samples.Length);

            int countA = splitSampleIndex;
            int countB = data.Samples.Length - splitSampleIndex;

            var samplesA = new float[countA];
            var samplesB = new float[countB];
            Array.Copy(data.Samples, 0, samplesA, 0, countA);
            Array.Copy(data.Samples, splitSampleIndex, samplesB, 0, countB);

            var dataA = new AudioData { Samples = samplesA, SampleRate = data.SampleRate, Channels = channels };
            var dataB = new AudioData { Samples = samplesB, SampleRate = data.SampleRate, Channels = channels };

            byte[] wavA = dataA.EncodeWav();
            byte[] wavB = dataB.EncodeWav();

            string baseName = Path.GetFileNameWithoutExtension(track.FileName);
            Color trackColor = track.TrackColor;

            RemoveTrack(track);

            var newTrackA = AddTrackFromBytes(wavA, $"{baseName}_A.wav");
            newTrackA.Offset = track.Offset;
            newTrackA.TrackColor = trackColor;

            var newTrackB = AddTrackFromBytes(wavB, $"{baseName}_B.wav");
            newTrackB.Offset = splitTimelineTime;
            newTrackB.TrackColor = trackColor;

            SelectTrack(newTrackA, false);

            return new[] { newTrackA, newTrackB };
        }

        public float FindEarliestFreeStart(AudioLane lane, float cursor)
        {
            float t = Mathf.Max(0f, cursor);
            if (lane == null) return t;

            var sorted = lane.Tracks.OrderBy(x => x.AudibleStart).ToList();
            bool moved = true;
            while (moved)
            {
                moved = false;
                foreach (var tr in sorted)
                {
                    if (t >= tr.AudibleStart && t < tr.AudibleEnd)
                    {
                        t = tr.AudibleEnd;
                        moved = true;
                    }
                }
            }
            return t;
        }

        public AudioTrack PasteTrackAt(byte[] bakedWav, string name, float cursorTime, AudioLane targetLane = null)
        {
            if (bakedWav == null) return null;
            string trackName = string.IsNullOrEmpty(name) ? "Pasted" : name;
            var track = AddTrackFromBytes(bakedWav, $"{trackName}.wav");

            var lane = targetLane ?? ActiveLane;
            if (lane != null && _lanes.Contains(lane) && lane != track.Lane)
                MoveTrackToLane(track, lane);
            lane = track.Lane;

            float start = FindEarliestFreeStart(lane, cursorTime);
            track.Offset = Mathf.Max(0f, start - track.TrimStart);
            SelectTrack(track, false);
            return track;
        }

        public AudioTrack AddFileAtCursor(string path, float cursorTime, AudioLane targetLane = null)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;

            var track = AddTrackFromFile(path);

            var lane = targetLane ?? ActiveLane;
            if (lane != null && _lanes.Contains(lane) && lane != track.Lane)
                MoveTrackToLane(track, lane);
            lane = track.Lane;

            float start = FindEarliestFreeStart(lane, cursorTime);
            track.Offset = Mathf.Max(0f, start - track.TrimStart);
            SelectTrack(track, false);
            return track;
        }

        private static string SanitizeFileName(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var parts = name.Split(invalid);
            return string.Join("_", parts.Where(p => p.Length > 0).ToArray());
        }

        private static string GetUniqueFilePath(string path)
        {
            if (!File.Exists(path)) return path;
            string dir = Path.GetDirectoryName(path);
            string name = Path.GetFileNameWithoutExtension(path);
            string ext = Path.GetExtension(path);
            for (int i = 1; i < 100; i++)
            {
                string candidate = Path.Combine(dir, $"{name}_{i}{ext}");
                if (!File.Exists(candidate))
                    return candidate;
            }
            return Path.Combine(dir, $"{name}_{Guid.NewGuid():N}{ext}");
        }

        #endregion
    }
}