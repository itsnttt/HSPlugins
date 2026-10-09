using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx.Configuration;
using BepInEx.Logging;
using KKAPI.Studio.SaveLoad;
using KKAPI.Utilities;
using TheBirdOfHermes.Audio;
using TheBirdOfHermes.UI;
using TheBirdOfHermes.Undo;
using UnityEngine;

namespace TheBirdOfHermes
{
    /// <summary>
    /// The Bird of Hermes by Fox (rnetiks), merged into Timeline with its author's permission: audio on
    /// lanes that play along with the timeline. It used to be a plugin of its own; now Timeline starts it,
    /// it follows Timeline's clock directly, and its window opens from Timeline's ⋯ menu.
    /// </summary>
    public class HermesDAW : MonoBehaviour
    {
        /// <summary>The id its scene data has always been saved under, kept so scenes made with it still load.</summary>
        public const string GUID = "org.fox.thebirdofhermes";
        public static ManualLogSource Logger { get; private set; }
        public static HermesDAW Instance { get; private set; }

        private TrackManager _trackManager;
        private AudioWindow _audioWindow;
        private UndoManager _undoManager;

        private bool _timelineWasShown;

        private static ConfigEntry<bool> _lightSkin;

        public TrackManager TrackManager => _trackManager;

        public bool WindowOpen
        {
            get { return _audioWindow != null && _audioWindow.IsOpen; }
            set
            {
                if (_audioWindow != null)
                    _audioWindow.IsOpen = value;
            }
        }

        /// <summary>Called by Timeline as it wakes, so the scene data handler is in place before any scene loads.</summary>
        internal static void Init(MonoBehaviour host, ConfigFile config, ManualLogSource logger)
        {
            Logger = logger;
            _lightSkin = config.Bind("Audio", "Light skin", false, "Use the lighter skin for the audio window.");
            WindowStyles.SetTheme(_lightSkin.Value);
            _lightSkin.SettingChanged += (s, e) => WindowStyles.SetTheme(_lightSkin.Value);
            Instance = host.gameObject.AddComponent<HermesDAW>();
        }

        private void Awake()
        {
            _trackManager = new TrackManager(this);
            _undoManager = new UndoManager();
            _audioWindow = new AudioWindow(_trackManager, _undoManager);
            _audioWindow.SetPlaybackTimeGetter(() => Timeline.Timeline.AudioClockTime);
            _audioWindow.SetIsPlayingGetter(() => Timeline.Timeline.AudioClockPlaying);
            _audioWindow.OnSeekEvent += time =>
            {
                Timeline.Timeline.AudioSeek(time);
                _trackManager.SeekAll(time);
            };

            SceneController.Plugin = this;
            StudioSaveLoadApi.RegisterExtraBehaviour<SceneController>(GUID);
        }

        private void OnDestroy()
        {
            _trackManager?.ClearAll();
        }

        private void Update()
        {
            _trackManager.PollAsyncOperations();
            VideoExportBridge.TryRegister(this);

            if (_trackManager.HasAudio)
                _trackManager.SyncAllPlayback(Timeline.Timeline.AudioClockTime, Timeline.Timeline.AudioClockPlaying);
        }

        private void OnGUI()
        {
            // The audio window goes when the Timeline window does, as it did beside the old one.
            bool shown = Timeline.Timeline.WindowShown;
            if (_timelineWasShown && shown == false)
                _audioWindow.IsOpen = false;
            _timelineWasShown = shown;
            if (_audioWindow.IsOpen == false)
                return;

            // Its keys only count while the mouse is over it, as Timeline's own do over the Timeline window.
            if (_audioWindow._windowRect.Contains(Event.current.mousePosition))
            {
                HandleUndoRedoInput();
                HandleKeyboardShortcuts();
            }

            GUI.depth = 1000;
            _audioWindow.Draw();
        }

        /// <summary>Hermes' Sync Time: the scene ends where the last audio ends.</summary>
        public void FitSceneLength()
        {
            float maxEnd = 0f;
            foreach (var track in _trackManager.AllTracks)
            {
                if (track.TimelineEnd > maxEnd)
                    maxEnd = track.TimelineEnd;
            }
            Timeline.Timeline.FitLengthToAudio(maxEnd);
        }

        /// <summary>Saves each track as it plays, trimmed and filtered, to a WAV file the user picks.</summary>
        public void SaveTracks(List<AudioTrack> tracks)
        {
            foreach (var track in tracks)
            {
                string suggestedName = Path.GetFileName(track.FileName);
                if (string.IsNullOrEmpty(Path.GetExtension(suggestedName)))
                    suggestedName += ".wav";
                string result;
                var success = SystemFileDialog.ShowDialog("Save audio - " + track.Name, suggestedName,
                    out result,
                    SystemFileDialog.FOS.OVERWRITEPROMPT, "WAV Audio|*.wav");
                if (string.IsNullOrEmpty(result) || !success) break;
                try
                {
                    byte[] baked = track.GetBakedWavBytes();
                    if (baked != null)
                        File.WriteAllBytes(result, baked);
                }
                catch (Exception ex)
                {
                    Logger.LogError($"Failed to save \"{track.Name}\": {ex.Message}");
                }
            }
        }

        /// <summary>Asks for audio files and puts them at the given time, on the given lane when there is one.</summary>
        public List<AudioTrack> AddFiles(float time, AudioLane lane)
        {
            var added = new List<AudioTrack>();
            string[] selection = OpenFileDialog.ShowDialog("Add audio", "", AudioLoader.GetFileFilter(), "",
                OpenFileDialog.OpenSaveFileDialgueFlags.OFN_FILEMUSTEXIST);
            if (selection == null)
                return added;
            foreach (string path in selection)
            {
                if (string.IsNullOrEmpty(path))
                    continue;
                try
                {
                    AudioTrack track = _trackManager.AddFileAtCursor(path, time, lane);
                    if (track != null)
                        added.Add(track);
                }
                catch (Exception e)
                {
                    Logger.LogError($"Failed to load audio: {e}");
                }
            }
            return added;
        }

        private void HandleUndoRedoInput()
        {
            Event e = Event.current;
            if (e.type != EventType.KeyDown) return;
            if (!e.control) return;

            if (e.keyCode == KeyCode.Z)
            {
                _undoManager.PerformUndo();
                e.Use();
            }
            else if (e.keyCode == KeyCode.Y)
            {
                _undoManager.PerformRedo();
                e.Use();
            }
        }

        private void HandleKeyboardShortcuts()
        {
            Event e = Event.current;
            if (e.type != EventType.KeyDown) return;

            bool ctrl = e.control || e.command;

            switch (ctrl)
            {
                case true when e.keyCode == KeyCode.S:
                {
                    SaveTracks(_trackManager.SelectedTracks.ToList());
                    e.Use();
                    return;
                }
                case true when e.keyCode == KeyCode.J:
                {
                    if (_trackManager.SelectedTracks.Count >= 2)
                    {
                        var selected = _trackManager.SelectedTracks.ToList();
                        var removeCmds = selected.Select(t => new RemoveTrackCommand(_trackManager, t)).ToList();
                        var newTrack = _trackManager.JoinTracks(selected);
                        if (newTrack != null)
                        {
                            var addCmd = new AddTrackCommand(_trackManager, newTrack);
                            var allCmds = new List<IUndoCommand>(removeCmds.ToArray()) { addCmd };
                            _undoManager?.Push(new CompositeUndoCommand("Join Tracks", allCmds));
                        }
                    }

                    e.Use();
                    return;
                }
                case true when e.keyCode == KeyCode.C:
                {
                    if (_trackManager.SelectedTracks.Count > 0)
                    {
                        TrackClipboard.Copy(_trackManager.SelectedTracks);
                        Logger.LogInfo(
                            $"Copied {_trackManager.SelectedTracks.Count} track(s) to clipboard (saved to temp folder)");
                    }

                    e.Use();
                    return;
                }
                case true when e.keyCode == KeyCode.V:
                {
                    if (TrackClipboard.HasData)
                    {
                        float pos = _audioWindow.PlaybackTime;
                        var targetLane = _trackManager.ActiveLane;
                        foreach (var item in TrackClipboard.Data)
                        {
                            var track = _trackManager.PasteTrackAt(item.BakedWavBytes, item.Name, pos, targetLane);
                            if (track != null)
                                _undoManager?.Push(new AddTrackCommand(_trackManager, track));
                        }
                    }

                    e.Use();
                    return;
                }
                case true when e.keyCode == KeyCode.X:
                {
                    if (_trackManager.SelectedTracks.Count > 0)
                    {
                        TrackClipboard.Copy(_trackManager.SelectedTracks);
                        var selected = _trackManager.SelectedTracks.ToList();
                        var cmds = selected.Select(t => new RemoveTrackCommand(_trackManager, t)).Cast<IUndoCommand>()
                            .ToList();
                        if (cmds.Count > 0)
                            _undoManager?.Push(new CompositeUndoCommand("Cut Tracks", cmds));
                        _trackManager.RemoveSelectedTracks();
                    }

                    e.Use();
                    return;
                }
                case false when !e.alt && !e.shift && e.keyCode == KeyCode.S:
                {
                    if (_trackManager.SelectedTracks.Count == 1)
                    {
                        var track = _trackManager.PrimarySelectedTrack;
                        if (track != null && !track.IsBusy && track.HasAudio)
                        {
                            float cursorTime = _audioWindow.PlaybackTime;
                            var removeCmd = new RemoveTrackCommand(_trackManager, track);
                            var newTracks = _trackManager.SplitTrack(track, cursorTime);
                            if (newTracks != null && newTracks.Length == 2)
                            {
                                var addCmdA = new AddTrackCommand(_trackManager, newTracks[0]);
                                var addCmdB = new AddTrackCommand(_trackManager, newTracks[1]);
                                _undoManager?.Push(new CompositeUndoCommand("Split Track",
                                    new IUndoCommand[] { removeCmd, addCmdA, addCmdB }));
                            }
                        }
                    }

                    e.Use();
                    break;
                }
            }
        }
    }
}
