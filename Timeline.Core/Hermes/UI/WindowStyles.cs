using System.Collections.Generic;
using UnityEngine;

namespace TheBirdOfHermes.UI
{
    public static class WindowStyles
    {
        public const float HeaderWidth = 150f;
        public const float LaneHeight = 64f;
        public const float RulerHeight = 22f;
        public const float ToolbarHeight = 24f;
        public const float HandleWidth = 6f;
        public const float MinWindowWidth = 600f;
        public const float MinWindowHeight = 200f;

        #region Theming

        public class Theme
        {
            public Color WindowBg;
            public Color ToolbarBg;
            public Color RulerBg;
            public Color RulerText;
            public Color LaneBg;
            public Color LaneBgAlt;
            public Color LaneBgSelected;
            public Color HeaderBg;
            public Color HeaderBgSelected;
            public Color LaneSeparator;
            public Color TrimmedRegion;
            public Color WaveformBg;
            public Color SnapLine;
            public Color Playhead;
            public Color HandleNormal;
            public Color HandleHover;
            public Color HandleDrag;
            public Color DragGhost;
            public Color SelectionBorder;
            public Color HoverBorder;
            public Color TrimmedWaveform;
            public Color FadeHandle;
            public Color TrackNameBg;
            public Color ActiveLaneBorder;
            public Color MenuBg;
            public Color MenuBorder;
            public Color MenuItemHover;
            public Color MenuSeparator;
            public Color LabelText;
            public Color TrackNameText;
            public Color MenuText;
            public Color HintText;
        }

        public static readonly Theme Dark = new Theme
        {
            WindowBg = new Color(0.12f, 0.12f, 0.14f),
            ToolbarBg = new Color(0.18f, 0.18f, 0.22f),
            RulerBg = new Color(0.14f, 0.14f, 0.17f),
            RulerText = new Color(0.7f, 0.7f, 0.7f),
            LaneBg = new Color(0.16f, 0.16f, 0.19f),
            LaneBgAlt = new Color(0.14f, 0.14f, 0.17f),
            LaneBgSelected = new Color(0.22f, 0.22f, 0.30f),
            HeaderBg = new Color(0.13f, 0.13f, 0.16f),
            HeaderBgSelected = new Color(0.18f, 0.18f, 0.26f),
            LaneSeparator = new Color(0.25f, 0.25f, 0.3f, 0.5f),
            TrimmedRegion = new Color(0.3f, 0.3f, 0.3f, 0.3f),
            WaveformBg = new Color(0f, 0f, 0f, 0.3f),
            SnapLine = new Color(1f, 0.8f, 0.2f, 0.8f),
            Playhead = Color.white,
            HandleNormal = new Color(1f, 1f, 1f, 0.4f),
            HandleHover = new Color(1f, 1f, 1f, 0.8f),
            HandleDrag = new Color(1f, 0.9f, 0.3f, 1f),
            DragGhost = new Color(1f, 1f, 1f, 0.15f),
            SelectionBorder = new Color(0.4f, 0.7f, 1f, 0.9f),
            HoverBorder = new Color(1f, 1f, 1f, 0.25f),
            TrimmedWaveform = new Color(1f, 1f, 1f, 0.3f),
            FadeHandle = new Color(1f, 0.85f, 0.3f, 0.9f),
            TrackNameBg = new Color(0f, 0f, 0f, 0.6f),
            ActiveLaneBorder = new Color(1f, 0.85f, 0.3f, 0.9f),
            MenuBg = new Color(0.16f, 0.16f, 0.20f),
            MenuBorder = new Color(0.3f, 0.3f, 0.35f),
            MenuItemHover = new Color(0.3f, 0.5f, 0.8f, 0.5f),
            MenuSeparator = new Color(0.32f, 0.32f, 0.38f),
            LabelText = Color.white,
            TrackNameText = Color.white,
            MenuText = new Color(0.9f, 0.9f, 0.9f),
            HintText = new Color(0.55f, 0.55f, 0.6f),
        };

        public static readonly Theme Light = new Theme
        {
            WindowBg = new Color(0.82f, 0.82f, 0.85f),
            ToolbarBg = new Color(0.72f, 0.72f, 0.76f),
            RulerBg = new Color(0.76f, 0.76f, 0.80f),
            RulerText = new Color(0.20f, 0.20f, 0.24f),
            LaneBg = new Color(0.88f, 0.88f, 0.90f),
            LaneBgAlt = new Color(0.82f, 0.82f, 0.85f),
            LaneBgSelected = new Color(0.72f, 0.80f, 0.95f),
            HeaderBg = new Color(0.78f, 0.78f, 0.82f),
            HeaderBgSelected = new Color(0.70f, 0.76f, 0.92f),
            LaneSeparator = new Color(0.45f, 0.45f, 0.50f, 0.6f),
            TrimmedRegion = new Color(0.2f, 0.2f, 0.2f, 0.25f),
            WaveformBg = new Color(1f, 1f, 1f, 0.35f),
            SnapLine = new Color(0.85f, 0.5f, 0f, 0.9f),
            Playhead = new Color(0.1f, 0.1f, 0.12f),
            HandleNormal = new Color(0.1f, 0.1f, 0.12f, 0.4f),
            HandleHover = new Color(0.1f, 0.1f, 0.12f, 0.8f),
            HandleDrag = new Color(0.9f, 0.55f, 0.05f, 1f),
            DragGhost = new Color(0f, 0f, 0f, 0.12f),
            SelectionBorder = new Color(0.15f, 0.4f, 0.85f, 0.95f),
            HoverBorder = new Color(0.1f, 0.1f, 0.12f, 0.3f),
            TrimmedWaveform = new Color(0f, 0f, 0f, 0.3f),
            FadeHandle = new Color(0.9f, 0.55f, 0.05f, 0.95f),
            TrackNameBg = new Color(1f, 1f, 1f, 0.7f),
            ActiveLaneBorder = new Color(0.9f, 0.55f, 0.05f, 0.95f),
            MenuBg = new Color(0.90f, 0.90f, 0.92f),
            MenuBorder = new Color(0.55f, 0.55f, 0.60f),
            MenuItemHover = new Color(0.45f, 0.6f, 0.9f, 0.55f),
            MenuSeparator = new Color(0.55f, 0.55f, 0.60f),
            LabelText = new Color(0.08f, 0.08f, 0.10f),
            TrackNameText = new Color(0.08f, 0.08f, 0.10f),
            MenuText = new Color(0.1f, 0.1f, 0.12f),
            HintText = new Color(0.35f, 0.35f, 0.40f),
        };
        public static Theme Current { get; private set; } = Dark;
        public static void SetTheme(bool light)
        {
            Current = light ? Light : Dark;
            _labelBold = null;
            _rulerLabel = null;
            _trackNameLabel = null;
            _menuItemLabel = null;
            _hintLabel = null;
        }

        #endregion
        public static Color WindowBg => Current.WindowBg;
        public static Color ToolbarBg => Current.ToolbarBg;
        public static Color RulerBg => Current.RulerBg;
        public static Color RulerText => Current.RulerText;

        public static Color LaneBg => Current.LaneBg;
        public static Color LaneBgAlt => Current.LaneBgAlt;
        public static Color LaneBgSelected => Current.LaneBgSelected;
        public static Color HeaderBg => Current.HeaderBg;
        public static Color HeaderBgSelected => Current.HeaderBgSelected;
        public static Color LaneSeparator => Current.LaneSeparator;

        public static Color TrimmedRegion => Current.TrimmedRegion;
        public static Color WaveformBg => Current.WaveformBg;

        public static Color SnapLine => Current.SnapLine;
        public static Color Playhead => Current.Playhead;
        public static Color HandleNormal => Current.HandleNormal;
        public static Color HandleHover => Current.HandleHover;
        public static Color HandleDrag => Current.HandleDrag;
        public static Color DragGhost => Current.DragGhost;
        public static Color SelectionBorder => Current.SelectionBorder;
        public static Color HoverBorder => Current.HoverBorder;
        public static Color TrimmedWaveform => Current.TrimmedWaveform;
        public static Color FadeHandle => Current.FadeHandle;
        public static Color TrackNameBg => Current.TrackNameBg;
        public static Color ActiveLaneBorder => Current.ActiveLaneBorder;
        public static Color MenuBg => Current.MenuBg;
        public static Color MenuBorder => Current.MenuBorder;
        public static Color MenuItemHover => Current.MenuItemHover;
        public static Color MenuSeparator => Current.MenuSeparator;

        public static readonly Color[] TrackColors =
        {
            new Color(0.3f, 0.6f, 1.0f),
            new Color(0.3f, 0.85f, 0.5f),
            new Color(1.0f, 0.5f, 0.3f),
            new Color(0.85f, 0.3f, 0.6f),
            new Color(0.6f, 0.4f, 1.0f),
            new Color(1.0f, 0.85f, 0.3f),
            new Color(0.3f, 0.85f, 0.85f),
            new Color(1.0f, 0.4f, 0.4f),
        };

        private static readonly Dictionary<int, Texture2D> TexCache = new Dictionary<int, Texture2D>();

        /// Retrieves a texture filled with the specified color. If a texture with the same color
        /// has already been created and cached, it reuses the cached texture. Otherwise, it generates
        /// a new texture, applies the color, and caches it for future use.
        /// <param name="c">The color to fill the texture.</param>
        /// <returns>A Texture2D filled with the specified color.</returns>
        public static Texture2D GetTexture(Color c)
        {
            int key = c.GetHashCode();
            if (!TexCache.TryGetValue(key, out var tex) || tex == null)
            {
                tex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
                tex.SetPixel(0, 0, c);
                tex.Apply();
                tex.hideFlags = HideFlags.DontSave;
                TexCache[key] = tex;
            }
            return tex;
        }

        /// Retrieves a color from the predefined track color palette based on the given index.
        /// The method cycles through the palette if the index exceeds its length.
        /// <param name="index">The index used to retrieve a color from the track color palette.</param>
        /// <returns>A Color from the predefined track color palette corresponding to the given index.</returns>
        public static Color GetTrackColor(int index)
        {
            return TrackColors[index % TrackColors.Length];
        }

        private static GUIStyle _labelBold;
        public static GUIStyle LabelBold => _labelBold ?? (_labelBold = new GUIStyle(GUI.skin.label)
        {
            fontSize = 11,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleLeft,
            normal = { textColor = Current.LabelText },
            padding = new RectOffset(4, 2, 0, 0)
        });

        private static GUIStyle _rulerLabel;
        public static GUIStyle RulerLabel => _rulerLabel ?? (_rulerLabel = new GUIStyle(GUI.skin.label)
        {
            fontSize = 9,
            alignment = TextAnchor.UpperLeft,
            normal = { textColor = RulerText },
            padding = new RectOffset(2, 0, 2, 0)
        });

        private static GUIStyle _trackNameLabel;
        public static GUIStyle TrackNameLabel => _trackNameLabel ?? (_trackNameLabel = new GUIStyle(GUI.skin.label)
        {
            fontSize = 10,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.UpperLeft,
            normal = { textColor = Current.TrackNameText },
            padding = new RectOffset(3, 3, 1, 1)
        });

        private static GUIStyle _progressLabel;
        public static GUIStyle ProgressLabel => _progressLabel ?? (_progressLabel = new GUIStyle(GUI.skin.label)
        {
            fontSize = 10,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.UpperLeft,
            normal = { textColor = Color.white },
            padding = new RectOffset(3, 3, 1, 1)
        });

        private static GUIStyle _menuItemLabel;
        public static GUIStyle MenuItemLabel => _menuItemLabel ?? (_menuItemLabel = new GUIStyle(GUI.skin.label)
        {
            fontSize = 11,
            alignment = TextAnchor.MiddleLeft,
            normal = { textColor = Current.MenuText },
            padding = new RectOffset(2, 2, 0, 0)
        });

        private static GUIStyle _hintLabel;
        public static GUIStyle HintLabel => _hintLabel ?? (_hintLabel = new GUIStyle(GUI.skin.label)
        {
            fontSize = 10,
            alignment = TextAnchor.UpperLeft,
            normal = { textColor = Current.HintText },
            wordWrap = true,
            padding = new RectOffset(2, 2, 0, 0)
        });

        private static GUIStyle _windowStyle;
        public static GUIStyle WindowStyle => _windowStyle ?? (_windowStyle = new GUIStyle(GUI.skin.window)
        {
            padding = new RectOffset(2, 2, 18, 2)
        });

        /// Formats a duration in seconds into a human-readable time string.
        /// If the duration is less than a minute, it is displayed in seconds with two decimal places (e.g., "45.00s").
        /// If the duration is one minute or longer, it is displayed in minutes and seconds (e.g., "2:30.00").
        /// <param name="seconds">The duration in seconds to format. Negative values are treated as zero.</param>
        /// <returns>A string representing the formatted time.</returns>
        public static string FormatTime(float seconds)
        {
            if (seconds < 0) seconds = 0;
            int min = (int)(seconds / 60f);
            float sec = seconds - min * 60f;
            return min > 0 ? $"{min}:{sec:00.00}" : $"{sec:0.00}s";
        }
    }
}