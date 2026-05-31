using NAudio.CoreAudioApi;

namespace VoiceToText.Audio;

/// <summary>Enumerates microphone / capture devices via WASAPI.</summary>
public static class AudioDevices
{
    public static IReadOnlyList<AudioInputDevice> GetInputDevices()
    {
        var result = new List<AudioInputDevice>();
        using var enumerator = new MMDeviceEnumerator();
        foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
        {
            using (device)
            {
                result.Add(new AudioInputDevice(device.ID, device.FriendlyName));
            }
        }
        return result;
    }

    /// <summary>The id of the default capture device, or null if none is present.</summary>
    public static string? GetDefaultInputDeviceId()
    {
        using var enumerator = new MMDeviceEnumerator();
        if (!enumerator.HasDefaultAudioEndpoint(DataFlow.Capture, Role.Communications))
            return null;
        using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);
        return device.ID;
    }
}
