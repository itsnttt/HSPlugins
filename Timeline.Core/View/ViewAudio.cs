using System.Collections.Generic;
using System.Linq;
using TheBirdOfHermes;
using TheBirdOfHermes.UI;
using Timeline.View;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Timeline
{
    public partial class Timeline
    {
        internal sealed partial class View
        {
            // The audio of The Bird of Hermes, merged in, shown in the Dope Sheet and the NLA: an Audio row
            // under the objects, then its lanes, each clip drawn with its waveform. Clips drag to move, by
            // their ends to trim, by the small squares on their top corners to fade, and up or down to change
            // lane; every edit is one step of Timeline's undo.

            private static readonly object _audioKey = new object();

            private static TrackManager AudioTracks
            {
                get { return HermesDAW.Instance == null ? null : HermesDAW.Instance.TrackManager; }
            }

            #region Rows
            private void AddAudioRows(List<Row> rows)
            {
                TrackManager audio = AudioTracks;
                if (audio == null || audio.HasAudio == false || editor == "graph")
                    return;
                rows.Add(new Row { type = RowType.AudioHead, key = _audioKey, label = "Audio", depth = 0, h = ROW });
                if (collapsed.Contains(_audioKey))
                    return;
                foreach (AudioLane lane in audio.Lanes)
                    rows.Add(new Row { type = RowType.AudioLane, key = lane, audio = lane, depth = 1, h = LANE });
            }

            /// <summary>The lanes and how many clips each holds, so the list is rebuilt when Hermes' own window or a scene changes them.</summary>
            private static int AudioSignature()
            {
                TrackManager audio = AudioTracks;
                if (audio == null)
                    return 0;
                unchecked
                {
                    int s = 17;
                    foreach (AudioLane lane in audio.Lanes)
                        s = s * 31 + lane.Tracks.Count + 1;
                    return s;
                }
            }

            private static string LaneName(TrackManager audio, AudioLane lane)
            {
                return string.IsNullOrEmpty(lane.Name) ? "Lane " + (audio.GetLaneIndex(lane) + 1) : lane.Name;
            }

            /// <summary>The list's label for an audio row.</summary>
            private string AudioRowLabel(Row r)
            {
                TrackManager audio = AudioTracks;
                if (r.type == RowType.AudioHead || audio == null)
                    return "Audio";
                if (r.audio.Tracks.Count == 0)
                    return Kit.Dim("New lane");
                string label = Kit.Escape(LaneName(audio, r.audio));
                if (r.audio.IsMuted)
                    label += " " + Kit.Dim("· muted");
                else if (Mathf.Abs(r.audio.Volume - 1f) > 0.005f)
                    label += " " + Kit.Dim("· " + Mathf.RoundToInt(r.audio.Volume * 100f) + " %");
                return label;
            }

            private void ToggleAudioMute(Row r)
            {
                if (r.audio == null || r.audio.Tracks.Count == 0)
                    return;
                T.RecordUndo("Mute audio lane");
                r.audio.IsMuted = !r.audio.IsMuted;
                ++_rowsVersion;
                Touch();
            }

            /// <summary>A click on an audio row in the list selects its clips: the lane's, or all of them.</summary>
            private void AudioRowDown(Row r, bool add)
            {
                TrackManager audio = AudioTracks;
                if (audio == null)
                    return;
                if (add == false)
                    audio.DeselectAll();
                IEnumerable<AudioTrack> clips = r.type == RowType.AudioHead ? audio.AllTracks : r.audio.Tracks;
                foreach (AudioTrack t in clips.ToList())
                {
                    if (t.IsSelected == false)
                        audio.SelectTrack(t, true);
                }
                if (r.type == RowType.AudioLane)
                    audio.ActiveLane = r.audio;
                Touch();
            }
            #endregion

            #region Drawing
            private struct AudioHit
            {
                public AudioTrack track;
                public float x0, x1, y0, y1, fadeInX, fadeOutX;
            }

            private readonly List<AudioHit> _audioHits = new List<AudioHit>();

            /// <summary>A waveform's peaks per 256 samples, worked out once per track and kept until its audio changes.</summary>
            private sealed class Peaks
            {
                public float[] source;
                public float[] min, max;
                public float scale;
            }

            private const int PeakBucket = 256;
            private readonly Dictionary<AudioTrack, Peaks> _peaks = new Dictionary<AudioTrack, Peaks>();

            private Peaks PeaksOf(AudioTrack track)
            {
                float[] mono = track.Audio == null ? null : track.Audio.MonoSamples;
                if (mono == null)
                    return null;
                Peaks pk;
                if (_peaks.TryGetValue(track, out pk) && pk.source == mono)
                    return pk;
                int n = (mono.Length + PeakBucket - 1) / PeakBucket;
                pk = new Peaks { source = mono, min = new float[n], max = new float[n] };
                float loudest = 0f;
                for (int b = 0; b < n; ++b)
                {
                    float lo = 0f, hi = 0f;
                    int end = Mathf.Min(mono.Length, (b + 1) * PeakBucket);
                    for (int i = b * PeakBucket; i < end; ++i)
                    {
                        float s = mono[i];
                        if (s < lo) lo = s;
                        if (s > hi) hi = s;
                    }
                    pk.min[b] = lo;
                    pk.max[b] = hi;
                    loudest = Mathf.Max(loudest, Mathf.Max(-lo, hi));
                }
                // Quiet audio is drawn larger, so its shape still reads.
                pk.scale = loudest > 0.0001f ? Mathf.Min(1f / loudest, 4f) : 1f;
                _peaks[track] = pk;
                return pk;
            }

            private AudioLane _audioDropLane;

            private void DrawAudio(Paint p, float w, float GH)
            {
                _audioHits.Clear();
                TrackManager audio = AudioTracks;
                if (audio == null)
                    return;
                foreach (Row r in _rows)
                {
                    if (r.type != RowType.AudioHead && r.type != RowType.AudioLane)
                        continue;
                    float y = RUL + r.y - scrollY, cy = y + r.h / 2f;
                    if (y > GH || y + r.h < RUL)
                        continue;
                    p.Rect(0f, y, w, r.h, r.type == RowType.AudioHead ? Pal.C(0x282B30) : Pal.C(0x23262B));
                    p.Rect(0f, y + r.h - 1f, w, 1f, Pal.C(0x1B1D21));
                    if (r.type == RowType.AudioHead)
                        continue;
                    AudioLane lane = r.audio;
                    if (lane == _audioDropLane)
                        p.StrokeRect(1f, y + 1f, w - 2f, r.h - 2f, 1f, Pal.accent);
                    if (lane.Tracks.Count == 0)
                    {
                        p.DashedRect(X(0f), y + 4f, X(T._duration) - X(0f), r.h - 9f, 4f, Pal.C(0x44474C));
                        p.Label("New lane · drag a clip here", (X(0f) + X(T._duration)) / 2f, cy, 11, Pal.C(0x6B6E74), TextAnchor.MiddleCenter);
                        continue;
                    }
                    foreach (AudioTrack t in lane.Tracks)
                        DrawClip(p, t, lane, y, r.h, w);
                }
                // A cached waveform holds on to its track's samples, so those of clips gone for good are let go.
                if (_peaks.Count > 0)
                {
                    var live = new HashSet<AudioTrack>(audio.AllTracks);
                    if (_peaks.Count > live.Count)
                    {
                        foreach (AudioTrack gone in _peaks.Keys.Where(k => live.Contains(k) == false).ToList())
                            _peaks.Remove(gone);
                    }
                }
            }

            private void DrawClip(Paint p, AudioTrack t, AudioLane lane, float y, float h, float w)
            {
                float x0 = X(t.AudibleStart), x1 = X(t.AudibleEnd), top = y + 3f, ch = h - 6f, cy = top + ch / 2f;
                if (t.HasAudio == false && t.IsBusy)
                    x1 = x0 + 120f;
                float fadeInX = X(t.AudibleStart + t.FadeInDuration), fadeOutX = X(t.AudibleEnd - t.FadeOutDuration);
                _audioHits.Add(new AudioHit { track = t, x0 = x0, x1 = x1, y0 = top, y1 = top + ch, fadeInX = fadeInX, fadeOutX = fadeOutX });
                if (x1 < -5f || x0 > w + 5f)
                    return;
                bool sel = t.IsSelected;
                p.globalAlpha = lane.IsMuted ? 0.4f : 1f;
                Color c = t.TrackColor;
                if (sel)
                    p.Rect(x0 - 2f, top - 2f, x1 - x0 + 4f, ch + 4f, Pal.accent);
                p.Rect(x0, top, x1 - x0, ch, new Color(c.r * 0.35f, c.g * 0.35f, c.b * 0.35f, 1f));

                Peaks pk = t.HasAudio ? PeaksOf(t) : null;
                if (pk != null && t.Audio.Data != null)
                {
                    float rate = t.Audio.Data.SampleRate / (float)PeakBucket, half = ch / 2f - 2f;
                    Color wave = new Color(c.r * 0.9f + 0.1f, c.g * 0.9f + 0.1f, c.b * 0.9f + 0.1f, 0.9f);
                    float from = Mathf.Max(x0, 0f), to = Mathf.Min(x1, w);
                    for (float x = from; x < to; x += 2f)
                    {
                        int b0 = Mathf.FloorToInt((Tt(x) - t.Offset) * rate), b1 = Mathf.CeilToInt((Tt(x + 2f) - t.Offset) * rate);
                        b0 = Mathf.Clamp(b0, 0, pk.min.Length - 1);
                        b1 = Mathf.Clamp(Mathf.Max(b1, b0 + 1), 1, pk.min.Length);
                        float lo = 0f, hi = 0f;
                        for (int b = b0; b < b1; ++b)
                        {
                            if (pk.min[b] < lo) lo = pk.min[b];
                            if (pk.max[b] > hi) hi = pk.max[b];
                        }
                        float fade = FadeAt(t, Tt(x));
                        lo = Mathf.Max(lo * pk.scale * fade, -1f);
                        hi = Mathf.Min(hi * pk.scale * fade, 1f);
                        p.Rect(x, cy - hi * half, 1.5f, Mathf.Max(1f, (hi - lo) * half), wave);
                    }
                }

                // The fades, as lines from silence up to full, with a handle to drag on each.
                Color ink = new Color(1f, 1f, 1f, 0.75f);
                if (t.FadeInDuration > 0f)
                    p.Line(x0, top + ch, fadeInX, top, 1f, ink);
                if (t.FadeOutDuration > 0f)
                    p.Line(fadeOutX, top, x1, top + ch, 1f, ink);
                if (sel)
                {
                    p.Rect(fadeInX - 3f, top, 6f, 6f, Pal.C(0xFFE2B0));
                    p.Rect(fadeOutX - 3f, top, 6f, 6f, Pal.C(0xFFE2B0));
                }

                if (x1 - x0 > 24f)
                {
                    string label = t.IsBusy ? t.AsyncDescription + " " + Mathf.RoundToInt(t.AsyncProgress * 100f) + " %" : t.Name;
                    p.Label(label, Mathf.Max(x0, 0f) + 7f, top + 8f, 10, Pal.C(0xE4E7EC), TextAnchor.MiddleLeft, true);
                }
                p.globalAlpha = 1f;
            }

            private static float FadeAt(AudioTrack t, float time)
            {
                float pos = time - t.AudibleStart, len = t.EffectiveDuration;
                float f = 1f;
                if (t.FadeInDuration > 0f && pos < t.FadeInDuration)
                    f = Mathf.Clamp01(pos / t.FadeInDuration);
                if (t.FadeOutDuration > 0f && pos > len - t.FadeOutDuration)
                    f = Mathf.Min(f, Mathf.Clamp01((len - pos) / t.FadeOutDuration));
                return f;
            }
            #endregion

            #region Input
            private sealed class AudioDragState
            {
                public AudioTrack track;
                public string zone;
                public float x0, y0;
                public float audibleStart, trimStart, trimEnd, fadeIn, fadeOut;
                public Dictionary<AudioTrack, float> offsets;
                public bool moved;
            }

            private AudioDragState _adrag;

            private AudioHit? AudioAt(Vector2 p, out string zone)
            {
                zone = null;
                for (int i = _audioHits.Count - 1; i >= 0; --i)
                {
                    AudioHit h = _audioHits[i];
                    if (p.y < h.y0 || p.y > h.y1 || p.x < h.x0 - 4f || p.x > h.x1 + 4f)
                        continue;
                    if (h.track.IsSelected && p.y < h.y0 + 8f && Mathf.Abs(p.x - h.fadeInX) < 5f)
                        zone = "fadein";
                    else if (h.track.IsSelected && p.y < h.y0 + 8f && Mathf.Abs(p.x - h.fadeOutX) < 5f)
                        zone = "fadeout";
                    else if (Mathf.Abs(p.x - h.x0) < 5f)
                        zone = "left";
                    else if (Mathf.Abs(p.x - h.x1) < 5f)
                        zone = "right";
                    else
                        zone = "body";
                    return h;
                }
                return null;
            }

            private bool OnAudioRow(Vector2 p)
            {
                Row r = RowAt(p.y);
                return r != null && (r.type == RowType.AudioLane || r.type == RowType.AudioHead);
            }

            /// <summary>A press on the audio rows. True when it was theirs, so nothing else handles it.</summary>
            private bool AudioDown(PointerEventData e, Vector2 p)
            {
                if (e.button != PointerEventData.InputButton.Left || p.y < RUL || OnAudioRow(p) == false)
                    return false;
                TrackManager audio = AudioTracks;
                bool add = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
                string zone;
                AudioHit? hit = AudioAt(p, out zone);
                if (hit.HasValue == false)
                {
                    if (add == false)
                        audio.DeselectAll();
                    Touch();
                    return true;
                }
                AudioTrack t = hit.Value.track;
                if (add)
                    audio.SelectTrack(t, true);
                else if (t.IsSelected == false)
                    audio.SelectTrack(t, false);
                if (t.IsSelected == false || t.IsBusy)
                {
                    Touch();
                    return true;
                }
                audio.ActiveLane = t.Lane;
                _adrag = new AudioDragState
                {
                    track = t, zone = zone, x0 = p.x, y0 = p.y,
                    audibleStart = t.AudibleStart, trimStart = t.TrimStart, trimEnd = t.TrimEnd, fadeIn = t.FadeInDuration, fadeOut = t.FadeOutDuration,
                    offsets = audio.SelectedTracks.ToDictionary(x => x, x => x.Offset)
                };
                Touch();
                return true;
            }

            private bool AudioDrag(PointerEventData e, Vector2 p)
            {
                if (_adrag == null)
                    return false;
                AudioDragState d = _adrag;
                AudioTrack t = d.track;
                if (d.moved == false)
                {
                    if (Mathf.Abs(p.x - d.x0) + Mathf.Abs(p.y - d.y0) < 3f)
                        return true;
                    d.moved = true;
                    T.RecordUndo(d.zone == "body" ? "Move audio" : d.zone == "left" || d.zone == "right" ? "Trim audio" : "Fade audio");
                }
                bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
                switch (d.zone)
                {
                    case "body":
                    {
                        // The clip's audible start lands on the snap, and every selected clip moves with it.
                        float start = Mathf.Max(0f, SnapT(d.audibleStart + Tt(p.x) - Tt(d.x0), shift));
                        float dt = start - d.audibleStart;
                        foreach (KeyValuePair<AudioTrack, float> o in d.offsets)
                            o.Key.Offset = Mathf.Max(-o.Key.TrimStart, o.Value + dt);
                        Row r = RowAt(p.y);
                        _audioDropLane = d.offsets.Count == 1 && r != null && r.type == RowType.AudioLane && r.audio != t.Lane ? r.audio : null;
                        break;
                    }
                    case "left":
                    {
                        float at = SnapT(Tt(p.x), shift);
                        t.TrimStart = Mathf.Clamp(at - t.Offset, 0f, t.FullDuration - t.TrimEnd - 0.05f);
                        t.ClampFade();
                        break;
                    }
                    case "right":
                    {
                        float at = SnapT(Tt(p.x), shift);
                        t.TrimEnd = Mathf.Clamp(t.Offset + t.FullDuration - at, 0f, t.FullDuration - t.TrimStart - 0.05f);
                        t.ClampFade();
                        break;
                    }
                    case "fadein":
                        t.FadeInDuration = Mathf.Clamp(Tt(p.x) - t.AudibleStart, 0f, t.EffectiveDuration);
                        t.ClampFade();
                        break;
                    case "fadeout":
                        t.FadeOutDuration = Mathf.Clamp(t.AudibleEnd - Tt(p.x), 0f, t.EffectiveDuration);
                        t.ClampFade();
                        break;
                }
                return true;
            }

            private bool AudioUp(PointerEventData e, Vector2 p)
            {
                if (_adrag == null)
                    return false;
                AudioDragState d = _adrag;
                _adrag = null;
                TrackManager audio = AudioTracks;
                if (d.moved && audio != null)
                {
                    // Clips never overlap on a lane: one dropped onto another slides to the nearer side of it.
                    if (_audioDropLane != null)
                        audio.MoveTrackToLane(d.track, _audioDropLane);
                    else
                    {
                        foreach (AudioTrack t in d.offsets.Keys)
                        {
                            if (t.Lane != null)
                                audio.ClampTrackPosition(t, t.Lane);
                        }
                    }
                    audio.SeekAll(T._playbackTime);
                }
                _audioDropLane = null;
                ++_rowsVersion;
                _rowsDirty = true;
                Touch();
                return true;
            }

            /// <summary>The right click menu over the audio rows. True when it was theirs.</summary>
            private bool AudioMenu(PointerEventData e, Vector2 p)
            {
                if (p.y < RUL || OnAudioRow(p) == false)
                    return false;
                string zone;
                AudioHit? hit = AudioAt(p, out zone);
                TrackManager audio = AudioTracks;
                if (hit.HasValue && hit.Value.track.IsSelected == false)
                    audio.SelectTrack(hit.Value.track, false);
                Row r = RowAt(p.y);
                float t = FrameSnap(Tt(p.x));
                var items = hit.HasValue ? ClipItems(hit.Value.track) : AudioRowItems(r, t);
                items.Add(new MenuItem { sep = true });
                items.Add(new MenuItem { label = "Move playhead here", act = () => SetTime(t) });
                OpenMenuAtPointer(e, items);
                Touch();
                return true;
            }

            /// <summary>What can be done to the clips selected, the one right clicked first among them.</summary>
            private List<MenuItem> ClipItems(AudioTrack clip)
            {
                TrackManager audio = AudioTracks;
                List<AudioTrack> selected = audio.SelectedTracks.ToList();
                float playhead = T._playbackTime;
                bool canSplit = CanSplit(clip, playhead);
                var colours = new List<MenuItem>();
                for (int i = 0; i < WindowStyles.TrackColors.Length; ++i)
                {
                    Color c = WindowStyles.TrackColors[i];
                    colours.Add(new MenuItem { label = "Colour " + (i + 1), dot = c, act = () => EditAudio("Clip colour", () => selected.ForEach(x => x.TrackColor = c)) });
                }
                return new List<MenuItem>
                {
                    new MenuItem { head = selected.Count > 1 ? selected.Count + " CLIPS SELECTED" : "CLIP “" + clip.Name + "”" },
                    new MenuItem { label = "Rename…", act = () => RenameDialog("Rename clip", clip.Name, v => EditAudio("Rename clip", () => clip.Name = v.Trim())) },
                    new MenuItem { label = "Colour", sub = colours },
                    new MenuItem { sep = true },
                    new MenuItem { label = "Split at playhead", kb = "Y", disabled = canSplit == false, act = () => EditAudio("Split audio", () => audio.SplitTrack(clip, FrameSnap(playhead))) },
                    new MenuItem { label = "Join the " + selected.Count + " selected clips", disabled = selected.Count < 2, act = () => EditAudio("Join audio", () => audio.JoinTracks(selected)) },
                    new MenuItem { label = selected.Count > 1 ? "Delete " + selected.Count + " clips" : "Delete clip", kb = "X", act = () => EditAudio("Delete audio", () => selected.ForEach(audio.RemoveTrack)) },
                    new MenuItem { sep = true },
                    new MenuItem { label = selected.Count > 1 ? "Save them as WAV…" : "Save as WAV…", act = () => HermesDAW.Instance.SaveTracks(selected) },
                    new MenuItem { label = "Effects and more…", act = () => HermesDAW.Instance.WindowOpen = true }
                };
            }

            /// <summary>An audio row's menu: adding audio, and the lane's own settings.</summary>
            private List<MenuItem> AudioRowItems(Row r, float time)
            {
                TrackManager audio = AudioTracks;
                var items = new List<MenuItem>();
                AudioLane lane = r != null && r.type == RowType.AudioLane ? r.audio : null;
                bool real = lane != null && lane.Tracks.Count != 0;
                items.Add(new MenuItem { head = lane == null ? "AUDIO" : real ? "LANE “" + LaneName(audio, lane) + "”" : "NEW LANE" });
                items.Add(new MenuItem { label = lane == null ? "Add audio file…" : "Add audio file here…", act = () => AddAudioFiles(time, lane) });
                if (real)
                {
                    var volumes = new List<MenuItem>();
                    foreach (int percent in new[] { 100, 75, 50, 25 })
                    {
                        float v = percent / 100f;
                        volumes.Add(new MenuItem { label = percent + " %", check = Mathf.Abs(lane.Volume - v) < 0.005f, act = () => EditAudio("Lane volume", () => lane.Volume = v) });
                    }
                    items.Add(new MenuItem { label = "Mute", check = lane.IsMuted, act = () => EditAudio("Mute audio lane", () => lane.IsMuted = !lane.IsMuted) });
                    items.Add(new MenuItem { label = "Volume", sub = volumes });
                    items.Add(new MenuItem { label = "Rename…", act = () => RenameDialog("Rename lane", lane.Name ?? "", v => EditAudio("Rename lane", () => lane.Name = v.Trim())) });
                    items.Add(new MenuItem { label = "Delete lane and its clips", act = () => EditAudio("Delete audio lane", () => lane.Tracks.ToList().ForEach(audio.RemoveTrack)) });
                }
                if (lane == null)
                {
                    var masters = new List<MenuItem>();
                    foreach (int percent in new[] { 100, 75, 50, 25 })
                    {
                        float v = percent / 100f;
                        masters.Add(new MenuItem { label = percent + " %", check = Mathf.Abs(audio.MasterVolume - v) < 0.005f, act = () => EditAudio("Audio volume", () => audio.MasterVolume = v) });
                    }
                    items.Add(new MenuItem { label = "Volume of all audio", sub = masters });
                    items.Add(new MenuItem { label = "Scene length to fit the audio", act = HermesDAW.Instance.FitSceneLength });
                    items.Add(new MenuItem { label = "Audio window…", act = () => HermesDAW.Instance.WindowOpen = true });
                }
                return items;
            }

            /// <summary>One undoable change to the audio, then the list and the playback brought up to date.</summary>
            private void EditAudio(string label, System.Action change)
            {
                T.RecordUndo(label);
                change();
                TrackManager audio = AudioTracks;
                if (audio != null)
                    audio.SeekAll(T._playbackTime);
                ++_rowsVersion;
                _rowsDirty = true;
                Touch();
            }

            /// <summary>The NLA's keys on the selected clips while no keys are selected: Y splits them at the playhead, X and Delete remove them.</summary>
            private bool AudioKeys(bool ctrl)
            {
                TrackManager audio = AudioTracks;
                if (ctrl || audio == null || audio.SelectedTracks.Count == 0 || T._selectedKeyframes.Count != 0)
                    return false;
                List<AudioTrack> selected = audio.SelectedTracks.ToList();
                if (Input.GetKeyDown(KeyCode.Y))
                {
                    float t = FrameSnap(T._playbackTime);
                    List<AudioTrack> cut = selected.Where(c => CanSplit(c, t)).ToList();
                    if (cut.Count != 0)
                        EditAudio("Split audio", () => cut.ForEach(c => audio.SplitTrack(c, t)));
                }
                else if (Input.GetKeyDown(KeyCode.X) || Input.GetKeyDown(KeyCode.Delete))
                    EditAudio("Delete audio", () => selected.ForEach(audio.RemoveTrack));
                else
                    return false;
                return true;
            }

            private static bool CanSplit(AudioTrack clip, float t)
            {
                return clip.HasAudio && clip.IsBusy == false && t > clip.AudibleStart + 0.01f && t < clip.AudibleEnd - 0.01f;
            }

            public void AddAudioFiles(float time, AudioLane lane)
            {
                if (HermesDAW.Instance == null)
                    return;
                T.RecordUndo("Add audio");
                List<AudioTrack> added = HermesDAW.Instance.AddFiles(time, lane);
                if (added.Count != 0)
                    Toast(added.Count == 1 ? "Added “" + added[0].Name + "”. It shows under Audio once it has loaded." : "Added " + added.Count + " audio files.");
                ++_rowsVersion;
                _rowsDirty = true;
                Touch();
            }
            #endregion
        }
    }
}
