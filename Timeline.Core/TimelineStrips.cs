using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml;
using Studio;
using Timeline.Nla;
using ToolBox.Extensions;
using UILib;
using UILib.ContextMenu;
using UILib.EventHandlers;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine;

namespace Timeline
{
    /// <summary>
    /// Non linear animation: reusable clips placed on the timeline as strips.
    ///
    /// A strip drives the same interpolables a track would, so nothing about how values reach the game
    /// changes. What changes is where the keyframes come from: instead of the track's own list, the
    /// surrounding pair is taken from the clip at a time the strip remaps. That is what makes a two
    /// second loop placeable five times, at different speeds, without copying keyframes.
    ///
    /// The whole feature is inert while there are no strips, so scenes that never use it behave exactly
    /// as before.
    /// </summary>
    public partial class Timeline
    {
        private readonly List<MotionStrip> _strips = new List<MotionStrip>();
        /// <summary>Rebuilt every evaluation: the interpolables a strip speaks for this frame.</summary>
        private readonly Dictionary<Interpolable, StripSample> _stripSamples = new Dictionary<Interpolable, StripSample>();

        #region Evaluation
        /// <summary>
        /// Works out what each strip has to say at the current time. Later strips win over earlier ones,
        /// which is plain Replace blending; mixing several contributions is a later stage.
        /// </summary>
        private void SampleStrips()
        {
            _stripSamples.Clear();
            if (_strips.Count == 0)
                return;

            _blended.Clear();
            RebuildStripOrder();
            int lane = int.MinValue;
            foreach (MotionStrip strip in _stripsByLane)
            {
                // Only the first strip in a lane may hold its first frame back over the time before it,
                // which is Blender's rule. A later one doing it covers every strip ahead of it in the
                // lane, and since the lane is processed in time order the later one would always win.
                bool firstInLane = strip.lane != lane;
                lane = strip.lane;
                if (strip == _tweakStrip)
                    continue;

                float weight = strip.WeightAt(_playbackTime, firstInLane);
                float local;
                if (weight <= 0.001f || strip.TryLocalTime(_playbackTime, firstInLane, out local) == false)
                    continue;

                foreach (ClipChannel channel in strip.clip.channels)
                {
                    if (channel.target == null || channel.target.enabled == false)
                        continue;
                    StripSample sample;
                    if (MotionStrip.TrySample(channel.keyframes, local, out sample) == false)
                        continue;

                    Contribution existing;
                    bool first = _blended.TryGetValue(channel.target, out existing) == false;

                    // A lone strip at full influence hands the delegate the untouched keyframe pair, which
                    // is what keeps discrete interpolables working: there is no halfway pose to compute.
                    if (first && weight >= 0.999f && strip.blendMode == StripBlendMode.Replace)
                    {
                        _blended[channel.target] = new Contribution { sample = sample, weight = weight, plain = true };
                        continue;
                    }

                    object value = ValueBlend.IsBlendable(sample.left)
                            ? ValueBlend.Lerp(sample.left, sample.right, sample.factor)
                            : null;

                    if (value == null)
                    {
                        // Discrete: the loudest contribution wins outright.
                        if (first || weight > existing.weight)
                            _blended[channel.target] = new Contribution { sample = sample, weight = weight, plain = true };
                        continue;
                    }

                    object under = first
                            ? BaseValueOf(channel.target)
                            : existing.plain
                                    ? ValueBlend.Lerp(existing.sample.left, existing.sample.right, existing.sample.factor)
                                    : existing.value;

                    if (under == null)
                    {
                        // Nothing to blend against, which happens when the target cannot report its
                        // current value. Blending here would hand the delegate a null and it would throw
                        // on every frame, so the strip speaks on its own instead.
                        if (first || weight > existing.weight)
                            _blended[channel.target] = new Contribution { sample = sample, weight = weight, plain = true };
                        continue;
                    }

                    object reference = ReferenceOf(channel);
                    _blended[channel.target] = new Contribution
                    {
                        value = ValueBlend.Blend(under, value, reference, weight, strip.blendMode),
                        weight = Mathf.Max(weight, first ? 0f : existing.weight),
                        plain = false
                    };
                }
            }

            foreach (KeyValuePair<Interpolable, Contribution> pair in _blended)
            {
                Contribution contribution = pair.Value;
                if (contribution.plain)
                    _stripSamples[pair.Key] = contribution.sample;
                else
                    _stripSamples[pair.Key] = new StripSample { left = contribution.value, right = contribution.value, factor = 0f };
            }
        }

        /// <summary>What the interpolable would be doing without any strip: its own keyframes, or the
        /// value it currently holds in the scene when it has none.</summary>
        private object BaseValueOf(Interpolable interpolable)
        {
            StripSample sample;
            if (MotionStrip.TrySample(interpolable.keyframes, _playbackTime, out sample))
                return ValueBlend.Lerp(sample.left, sample.right, sample.factor);
            try
            {
                return interpolable.GetValue();
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>The channel's value at its own first frame, which Add measures its movement from.</summary>
        private static object ReferenceOf(ClipChannel channel)
        {
            return channel.keyframes.Count == 0 ? null : channel.keyframes.Values[0].value;
        }

        private struct Contribution
        {
            public StripSample sample;
            public object value;
            public float weight;
            /// <summary>The keyframe pair is passed through untouched rather than a computed value.</summary>
            public bool plain;
        }

        private readonly Dictionary<Interpolable, Contribution> _blended = new Dictionary<Interpolable, Contribution>();

        /// <summary>
        /// Strips ordered so lower lanes are mixed before the ones stacked on top of them.
        ///
        /// Kept as a list that is re-sorted only when the strips change. Evaluation runs twice a frame,
        /// and a LINQ OrderBy there allocates a sorted buffer and an enumerator every single time.
        /// </summary>
        private readonly List<MotionStrip> _stripsByLane = new List<MotionStrip>();
        /// <summary>
        /// Lane first, then time. Lane alone left the order within a lane to an unstable sort, so which
        /// of two strips on the same lane counted as "later" - and therefore won - was down to chance.
        /// </summary>
        private static readonly Comparison<MotionStrip> _byLane = (a, b) =>
        {
            int byLane = a.lane.CompareTo(b.lane);
            return byLane != 0 ? byLane : a.start.CompareTo(b.start);
        };

        /// <summary>
        /// Rebuilt unconditionally rather than tracked with a dirty flag. Reusing the list means Clear,
        /// AddRange and Sort allocate nothing once it has grown, and there is no invariant to get wrong
        /// the next time something adds a strip or moves one between lanes.
        /// </summary>
        private void RebuildStripOrder()
        {
            _stripsByLane.Clear();
            _stripsByLane.AddRange(_strips);
            _stripsByLane.Sort(_byLane);
        }

        private const int _maxLanes = 4;
        #endregion

        #region Authoring
        /// <summary>
        /// Moves the keyframes of the given tracks into a new clip and drops a strip where they were.
        ///
        /// The keyframes leave the tracks, which is the point: a track and a strip driving the same value
        /// would fight. The tracks themselves stay behind as empty channels so the strip has something to
        /// bind to and so the rows remain visible in the list.
        /// </summary>
        private void PushDownToStrip(List<Interpolable> tracks)
        {
            List<Interpolable> withKeyframes = tracks.Where(t => t.keyframes.Count != 0).ToList();
            if (withKeyframes.Count == 0)
                return;

            float rangeStart = withKeyframes.Min(t => t.keyframes.Keys[0]);
            float rangeEnd = withKeyframes.Max(t => t.keyframes.Keys[t.keyframes.Count - 1]);
            if (rangeEnd <= rangeStart)
                return;

            RecordUndo("Push down to strip");

            var clip = new MotionClip
            {
                name = withKeyframes.Count == 1 ? DisplayName(withKeyframes[0]) : "Clip " + (_strips.Count + 1),
                length = rangeEnd - rangeStart
            };

            var toRemove = new List<KeyValuePair<float, Keyframe>>();
            foreach (Interpolable track in withKeyframes)
            {
                var channel = new ClipChannel(track);
                foreach (KeyValuePair<float, Keyframe> pair in track.keyframes)
                {
                    channel.keyframes.Add(pair.Key - rangeStart, new Keyframe(pair.Value, track));
                    toRemove.Add(pair);
                }
                clip.channels.Add(channel);
            }

            // The tracks have to survive as strip targets, so they are emptied rather than deleted.
            DeleteKeyframes(toRemove, false);

            _strips.Add(new MotionStrip { clip = clip, start = rangeStart });
            UpdateGrid();
        }

        /// <summary>
        /// The same thing for a part of a track rather than all of it: whatever keyframes are selected
        /// become the clip, and only those leave the tracks.
        ///
        /// Blender has no equivalent, its Push Down takes the whole action and you trim the strip
        /// afterwards. Selecting the keyframes first says the same thing in fewer steps, and it is the
        /// obvious move once the dope sheet already knows how to select a range.
        /// </summary>
        private void PushDownSelectionToStrip()
        {
            if (_selectedKeyframes.Count < 2)
            {
                Logger.LogMessage("Select at least two keyframes first: a strip needs something to play.");
                return;
            }

            float rangeStart = float.PositiveInfinity;
            float rangeEnd = float.NegativeInfinity;
            var byTrack = new Dictionary<Interpolable, List<KeyValuePair<float, Keyframe>>>();
            foreach (KeyValuePair<float, Keyframe> pair in _selectedKeyframes)
            {
                List<KeyValuePair<float, Keyframe>> keyframes;
                if (byTrack.TryGetValue(pair.Value.parent, out keyframes) == false)
                {
                    keyframes = new List<KeyValuePair<float, Keyframe>>();
                    byTrack.Add(pair.Value.parent, keyframes);
                }
                keyframes.Add(pair);
                rangeStart = Mathf.Min(rangeStart, pair.Key);
                rangeEnd = Mathf.Max(rangeEnd, pair.Key);
            }
            if (rangeEnd <= rangeStart)
            {
                Logger.LogMessage("Those keyframes are all at the same time, there is no span to make a strip from.");
                return;
            }

            RecordUndo("Push down to strip");
            var clip = new MotionClip
            {
                name = byTrack.Count == 1 ? DisplayName(_selectedKeyframes[0].Value.parent) : "Clip " + (_strips.Count + 1),
                length = rangeEnd - rangeStart
            };

            var toRemove = new List<KeyValuePair<float, Keyframe>>();
            foreach (KeyValuePair<Interpolable, List<KeyValuePair<float, Keyframe>>> pair in byTrack)
            {
                var channel = new ClipChannel(pair.Key);
                foreach (KeyValuePair<float, Keyframe> keyframe in pair.Value)
                {
                    channel.keyframes[keyframe.Key - rangeStart] =
                            new Keyframe(keyframe.Value, pair.Key);
                    toRemove.Add(keyframe);
                }
                clip.channels.Add(channel);
            }

            DeleteKeyframes(toRemove, false); // the tracks stay, they are what the strip drives
            SelectKeyframes(new List<KeyValuePair<float, Keyframe>>());

            _strips.Add(new MotionStrip { clip = clip, start = rangeStart });
            UpdateGrid();
        }

        /// <summary>
        /// Cuts a strip in two at a time, Blender's Y. Both halves keep playing exactly what they played
        /// before, because the cut is expressed as a clip range rather than by copying keyframes.
        /// </summary>
        private void SplitStrip(MotionStrip strip, float time)
        {
            if (strip.clip == null || time <= strip.start + 0.01f || time >= strip.end - 0.01f)
            {
                Logger.LogMessage("Put the cursor inside the strip first.");
                return;
            }
            if (strip.repeat > 1)
            {
                Logger.LogMessage("A repeating strip cannot be split. Set its repeat back to 1, or merge it first.");
                return;
            }

            float local;
            if (strip.TryLocalTime(time, out local) == false)
                return;

            RecordUndo("Split strip");
            float from = strip.effectiveClipStart;
            float to = strip.effectiveClipEnd;

            var second = new MotionStrip
            {
                clip = strip.clip,
                start = time,
                scale = strip.scale,
                enabled = strip.enabled,
                reverse = strip.reverse,
                extrapolation = strip.extrapolation,
                lane = strip.lane,
                influence = strip.influence,
                blendOut = strip.blendOut,
                blendMode = strip.blendMode,
                // Played backwards, the later half of the strip is the earlier part of the clip.
                clipStart = strip.reverse ? from : local,
                clipEnd = strip.reverse ? local : to
            };

            if (strip.reverse)
                strip.clipStart = local;
            else
                strip.clipEnd = local;
            strip.blendOut = 0f; // that fade belonged to the end of the whole strip, which is now the other one

            _strips.Add(second);
            SelectStrip(second, false);
            UpdateGrid();
        }

        private static string DisplayName(Interpolable interpolable)
        {
            return string.IsNullOrEmpty(interpolable.alias) ? TrackName(interpolable) : interpolable.alias;
        }

        /// <summary>
        /// A second placement of the same clip, straight after this one, playing exactly what this one
        /// plays.
        ///
        /// It used to copy the clip, the scale, the repeat count and nothing else, so a strip that had
        /// been trimmed, reversed, faded or set to Add came back as the whole clip at full influence on
        /// lane zero - which is not a duplicate of anything.
        /// </summary>
        private void DuplicateStrip(MotionStrip strip)
        {
            RecordUndo("Duplicate strip");
            var copy = new MotionStrip
            {
                clip = strip.clip, // clips are shared on purpose, that is the whole point of reuse
                start = strip.end,
                scale = strip.scale,
                repeat = strip.repeat,
                enabled = strip.enabled,
                reverse = strip.reverse,
                extrapolation = strip.extrapolation,
                lane = strip.lane,
                influence = strip.influence,
                blendIn = strip.blendIn,
                blendOut = strip.blendOut,
                blendMode = strip.blendMode,
                clipStart = strip.clipStart,
                clipEnd = strip.clipEnd
            };

            // Straight after the original on its own lane if there is room, otherwise the first lane up
            // that has it. Dropping it onto a strip already there would leave two strips overlapping,
            // which the lane rules exist to prevent.
            for (int lane = strip.lane; lane < _maxLanes; ++lane)
            {
                copy.lane = lane;
                if (OverlapsInLane(copy, lane) == false)
                    break;
            }

            _strips.Add(copy);
            SelectStrip(copy, false);
            UpdateGrid();
        }

        /// <summary>The strip currently opened for editing, whose clip is laid out on the tracks.</summary>
        private MotionStrip _tweakStrip;

        /// <summary>
        /// Opens a strip for editing: its clip is laid back out on the tracks at the strip's own time and
        /// speed, so what you edit is what you saw playing. The strip goes quiet meanwhile, otherwise it
        /// and the tracks would drive the same values at once.
        /// </summary>
        private void EnterTweakMode(MotionStrip strip)
        {
            if (_tweakStrip != null)
                ExitTweakMode();
            if (strip.clip == null)
                return;

            RecordUndo("Edit strip");
            _tweakStrip = strip;

            foreach (ClipChannel channel in strip.clip.channels)
            {
                if (channel.target == null)
                    continue;
                foreach (KeyValuePair<float, Keyframe> pair in channel.keyframes)
                {
                    float time = strip.start + pair.Key * strip.scale;
                    if (channel.target.keyframes.ContainsKey(time) == false)
                        channel.target.keyframes.Add(time, new Keyframe(pair.Value, channel.target));
                }
            }

            UpdateInterpolablesView();
            UpdateGrid();
        }

        /// <summary>
        /// Folds the edited keyframes back into the clip and takes them off the tracks again. The clip's
        /// length is re-derived from what is there now, so adding keyframes past the end grows the strip.
        /// </summary>
        private void ExitTweakMode()
        {
            MotionStrip strip = _tweakStrip;
            _tweakStrip = null;
            if (strip == null || strip.clip == null)
                return;

            RecordUndo("Finish editing strip");

            float scale = Mathf.Max(strip.scale, 0.01f);
            float length = 0f;
            var toRemove = new List<KeyValuePair<float, Keyframe>>();

            foreach (ClipChannel channel in strip.clip.channels)
            {
                if (channel.target == null)
                    continue;
                channel.keyframes.Clear();
                foreach (KeyValuePair<float, Keyframe> pair in channel.target.keyframes)
                {
                    float local = (pair.Key - strip.start) / scale;
                    if (local < -0.0001f)
                        continue; // left of the strip, treated as belonging to the track again
                    local = Mathf.Max(local, 0f);
                    if (channel.keyframes.ContainsKey(local) == false)
                        channel.keyframes.Add(local, new Keyframe(pair.Value, channel.target));
                    if (local > length)
                        length = local;
                    toRemove.Add(pair);
                }
            }

            if (length > 0f)
                strip.clip.length = length;
            DeleteKeyframes(toRemove, false);

            UpdateInterpolablesView();
            UpdateGrid();
        }

        /// <summary>
        /// Folds several strips into one clip. Each contributing keyframe is written at the timeline time
        /// it actually plays, repeats expanded and speed applied, so back to back strips join seamlessly.
        ///
        /// Where strips overlap on the same channel the upper lane wins at any shared time. Influence and
        /// the fades are not baked: those are a blend between two sources, and once the sources have
        /// become one clip there is nothing left to blend against.
        /// </summary>
        private void MergeStrips(List<MotionStrip> strips)
        {
            List<MotionStrip> usable = strips.Where(s => s != null && s.clip != null).ToList();
            if (usable.Count < 2)
                return;

            RecordUndo("Merge strips");

            float start = usable.Min(s => s.start);
            float end = usable.Max(s => s.end);
            var clip = new MotionClip { name = "Merged", length = Mathf.Max(end - start, 0.01f) };

            foreach (MotionStrip strip in usable.OrderBy(s => s.lane).ThenBy(s => s.start))
            {
                foreach (ClipChannel source in strip.clip.channels)
                {
                    if (source.target == null)
                        continue;
                    ClipChannel target = clip.channels.FirstOrDefault(c => c.target == source.target);
                    if (target == null)
                    {
                        target = new ClipChannel(source.target);
                        clip.channels.Add(target);
                    }

                    // Inverts the strip's own time mapping, so trimming and reverse come out right
                    // instead of the merged clip silently ignoring them.
                    float from = strip.effectiveClipStart;
                    float span = strip.clipSpan;
                    for (int pass = 0; pass < Mathf.Max(strip.repeat, 1); ++pass)
                    {
                        float passStart = strip.start + pass * strip.singleLength - start;
                        foreach (KeyValuePair<float, Keyframe> pair in source.keyframes)
                        {
                            if (pair.Key < from - 0.0001f || pair.Key > from + span + 0.0001f)
                                continue; // trimmed away

                            float offset = strip.reverse ? from + span - pair.Key : pair.Key - from;
                            float time = passStart + offset * strip.scale;
                            if (time < -0.0001f || time > clip.length + 0.0001f)
                                continue;
                            time = Mathf.Clamp(time, 0f, clip.length);
                            // Indexer rather than Add: a later lane overwrites an earlier one at the
                            // same time instead of throwing on the duplicate key.
                            target.keyframes[time] = new Keyframe(pair.Value, source.target);
                        }
                    }
                }
            }

            foreach (MotionStrip strip in usable)
                _strips.Remove(strip);

            var merged = new MotionStrip { clip = clip, start = start };
            _strips.Add(merged);
            SelectStrip(merged, false);
            UpdateGrid();
        }

        private void RemoveStrip(MotionStrip strip)
        {
            RecordUndo("Delete strip");
            _strips.Remove(strip);
            _selectedStrips.Remove(strip);
            if (_selectedStrips.Count == 0)
                CloseStripWindow();
            UpdateGrid();
        }
        #endregion

        #region Display

        /// <summary>
        /// Where a dragged strip lands. Two strips overlapping in one lane both speak at once and fight
        /// over the same value, so a strip dropped on top of another looks for a free lane, and if there
        /// is none it goes back where it came from.
        /// </summary>
        private void SettleStrip(MotionStrip strip, float previousStart)
        {
            if (OverlapsInLane(strip, strip.lane) == false)
                return;

            for (int lane = 0; lane < _maxLanes; ++lane)
            {
                if (OverlapsInLane(strip, lane))
                    continue;
                strip.lane = lane;
                Logger.LogMessage("Strip moved to lane " + (lane + 1) + ", the one you dropped it on was taken.");
                return;
            }

            strip.start = previousStart;
            Logger.LogMessage("No lane free for that: the strip is back where it was.");
        }

        private bool OverlapsInLane(MotionStrip strip, int lane)
        {
            foreach (MotionStrip other in _strips)
            {
                if (other == strip || other.lane != lane || other.clip == null)
                    continue;
                if (strip.start < other.end - 0.001f && other.start < strip.end - 0.001f)
                    return true;
            }
            return false;
        }

        #endregion

        #region Inspector
        private readonly List<MotionStrip> _selectedStrips = new List<MotionStrip>();

        private void SelectStrip(MotionStrip strip, bool additive)
        {
            if (additive == false)
                _selectedStrips.Clear();
            else
                _selectedStrips.Remove(strip);
            _selectedStrips.Add(strip);

            // A strip has no row of its own in the channel list, so the next best thing is to light up
            // the tracks it drives: you can see at a glance what it covers, and the graph editor, which
            // draws the selected tracks, turns into that strip's curves.
            if (additive == false && strip.clip != null)
            {
                _selectedInterpolables.Clear();
                foreach (ClipChannel channel in strip.clip.channels)
                {
                    if (channel.target != null)
                        _selectedInterpolables.Add(channel.target);
                }
            }
            UpdateStrips();
        }

        private void CloseStripWindow()
        {
            _selectedStrips.Clear();
            UpdateStrips();
        }

        #endregion

        #region Scene data
        private void WriteStrips(XmlTextWriter writer, List<KeyValuePair<int, ObjectCtrlInfo>> dic)
        {
            foreach (MotionStrip strip in _strips)
            {
                if (strip.clip == null || strip.clip.channels.Count == 0)
                    continue;

                writer.WriteStartElement("strip");
                writer.WriteAttributeString("name", strip.clip.name);
                writer.WriteAttributeString("clipLength", XmlConvert.ToString(strip.clip.length));
                writer.WriteAttributeString("start", XmlConvert.ToString(strip.start));
                writer.WriteAttributeString("scale", XmlConvert.ToString(strip.scale));
                writer.WriteAttributeString("repeat", XmlConvert.ToString(strip.repeat));
                writer.WriteAttributeString("enabled", XmlConvert.ToString(strip.enabled));
                writer.WriteAttributeString("extrapolation", strip.extrapolation.ToString());
                writer.WriteAttributeString("lane", XmlConvert.ToString(strip.lane));
                writer.WriteAttributeString("influence", XmlConvert.ToString(strip.influence));
                writer.WriteAttributeString("blendIn", XmlConvert.ToString(strip.blendIn));
                writer.WriteAttributeString("blendOut", XmlConvert.ToString(strip.blendOut));
                writer.WriteAttributeString("blendMode", strip.blendMode.ToString());
                writer.WriteAttributeString("reverse", XmlConvert.ToString(strip.reverse));
                writer.WriteAttributeString("clipStart", XmlConvert.ToString(strip.clipStart));
                writer.WriteAttributeString("clipEnd", XmlConvert.ToString(strip.clipEnd));

                foreach (ClipChannel channel in strip.clip.channels)
                {
                    if (channel.target == null)
                        continue;
                    int objectIndex = -1;
                    if (channel.target.oci != null)
                    {
                        objectIndex = dic.FindIndex(e => e.Value == channel.target.oci);
                        if (objectIndex == -1)
                            continue;
                    }

                    // Same shape as a normal interpolable element, so the existing rebinding attributes
                    // (guide object path, bone path) come along for free.
                    writer.WriteStartElement("interpolable");
                    writer.WriteAttributeString("owner", channel.target.owner);
                    if (objectIndex != -1)
                        writer.WriteAttributeString("objectIndex", XmlConvert.ToString(objectIndex));
                    writer.WriteAttributeString("id", channel.target.id);
                    if (channel.target.writeParameterToXml != null)
                        channel.target.writeParameterToXml(channel.target.oci, writer, channel.target.parameter);

                    foreach (KeyValuePair<float, Keyframe> pair in channel.keyframes)
                    {
                        writer.WriteStartElement("keyframe");
                        writer.WriteAttributeString("time", XmlConvert.ToString(pair.Key));
                        channel.target.WriteValueToXml(writer, pair.Value.value);
                        foreach (UnityEngine.Keyframe curveKey in pair.Value.curve.keys)
                        {
                            writer.WriteStartElement("curveKeyframe");
                            writer.WriteAttributeString("time", XmlConvert.ToString(curveKey.time));
                            writer.WriteAttributeString("value", XmlConvert.ToString(curveKey.value));
                            writer.WriteAttributeString("inTangent", XmlConvert.ToString(curveKey.inTangent));
                            writer.WriteAttributeString("outTangent", XmlConvert.ToString(curveKey.outTangent));
                            writer.WriteEndElement();
                        }
                        writer.WriteEndElement();
                    }
                    writer.WriteEndElement();
                }
                writer.WriteEndElement();
            }
        }

        private void ReadStrips(XmlNode root, List<KeyValuePair<int, ObjectCtrlInfo>> dic)
        {
            foreach (XmlNode stripNode in root.ChildNodes)
            {
                if (stripNode.Name != "strip")
                    continue;
                try
                {
                    var clip = new MotionClip
                    {
                        name = stripNode.Attributes["name"]?.Value ?? "Clip",
                        length = XmlConvert.ToSingle(stripNode.Attributes["clipLength"].Value)
                    };

                    foreach (XmlNode channelNode in stripNode.ChildNodes)
                    {
                        if (channelNode.Name != "interpolable")
                            continue;
                        Interpolable target = ResolveInterpolable(channelNode, dic);
                        if (target == null)
                            continue;

                        var channel = new ClipChannel(target);
                        foreach (XmlNode keyframeNode in channelNode.ChildNodes)
                        {
                            if (keyframeNode.Name != "keyframe")
                                continue;
                            float time = XmlConvert.ToSingle(keyframeNode.Attributes["time"].Value);
                            var curve = new AnimationCurve();
                            foreach (XmlNode curveNode in keyframeNode.ChildNodes)
                            {
                                if (curveNode.Name != "curveKeyframe")
                                    continue;
                                curve.AddKey(new UnityEngine.Keyframe(
                                        XmlConvert.ToSingle(curveNode.Attributes["time"].Value),
                                        XmlConvert.ToSingle(curveNode.Attributes["value"].Value),
                                        XmlConvert.ToSingle(curveNode.Attributes["inTangent"].Value),
                                        XmlConvert.ToSingle(curveNode.Attributes["outTangent"].Value)));
                            }
                            if (channel.keyframes.ContainsKey(time) == false)
                                channel.keyframes.Add(time, new Keyframe(target.ReadValueFromXml(keyframeNode), target, curve));
                        }
                        if (channel.keyframes.Count != 0)
                            clip.channels.Add(channel);
                    }

                    if (clip.channels.Count == 0)
                        continue;

                    var strip = new MotionStrip
                    {
                        clip = clip,
                        start = XmlConvert.ToSingle(stripNode.Attributes["start"].Value),
                        scale = XmlConvert.ToSingle(stripNode.Attributes["scale"].Value),
                        repeat = XmlConvert.ToInt32(stripNode.Attributes["repeat"].Value),
                        enabled = stripNode.Attributes["enabled"] == null || XmlConvert.ToBoolean(stripNode.Attributes["enabled"].Value)
                    };

                    // Layering attributes, all optional so strips written before stage two still load.
                    if (stripNode.Attributes["lane"] != null)
                        strip.lane = Mathf.Clamp(XmlConvert.ToInt32(stripNode.Attributes["lane"].Value), 0, _maxLanes - 1);
                    if (stripNode.Attributes["influence"] != null)
                        strip.influence = XmlConvert.ToSingle(stripNode.Attributes["influence"].Value);
                    if (stripNode.Attributes["blendIn"] != null)
                        strip.blendIn = XmlConvert.ToSingle(stripNode.Attributes["blendIn"].Value);
                    if (stripNode.Attributes["blendOut"] != null)
                        strip.blendOut = XmlConvert.ToSingle(stripNode.Attributes["blendOut"].Value);
                    if (stripNode.Attributes["blendMode"] != null)
                    {
                        try
                        {
                            strip.blendMode = (StripBlendMode)Enum.Parse(typeof(StripBlendMode), stripNode.Attributes["blendMode"].Value);
                        }
                        catch (Exception)
                        {
                            strip.blendMode = StripBlendMode.Replace;
                        }
                    }
                    if (stripNode.Attributes["reverse"] != null)
                        strip.reverse = XmlConvert.ToBoolean(stripNode.Attributes["reverse"].Value);
                    if (stripNode.Attributes["clipStart"] != null)
                        strip.clipStart = XmlConvert.ToSingle(stripNode.Attributes["clipStart"].Value);
                    if (stripNode.Attributes["clipEnd"] != null)
                        strip.clipEnd = XmlConvert.ToSingle(stripNode.Attributes["clipEnd"].Value);

                    if (stripNode.Attributes["extrapolation"] != null)
                    {
                        try
                        {
                            strip.extrapolation = (StripExtrapolation)Enum.Parse(typeof(StripExtrapolation), stripNode.Attributes["extrapolation"].Value);
                        }
                        catch (Exception)
                        {
                            strip.extrapolation = StripExtrapolation.Hold;
                        }
                    }
                    _strips.Add(strip);
                }
                catch (Exception e)
                {
                    Logger.LogError("Couldn't read a strip:\n" + e);
                }
            }
        }

        /// <summary>
        /// Finds the live interpolable an element describes, creating it when the scene does not have it.
        /// A strip target with no keyframes of its own is never written by the tree writer, so on load it
        /// has to be brought back from the strip's own copy of the binding attributes.
        /// </summary>
        private Interpolable ResolveInterpolable(XmlNode node, List<KeyValuePair<int, ObjectCtrlInfo>> dic)
        {
            try
            {
                string ownerId = node.Attributes["owner"]?.Value;
                string id = node.Attributes["id"]?.Value;
                if (ownerId == null || id == null)
                    return null;

                ObjectCtrlInfo oci = null;
                if (node.Attributes["objectIndex"] != null)
                {
                    int objectIndex = XmlConvert.ToInt32(node.Attributes["objectIndex"].Value);
                    if (objectIndex < 0 || objectIndex >= dic.Count)
                        return null;
                    oci = dic[objectIndex].Value;
                }

                InterpolableModel model = _interpolableModelsList.Find(m => m.owner == ownerId && m.id == id);
                if (model == null)
                    return null;

                Interpolable interpolable = model.readParameterFromXml != null
                        ? new Interpolable(oci, model.readParameterFromXml(oci, node), model)
                        : new Interpolable(oci, model);

                Interpolable existing;
                if (_interpolables.TryGetValue(interpolable.GetHashCode(), out existing))
                    return existing;

                _interpolables.Add(interpolable.GetHashCode(), interpolable);
                _interpolablesTree.AddLeaf(interpolable);
                return interpolable;
            }
            catch (Exception e)
            {
                Logger.LogError("Couldn't resolve a strip channel:\n" + e);
                return null;
            }
        }
        #endregion
    }
}
