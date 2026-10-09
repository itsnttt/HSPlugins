using System.Collections.Generic;
using UnityEngine;

namespace TheBirdOfHermes
{
    public class AudioLane
    {
        public List<AudioTrack> Tracks { get; } = new List<AudioTrack>();
        public float Volume { get; set; } = 1f;
        public bool IsMuted { get; set; }
        public string Name { get; set; }
        public bool HasColor { get; set; }
        public Color Color { get; set; } = Color.white;
    }
}