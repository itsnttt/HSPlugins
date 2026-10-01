using System;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using TheBirdOfHermes.Audio;
using UnityEngine;

namespace TheBirdOfHermes
{
    /// <summary>
    /// Hands the scene's audio to VideoExport, when it is installed and has its audio API.
    ///
    /// Nothing in Timeline names a VideoExport type: a type implementing one would fail to load whenever
    /// VideoExport is missing or older, and every plugin that lists Timeline's types (HarmonyX does, for
    /// lookups by name) would get a ReflectionTypeLoadException. So the provider is made here at run time,
    /// once VideoExport's IAudioPlugin is known to exist, and VideoExport is called through reflection.
    /// </summary>
    internal static class VideoExportBridge
    {
        private const string VideoExportGuid = "com.joan6694.illusionplugins.videoexport";
        private static bool _attempted;

        public static void TryRegister(HermesDAW plugin)
        {
            if (_attempted) return;
            _attempted = true;

            BepInEx.PluginInfo info;
            if (!BepInEx.Bootstrap.Chainloader.PluginInfos.TryGetValue(VideoExportGuid, out info) ||
                info.Instance == null)
                return;

            // Builds of VideoExport from before its audio API share its version number, so the API itself is
            // looked for: without it, videos simply leave the scene's audio out, which is not an error.
            Type iface = info.Instance.GetType().Assembly.GetType("VideoExport.AudioPlugins.IAudioPlugin");
            MethodInfo add = info.Instance.GetType().GetMethod("AddAudioPlugin");
            if (iface == null || add == null || add.IsGenericMethodDefinition == false)
            {
                HermesDAW.Logger.LogInfo("This VideoExport has no audio support, so exported videos leave out the scene's audio. A newer VideoExport adds it.");
                return;
            }

            try
            {
                Type providerType = MakeProviderType(iface);
                object provider = Activator.CreateInstance(providerType);
                object[] args = { provider, null };
                bool ok = (bool)add.MakeGenericMethod(providerType).Invoke(info.Instance, args);
                if (ok)
                    HermesDAW.Logger.LogInfo("Registered with VideoExport as an audio provider.");
                else
                    HermesDAW.Logger.LogWarning("VideoExport rejected the Hermes audio provider: " + args[1]);
            }
            catch (Exception e)
            {
                HermesDAW.Logger.LogWarning("VideoExport audio registration failed, so exported videos leave out the scene's audio: " + e);
            }
        }

        /// <summary>
        /// A class implementing VideoExport's IAudioPlugin: its Name and SafeName are constants, and its
        /// MakeAudioStream calls <see cref="HermesVideoAudio.MakeAudioStream"/>.
        /// </summary>
        private static Type MakeProviderType(Type iface)
        {
            AssemblyBuilder assembly = AppDomain.CurrentDomain.DefineDynamicAssembly(new AssemblyName("TimelineHermesVideoAudio"), AssemblyBuilderAccess.Run);
            ModuleBuilder module = assembly.DefineDynamicModule("TimelineHermesVideoAudio");
            TypeBuilder type = module.DefineType("TheBirdOfHermes.HermesAudioProvider", TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.Class, typeof(object), new[] { iface });
            type.DefineDefaultConstructor(MethodAttributes.Public);
            const MethodAttributes implementation = MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.Final | MethodAttributes.HideBySig | MethodAttributes.NewSlot;

            foreach (string[] property in new[] { new[] { "Name", "The Bird of Hermes" }, new[] { "SafeName", "TheBirdOfHermes" } })
            {
                MethodBuilder getter = type.DefineMethod("get_" + property[0], implementation | MethodAttributes.SpecialName, typeof(string), Type.EmptyTypes);
                ILGenerator il = getter.GetILGenerator();
                il.Emit(OpCodes.Ldstr, property[1]);
                il.Emit(OpCodes.Ret);
                type.DefineMethodOverride(getter, iface.GetProperty(property[0]).GetGetMethod());
            }

            MethodInfo target = iface.GetMethod("MakeAudioStream");
            MethodBuilder make = type.DefineMethod("MakeAudioStream", implementation, typeof(BinaryReader), new[] { typeof(float), typeof(float), typeof(int) });
            ILGenerator body = make.GetILGenerator();
            body.Emit(OpCodes.Ldarg_1);
            body.Emit(OpCodes.Ldarg_2);
            body.Emit(OpCodes.Ldarg_3);
            body.Emit(OpCodes.Call, typeof(HermesVideoAudio).GetMethod("MakeAudioStream"));
            body.Emit(OpCodes.Ret);
            type.DefineMethodOverride(make, target);

            return type.CreateType();
        }
    }

    /// <summary>What the provider made at run time calls: the scene's audio mixed down for the video's span.</summary>
    public static class HermesVideoAudio
    {
        public static BinaryReader MakeAudioStream(float startTime, float duration, int sampleRate)
        {
            var manager = HermesDAW.Instance != null ? HermesDAW.Instance.TrackManager : null;
            if (manager == null || !manager.HasAudio || duration <= 0f || sampleRate <= 0)
                return null;

            byte[] wav = HermesMixdown.RenderWav(manager, startTime, duration, sampleRate);
            return wav != null ? new BinaryReader(new MemoryStream(wav, false)) : null;
        }
    }

    internal static class HermesMixdown
    {
        public static byte[] RenderWav(TrackManager manager, float startTime, float duration, int sampleRate)
        {
            int frames = Mathf.CeilToInt(duration * sampleRate);
            if (frames <= 0) return null;

            const int channels = 2;
            var mix = new float[frames * channels];
            bool any = false;

            float master = manager.MasterVolume;
            foreach (var lane in manager.Lanes)
            {
                if (lane.IsMuted) continue;
                float laneGain = lane.Volume * master;
                if (laneGain <= 0f) continue;

                foreach (var track in lane.Tracks)
                {
                    if (track == null || track.IsBusy || !track.HasAudio) continue;
                    var data = track.Audio.Data;
                    if (data == null || data.Samples == null || data.Samples.Length == 0) continue;
                    if (MixTrack(track, data, laneGain, startTime, sampleRate, frames, mix))
                        any = true;
                }
            }

            if (!any) return null;

            for (int i = 0; i < mix.Length; i++)
            {
                if (mix[i] > 1f) mix[i] = 1f;
                else if (mix[i] < -1f) mix[i] = -1f;
            }

            return new AudioData { Channels = channels, SampleRate = sampleRate, Samples = mix }.EncodeWav();
        }

        private static bool MixTrack(AudioTrack track, AudioData data, float gain,
            float startTime, int outRate, int outFrames, float[] mix)
        {
            float audStart = track.AudibleStart;
            float audEnd = track.AudibleEnd;

            int f0 = Mathf.Max(0, Mathf.CeilToInt((audStart - startTime) * outRate));
            int f1 = Mathf.Min(outFrames, Mathf.FloorToInt((audEnd - startTime) * outRate));
            if (f1 <= f0) return false;

            int srcCh = Mathf.Max(1, data.Channels);
            int srcRate = data.SampleRate;
            int srcFrames = data.Samples.Length / srcCh;
            var src = data.Samples;

            float effDur = track.EffectiveDuration;
            float fadeIn = track.FadeInDuration;
            float fadeOut = track.FadeOutDuration;
            float fadeOutStart = effDur - fadeOut;

            for (int f = f0; f < f1; f++)
            {
                float t = startTime + (float)f / outRate;
                float srcPos = (t - track.Offset) * srcRate;
                int i0 = (int)srcPos;
                if (i0 < 0 || i0 >= srcFrames) continue;
                int i1 = i0 + 1 < srcFrames ? i0 + 1 : i0;
                float frac = srcPos - i0;

                float pos = t - audStart;
                float fade = 1f;
                if (fadeIn > 0f && pos < fadeIn)
                    fade = pos / fadeIn;
                if (fadeOut > 0f && pos > fadeOutStart)
                    fade = Mathf.Min(fade, (effDur - pos) / fadeOut);
                if (fade <= 0f) continue;
                float g = gain * fade;

                int b0 = i0 * srcCh, b1 = i1 * srcCh;
                float l, r;
                if (srcCh >= 2)
                {
                    l = src[b0] + (src[b1] - src[b0]) * frac;
                    r = src[b0 + 1] + (src[b1 + 1] - src[b0 + 1]) * frac;
                }
                else
                {
                    l = r = src[b0] + (src[b1] - src[b0]) * frac;
                }

                int o = f * 2;
                mix[o] += l * g;
                mix[o + 1] += r * g;
            }

            return true;
        }
    }
}