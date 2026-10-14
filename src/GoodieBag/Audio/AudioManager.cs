using System.Runtime.InteropServices;
using GoodieBag.Config;

namespace GoodieBag.Audio;

internal sealed record AudioDevice(string Id, string Name);

internal static class AudioManager
{
    private const uint DEVICE_STATE_ACTIVE = 0x1;
    private const uint STGM_READ = 0x0;

    private const ERole DefaultRole = ERole.Console;

    private static readonly PropertyKey FriendlyNameKey =
        new(new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"), 14);

    public static AudioSnapshot Capture()
    {
        var playback = GetDefault(EDataFlow.Render);
        var recording = GetDefault(EDataFlow.Capture);
        return new AudioSnapshot
        {
            PlaybackId = playback?.Id,
            PlaybackName = playback?.Name,
            RecordingId = recording?.Id,
            RecordingName = recording?.Name,
        };
    }

    public static List<string> Apply(AudioSnapshot audio)
    {
        var missing = new List<string>();
        if (audio.PlaybackId is not null)
            ApplyOne(EDataFlow.Render, audio.PlaybackId, audio.PlaybackName, audio.SetCommunications, "Playback", missing);
        if (audio.RecordingId is not null)
            ApplyOne(EDataFlow.Capture, audio.RecordingId, audio.RecordingName, audio.SetCommunications, "Recording", missing);
        return missing;
    }

    public static async Task ApplyAsync(AudioSnapshot? audio, int? volume, bool displayChanged, List<string> problems)
    {
        if (audio is not null)
        {
            try
            {
                var missing = Apply(audio);
                for (int attempt = 0; missing.Count > 0 && displayChanged && attempt < 8; attempt++)
                {
                    await Task.Delay(500);
                    missing = Apply(audio);
                }
                problems.AddRange(missing.Select(x => $"Audio device not found ({x})"));
            }
            catch (Exception ex)
            {
                problems.Add("Audio: " + ex.Message);
            }
        }
        if (volume is { } v)
        {
            try { SetVolume(v); }
            catch (Exception ex) { problems.Add("Volume: " + ex.Message); }
        }
    }

    public static IEnumerable<string> Changes(AudioSnapshot? audio, int? volume)
    {
        var changes = new List<string>();
        try
        {
            if (audio is not null)
            {
                var playback = GetDefault(EDataFlow.Render);
                var recording = GetDefault(EDataFlow.Capture);
                if (audio.PlaybackId is not null && playback?.Id != audio.PlaybackId)
                    changes.Add($"Speakers: {playback?.Name ?? "none"} → {audio.PlaybackName}");
                if (audio.RecordingId is not null && recording?.Id != audio.RecordingId)
                    changes.Add($"Microphone: {recording?.Name ?? "none"} → {audio.RecordingName}");
            }
            if (volume is { } v && GetVolume() is var now && now != v)
                changes.Add(now is null ? $"Volume → {v}%" : $"Volume: {now}% → {v}%");
        }
        catch
        {
        }
        return changes;
    }

    public static List<AudioDevice> List(EDataFlow flow)
    {
        var result = new List<AudioDevice>();
        var enumerator = CreateEnumerator();
        IMMDeviceCollection? collection = null;
        try
        {
            Check(enumerator.EnumAudioEndpoints(flow, DEVICE_STATE_ACTIVE, out collection));
            Check(collection.GetCount(out uint count));

            for (uint i = 0; i < count; i++)
            {
                if (collection.Item(i, out var device) != 0)
                    continue;
                try
                {
                    if (ToAudioDevice(device) is { } d)
                        result.Add(d);
                }
                finally { Release(device); }
            }
        }
        finally
        {
            Release(collection);
            Release(enumerator);
        }
        return result;
    }

    public static AudioDevice? GetDefault(EDataFlow flow)
    {
        var enumerator = CreateEnumerator();
        try
        {
            if (enumerator.GetDefaultAudioEndpoint(flow, DefaultRole, out var device) != 0)
                return null;
            try { return ToAudioDevice(device); }
            finally { Release(device); }
        }
        finally { Release(enumerator); }
    }

    public static int? GetVolume()
    {
        var volume = DefaultPlaybackVolume();
        if (volume is null)
            return null;
        try { return volume.GetMasterVolumeLevelScalar(out float level) == 0 ? (int)Math.Round(level * 100) : (int?)null; }
        finally { Release(volume); }
    }

    public static void SetVolume(int percent)
    {
        var volume = DefaultPlaybackVolume() ?? throw new InvalidOperationException("No playback device.");
        try
        {
            var context = Guid.Empty;
            Check(volume.SetMasterVolumeLevelScalar(Math.Clamp(percent, 0, 100) / 100f, ref context));
        }
        finally { Release(volume); }
    }

    private static IAudioEndpointVolume? DefaultPlaybackVolume()
    {
        var enumerator = CreateEnumerator();
        IMMDevice? device = null;
        try
        {
            if (enumerator.GetDefaultAudioEndpoint(EDataFlow.Render, DefaultRole, out device) != 0)
                return null;
            var iid = typeof(IAudioEndpointVolume).GUID;
            if (device.Activate(ref iid, 23, IntPtr.Zero, out var instance) != 0)
                return null;
            if (instance is IAudioEndpointVolume volume)
                return volume;
            Release(instance);
            return null;
        }
        finally
        {
            Release(device);
            Release(enumerator);
        }
    }

    private static void ApplyOne(EDataFlow flow, string id, string? name, bool communications, string label, List<string> missing)
    {
        var devices = List(flow);
        var match = devices.FirstOrDefault(d => d.Id == id)
                    ?? devices.FirstOrDefault(d => name is not null && string.Equals(d.Name, name, StringComparison.OrdinalIgnoreCase));

        if (match is null)
        {
            missing.Add($"{label}: {name ?? id}");
            return;
        }

        var policy = (IPolicyConfig)new PolicyConfigClientComObject();
        try
        {
            Check(policy.SetDefaultEndpoint(match.Id, ERole.Console));
            Check(policy.SetDefaultEndpoint(match.Id, ERole.Multimedia));
            if (communications)
                Check(policy.SetDefaultEndpoint(match.Id, ERole.Communications));
        }
        finally { Release(policy); }
    }

    private static IMMDeviceEnumerator CreateEnumerator() => (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();

    private static AudioDevice? ToAudioDevice(IMMDevice device)
    {
        if (device.GetId(out string id) != 0)
            return null;
        return new AudioDevice(id, ReadFriendlyName(device) ?? id);
    }

    private static string? ReadFriendlyName(IMMDevice device)
    {
        if (device.OpenPropertyStore(STGM_READ, out var store) != 0)
            return null;
        try
        {
            var key = FriendlyNameKey;
            if (store.GetValue(ref key, out var value) != 0)
                return null;

            try
            {
                return value.VarType == Ole32.VT_LPWSTR ? Marshal.PtrToStringUni(value.PointerValue) : null;
            }
            finally
            {
                Ole32.PropVariantClear(ref value);
            }
        }
        finally { Release(store); }
    }

    private static void Release(object? com)
    {
        if (com is not null && Marshal.IsComObject(com))
            Marshal.ReleaseComObject(com);
    }

    private static void Check(int hr)
    {
        if (hr < 0)
            Marshal.ThrowExceptionForHR(hr);
    }
}
