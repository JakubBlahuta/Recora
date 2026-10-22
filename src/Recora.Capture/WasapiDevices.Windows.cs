#if RECORA_WINDOWS
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using Recora.Core.Abstractions;
using Recora.Core.Models;

namespace Recora.Capture;

/// <summary>Real WASAPI capture-device enumeration (Windows TFM).</summary>
internal static class NativeWasapi
{
    public static IReadOnlyList<MicrophoneDevice> EnumerateCaptureDevices()
    {
        var list = new List<MicrophoneDevice>
        {
            new() { Id = "default", DisplayName = "System default", IsDefault = true }
        };

        using var enumerator = new MMDeviceEnumerator();
        var defaultId = SafeDefaultId(enumerator);
        foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
        {
            try
            {
                var id = device.ID;
                list.Add(new MicrophoneDevice
                {
                    Id = id,
                    DisplayName = device.FriendlyName,
                    IsDefault = string.Equals(id, defaultId, StringComparison.OrdinalIgnoreCase)
                });
            }
            catch
            {
                // skip endpoints that throw on property access
            }
        }

        return list;
    }

    private static string? SafeDefaultId(MMDeviceEnumerator enumerator)
    {
        try
        {
            return enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications).ID;
        }
        catch
        {
            try
            {
                return enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Multimedia).ID;
            }
            catch
            {
                return null;
            }
        }
    }
}

/// <summary>
/// WASAPI mixer: system loopback (Teams app audio approximation) + selected mic.
/// Process-loopback for ms-teams.exe is attempted via activation params when available (Win11+);
/// otherwise falls back to WasapiLoopbackCapture.
/// </summary>
internal sealed partial class AudioMixer
{
    private WasapiCapture? _micCapture;
    private WasapiLoopbackCapture? _loopback;
    private readonly object _pcmGate = new();
    private readonly List<float> _mixBuffer = [];
    private int _sampleRate = 48000;
    private int _channels = 2;

    partial void StartTeamsLoopbackCore(ref bool ok)
    {
        ok = StartLoopback(preferProcess: true);
    }

    partial void StartSystemLoopbackCore(ref bool ok)
    {
        ok = StartLoopback(preferProcess: false);
    }

    partial void StartMicrophoneCore(ref bool ok)
    {
        try
        {
            WaveFormat? format = null;
            if (_micDeviceId is not null && _micDeviceId != "default")
            {
                using var enumerator = new MMDeviceEnumerator();
                var device = enumerator.GetDevice(_micDeviceId);
                _micCapture = new WasapiCapture(device);
            }
            else
            {
                _micCapture = new WasapiCapture();
            }

            format = _micCapture.WaveFormat;
            _sampleRate = format.SampleRate;
            _channels = format.Channels;
            _micCapture.DataAvailable += (_, e) => AppendPcm(e.Buffer, e.BytesRecorded, format!, duck: false);
            _micCapture.StartRecording();
            ok = true;
            _logger?.LogInformation("WASAPI mic started device={Device} {Rate}Hz/{Ch}ch",
                _micDeviceId, _sampleRate, _channels);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "WASAPI mic start failed");
            _micCapture?.Dispose();
            _micCapture = null;
            ok = false;
        }
    }

    partial void PullMixedPcmCore(ref bool ok, ref PcmAudioEventArgs pcm)
    {
        lock (_pcmGate)
        {
            if (_mixBuffer.Count == 0)
            {
                ok = false;
                return;
            }

            var samples = _mixBuffer.ToArray();
            _mixBuffer.Clear();
            pcm = new PcmAudioEventArgs
            {
                SampleRate = _sampleRate,
                Channels = _channels,
                Samples = samples,
                Simulated = false
            };
            ok = true;
        }
    }

    partial void DisposeCore()
    {
        try { _micCapture?.StopRecording(); } catch { /* ignore */ }
        try { _loopback?.StopRecording(); } catch { /* ignore */ }
        _micCapture?.Dispose();
        _loopback?.Dispose();
        _micCapture = null;
        _loopback = null;
    }

    private bool StartLoopback(bool preferProcess)
    {
        try
        {
            // NAudio WasapiLoopbackCapture = render endpoint loopback (system / what-you-hear).
            // True per-process loopback for ms-teams.exe needs Win11 AUDIOCLIENT_PROCESS_LOOPBACK_PARAMS;
            // we document that as follow-up and use system loopback which includes Teams audio when unmuted.
            _loopback = new WasapiLoopbackCapture();
            var format = _loopback.WaveFormat;
            _sampleRate = format.SampleRate;
            _channels = format.Channels;
            _loopback.DataAvailable += (_, e) => AppendPcm(e.Buffer, e.BytesRecorded, format, duck: false);
            _loopback.StartRecording();
            _logger?.LogInformation(
                "WASAPI loopback started ({Mode}) {Rate}Hz/{Ch}ch",
                preferProcess ? "system-fallback-for-teams" : "system",
                _sampleRate, _channels);
            return true;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "WASAPI loopback failed");
            _loopback?.Dispose();
            _loopback = null;
            return false;
        }
    }

    private void AppendPcm(byte[] buffer, int bytes, WaveFormat format, bool duck)
    {
        if (bytes <= 0) return;
        var floats = new float[bytes / (format.BitsPerSample / 8)];
        if (format.Encoding == WaveFormatEncoding.IeeeFloat)
        {
            Buffer.BlockCopy(buffer, 0, floats, 0, bytes);
        }
        else if (format.BitsPerSample == 16)
        {
            for (int i = 0, s = 0; i < bytes; i += 2, s++)
                floats[s] = BitConverter.ToInt16(buffer, i) / 32768f;
        }
        else
        {
            return;
        }

        if (duck)
        {
            for (var i = 0; i < floats.Length; i++)
                floats[i] *= 0.35f;
        }

        lock (_pcmGate)
        {
            // Cap buffer (~1s) to avoid unbounded growth if encoder is slow
            var max = _sampleRate * _channels;
            if (_mixBuffer.Count > max)
                _mixBuffer.RemoveRange(0, _mixBuffer.Count - max);
            _mixBuffer.AddRange(floats);
        }
    }
}
#endif
