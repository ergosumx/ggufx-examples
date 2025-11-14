namespace WhisperCapture;

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ErgoX.GgufX;
using NAudio.CoreAudioApi;
using NAudio.Wave;

/// <summary>
/// Entry point for the whisper_capture utility that records audio from a selected device into a WAV file.
/// </summary>
internal static class Program
{
    private const string DefaultFilePrefix = "capture";
    private const int RenderDeviceIdBase = 1_000_000;
    private const int CaptureDeviceIdBase = 2_000_000;

    private enum CaptureDeviceKind
    {
        Input,
        Output,
    }

    private sealed record CaptureDevice(int DeviceId, string Name, CaptureDeviceKind Kind, int SampleRate, int Channels, string EndpointId);

    /// <summary>
    /// Executes the capture workflow.
    /// </summary>
    /// <param name="args">Command line arguments.</param>
    /// <returns>Zero on success, non-zero otherwise.</returns>
    public static async Task<int> Main(string[] args)
    {
        try
        {
            var projectRoot = ResolveProjectRoot();
            ConfigureRuntime(projectRoot);

            var options = CaptureOptions.Parse(args);
            var devices = EnumerateDevices();

            if (options.ListDevices)
            {
                PrintDevices(devices);
                return 0;
            }

            if (devices.Count == 0)
            {
                await Console.Error.WriteLineAsync("No audio devices were reported by the Windows audio stack.").ConfigureAwait(false);
                return 1;
            }

            var selectedDevice = ResolveDevice(devices, options.DeviceId);
            if (selectedDevice is null)
            {
                await Console.Error.WriteLineAsync("No audio device was selected. Use --list-devices to inspect available identifiers.").ConfigureAwait(false);
                return 1;
            }

            var outputPath = ResolveOutputPath(options.OutputPath);
            using var session = AudioCaptureSession.Create(selectedDevice, outputPath);

            Console.WriteLine($"Recording from: [{selectedDevice.DeviceId}] {selectedDevice.Name}");
            Console.WriteLine($"Device kind: {selectedDevice.Kind} | Format: {session.Format.SampleRate} Hz, {session.Format.Channels} channel(s)");
            Console.WriteLine($"Output file: {outputPath}");
            Console.WriteLine("Press ENTER to stop recording. Use CTRL+C to cancel.");

            using var completionSource = new CaptureCompletionSource(options.Duration);
            SetupCancellationHandlers(completionSource);

            session.Start();

            await completionSource.WaitAsync().ConfigureAwait(false);

            session.Stop();
            await session.Completion.ConfigureAwait(false);

            Console.WriteLine("Capture completed successfully.");
            return 0;
        }
        catch (OperationCanceledException)
        {
            await Console.Error.WriteLineAsync("Capture cancelled.").ConfigureAwait(false);
            return 1;
        }
        catch (GgufxNativeException ex)
        {
            await Console.Error.WriteLineAsync($"Native runtime failure: {ex.StatusCode} | {ex.NativeMessage}").ConfigureAwait(false);
            return 1;
        }
        catch (ArgumentException ex)
        {
            await Console.Error.WriteLineAsync(ex.Message).ConfigureAwait(false);
            return 1;
        }
        catch (InvalidOperationException ex)
        {
            await Console.Error.WriteLineAsync(ex.Message).ConfigureAwait(false);
            return 1;
        }
    }

    /// <summary>
    /// Configures the GGUFx runtime to load native dependencies from the repository.
    /// </summary>
    /// <param name="projectRoot">Absolute path to the project root folder.</param>
    private static void ConfigureRuntime(string projectRoot)
    {
        var repositoryRoot = Path.GetFullPath(Path.Combine(projectRoot, "..", ".."));
        var runtimeDirectory = Path.Combine(repositoryRoot, "src", "ErgoX.GgufX", "runtimes", "win-x64");

        if (!Directory.Exists(runtimeDirectory))
        {
            throw new DirectoryNotFoundException($"Runtime directory not found: {runtimeDirectory}");
        }

        GgufxRuntime.Configure(new GgufxRuntimeOptions
        {
            NativeRootDirectory = runtimeDirectory,
        });
    }

    /// <summary>
    /// Resolves the full path of the project directory by ascending until the csproj is located.
    /// </summary>
    /// <returns>Absolute path to the project folder.</returns>
    private static string ResolveProjectRoot()
    {
        var directory = Path.GetFullPath(AppContext.BaseDirectory);

        while (!string.IsNullOrWhiteSpace(directory))
        {
            var candidate = Path.Combine(directory, "whisper_capture.csproj");
            if (File.Exists(candidate))
            {
                return directory;
            }

            directory = Directory.GetParent(directory)?.FullName ?? string.Empty;
        }

        throw new InvalidOperationException("Unable to locate whisper_capture.csproj.");
    }

    /// <summary>
    /// Enumerates active playback and capture endpoints exposed by Windows.
    /// </summary>
    /// <returns>A combined collection of render (loopback) and capture endpoints.</returns>
    private static IReadOnlyList<CaptureDevice> EnumerateDevices()
    {
        using var enumerator = new MMDeviceEnumerator();
        var results = new List<CaptureDevice>();

        var renderDevices = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
        for (var index = 0; index < renderDevices.Count; index++)
        {
            using var device = renderDevices[index];
            var format = device.AudioClient.MixFormat;
            results.Add(new CaptureDevice(RenderDeviceIdBase + index, device.FriendlyName, CaptureDeviceKind.Output, format.SampleRate, format.Channels, device.ID));
        }

        var captureDevices = enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active);
        for (var index = 0; index < captureDevices.Count; index++)
        {
            using var device = captureDevices[index];
            var format = device.AudioClient.MixFormat;
            results.Add(new CaptureDevice(CaptureDeviceIdBase + index, device.FriendlyName, CaptureDeviceKind.Input, format.SampleRate, format.Channels, device.ID));
        }

        return results;
    }

    /// <summary>
    /// Prints the devices discovered by the native runtime.
    /// </summary>
    /// <param name="devices">Devices reported by ggufx-asr.</param>
    private static void PrintDevices(IReadOnlyList<CaptureDevice> devices)
    {
        Console.WriteLine("Devices exposed by Windows:");
        foreach (var device in devices.OrderBy(d => d.DeviceId))
        {
            Console.WriteLine($"  [{device.DeviceId}] {device.Name} | {device.Kind} | {device.SampleRate} Hz | {device.Channels} ch");
        }
    }

    /// <summary>
    /// Resolves the target device based on the supplied identifier or defaults to the first available input.
    /// </summary>
    /// <param name="devices">Available devices.</param>
    /// <param name="requestedId">Optional requested device identifier.</param>
    /// <returns>The selected device when found; otherwise <c>null</c>.</returns>
    private static CaptureDevice? ResolveDevice(IReadOnlyList<CaptureDevice> devices, int? requestedId)
    {
        if (requestedId.HasValue)
        {
            foreach (var device in devices)
            {
                if (device.DeviceId == requestedId.Value)
                {
                    return device;
                }
            }

            Console.Error.WriteLine($"Requested device id {requestedId.Value} was not found.");
            return null;
        }

        foreach (var device in devices)
        {
            if (device.Kind == CaptureDeviceKind.Input)
            {
                return device;
            }
        }

        return devices.Count > 0 ? devices[0] : null;
    }

    /// <summary>
    /// Resolves the capture output path, generating a timestamped filename when none is supplied.
    /// </summary>
    /// <param name="providedPath">Optional user supplied path.</param>
    /// <returns>The absolute output path.</returns>
    private static string ResolveOutputPath(string? providedPath)
    {
        if (!string.IsNullOrWhiteSpace(providedPath))
        {
            var explicitPath = Path.GetFullPath(providedPath);
            var directory = Path.GetDirectoryName(explicitPath);
            if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            return explicitPath;
        }

        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
        var fileName = $"{DefaultFilePrefix}-{timestamp}.wav";
        return Path.Combine(Environment.CurrentDirectory, fileName);
    }

    /// <summary>
    /// Sets up console-based cancellation handlers.
    /// </summary>
    /// <param name="completionSource">The completion source coordinating shutdown.</param>
    private static void SetupCancellationHandlers(CaptureCompletionSource completionSource)
    {
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            completionSource.TryComplete();
        };

        if (Console.IsInputRedirected)
        {
            return;
        }

        _ = Task.Run(() =>
        {
            Console.ReadLine();
            completionSource.TryComplete();
        }, CancellationToken.None);
    }

    /// <summary>
    /// Represents the parsed capture options.
    /// </summary>
    private sealed class CaptureOptions
    {
        private CaptureOptions()
        {
        }

        /// <summary>
        /// Gets a value indicating whether devices should be listed.
        /// </summary>
        public bool ListDevices { get; private set; }

        /// <summary>
        /// Gets the requested device identifier.
        /// </summary>
        public int? DeviceId { get; private set; }

        /// <summary>
        /// Gets the requested recording duration.
        /// </summary>
        public TimeSpan? Duration { get; private set; }

        /// <summary>
        /// Gets the output file path supplied by the user.
        /// </summary>
        public string? OutputPath { get; private set; }

        /// <summary>
        /// Parses the supplied command-line arguments.
        /// </summary>
        /// <param name="args">Raw arguments.</param>
        /// <returns>The populated <see cref="CaptureOptions"/> instance.</returns>
        public static CaptureOptions Parse(IReadOnlyList<string> args)
        {
            var options = new CaptureOptions();

            var index = 0;
            while (index < args.Count)
            {
                var argument = args[index];
                index++;
                switch (argument)
                {
                    case "--list-devices":
                    case "-l":
                        options.ListDevices = true;
                        break;
                    case "--device":
                    case "-d":
                        options.DeviceId = ParseDeviceId(args, ref index);
                        break;
                    case "--duration":
                    case "-t":
                        options.Duration = ParseDuration(args, ref index);
                        break;
                    case "--output":
                    case "-o":
                        options.OutputPath = ParseOutputPath(args, ref index);
                        break;
                    case "--help":
                    case "-h":
                        PrintUsage();
                        options.ListDevices = true;
                        break;
                    default:
                        throw new ArgumentException($"Unrecognized argument: {argument}");
                }
            }

            return options;
        }

        /// <summary>
        /// Parses the device identifier argument.
        /// </summary>
        /// <param name="args">Command-line arguments.</param>
        /// <param name="index">Reference to the current argument index.</param>
        /// <returns>The parsed device identifier.</returns>
        private static int ParseDeviceId(IReadOnlyList<string> args, ref int index)
        {
            var value = ReadNextArgument(args, ref index, "--device");
            if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var deviceId))
            {
                throw new ArgumentException($"Invalid device identifier: {value}.");
            }

            return deviceId;
        }

        /// <summary>
        /// Parses the duration argument expressed in seconds.
        /// </summary>
        /// <param name="args">Command-line arguments.</param>
        /// <param name="index">Reference to the current argument index.</param>
        /// <returns>The duration represented as a <see cref="TimeSpan"/>.</returns>
        private static TimeSpan ParseDuration(IReadOnlyList<string> args, ref int index)
        {
            var value = ReadNextArgument(args, ref index, "--duration");
            if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) || seconds <= 0)
            {
                throw new ArgumentException($"Invalid duration value: {value}.");
            }

            return TimeSpan.FromSeconds(seconds);
        }

        /// <summary>
        /// Parses the output path argument.
        /// </summary>
        /// <param name="args">Command-line arguments.</param>
        /// <param name="index">Reference to the current argument index.</param>
        /// <returns>The user-provided file path.</returns>
        private static string ParseOutputPath(IReadOnlyList<string> args, ref int index)
        {
            return ReadNextArgument(args, ref index, "--output");
        }

        /// <summary>
        /// Retrieves the next argument value, throwing when none is available.
        /// </summary>
        /// <param name="args">Command-line arguments.</param>
        /// <param name="index">Reference to the current argument index.</param>
        /// <param name="optionName">Name of the option requiring the value.</param>
        /// <returns>The raw argument value.</returns>
        private static string ReadNextArgument(IReadOnlyList<string> args, ref int index, string optionName)
        {
            if (index >= args.Count)
            {
                throw new ArgumentException($"{optionName} requires a value.");
            }

            var value = args[index];
            index++;
            return value;
        }

        /// <summary>
        /// Prints usage instructions.
        /// </summary>
        private static void PrintUsage()
        {
            Console.WriteLine("Usage: whisper_capture [options]");
            Console.WriteLine("Options:");
            Console.WriteLine("  --list-devices, -l       List devices exposed by the Windows audio stack and exit.");
            Console.WriteLine("  --device, -d <id>        Capture using the specified device identifier.");
            Console.WriteLine("  --duration, -t <seconds> Stop automatically after the specified duration.");
            Console.WriteLine("  --output, -o <path>      Write to the specified WAV file path.");
            Console.WriteLine("  --help, -h               Display this help message.");
        }
    }

    /// <summary>
    /// Coordinates signalling completion of the capture session.
    /// </summary>
    private sealed class CaptureCompletionSource : IDisposable
    {
        private readonly TaskCompletionSource<bool> completionSource = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private bool disposed;

        /// <summary>
        /// Initializes a new instance of the <see cref="CaptureCompletionSource"/> class.
        /// </summary>
        /// <param name="duration">Optional capture duration.</param>
        public CaptureCompletionSource(TimeSpan? duration)
        {
            if (duration.HasValue)
            {
                _ = Task.Run(async () =>
                {
                    await Task.Delay(duration.Value, CancellationToken.None).ConfigureAwait(false);
                    TryComplete();
                }, CancellationToken.None);
            }
        }

        /// <summary>
        /// Waits for completion.
        /// </summary>
        /// <returns>A task that completes when capture should stop.</returns>
        public Task WaitAsync()
        {
            return completionSource.Task;
        }

        /// <summary>
        /// Attempts to complete the capture.
        /// </summary>
        public void TryComplete()
        {
            completionSource.TrySetResult(true);
        }

        /// <summary>
        /// Disposes the completion source, cancelling any pending timers.
        /// </summary>
        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
        }
    }

    /// <summary>
    /// Represents a managed audio capture session backed by WASAPI.
    /// </summary>
    private sealed class AudioCaptureSession : IDisposable
    {
        private readonly WasapiCapture capture;
        private readonly WaveFileWriter writer;
        private readonly MMDevice device;
        private readonly TaskCompletionSource<bool> completionSource = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private bool stopping;
        private bool disposed;
        private bool resourcesReleased;

        private AudioCaptureSession(WasapiCapture capture, MMDevice device, string outputPath)
        {
            this.capture = capture;
            this.device = device;
            writer = new WaveFileWriter(outputPath, capture.WaveFormat);
            capture.DataAvailable += OnDataAvailable;
            capture.RecordingStopped += OnRecordingStopped;
        }

        /// <summary>
        /// Gets the wave format reported by the capture device.
        /// </summary>
        public WaveFormat Format => capture.WaveFormat;

        /// <summary>
        /// Gets a task that completes once the capture has stopped.
        /// </summary>
        public Task Completion => completionSource.Task;

        /// <summary>
        /// Creates a new capture session for the specified device.
        /// </summary>
        /// <param name="deviceInfo">The selected capture descriptor.</param>
        /// <param name="outputPath">The output WAV path.</param>
        /// <returns>An initialized <see cref="AudioCaptureSession"/>.</returns>
        public static AudioCaptureSession Create(CaptureDevice deviceInfo, string outputPath)
        {
            var mmDevice = ResolveEndpoint(deviceInfo);
            WasapiCapture capture = deviceInfo.Kind == CaptureDeviceKind.Output
                ? new WasapiLoopbackCapture(mmDevice)
                : new WasapiCapture(mmDevice)
                {
                    ShareMode = AudioClientShareMode.Shared,
                };

            return new AudioCaptureSession(capture, mmDevice, outputPath);
        }

        /// <summary>
        /// Begins recording.
        /// </summary>
        public void Start()
        {
            capture.StartRecording();
        }

        /// <summary>
        /// Requests that recording stop.
        /// </summary>
        public void Stop()
        {
            if (stopping)
            {
                return;
            }

            stopping = true;
            capture.StopRecording();
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            Stop();
            capture.DataAvailable -= OnDataAvailable;
            capture.RecordingStopped -= OnRecordingStopped;
            ReleaseResources(null);
        }

        private static MMDevice ResolveEndpoint(CaptureDevice deviceInfo)
        {
            using var enumerator = new MMDeviceEnumerator();
            return enumerator.GetDevice(deviceInfo.EndpointId);
        }

        [SuppressMessage("Reliability", "MA0045:Use 'WriteAsync' instead of 'Write' and make method async", Justification = "NAudio invokes this callback on a dedicated thread and synchronous writes maintain ordering.")]
        private void OnDataAvailable(object? sender, WaveInEventArgs e)
        {
            writer.Write(e.Buffer, 0, e.BytesRecorded);
        }

        private void OnRecordingStopped(object? sender, StoppedEventArgs e)
        {
            this.ReleaseResources(e.Exception);
        }

        /// <summary>
        /// Releases unmanaged resources and signals completion.
        /// </summary>
        /// <param name="error">Optional error encountered during capture.</param>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Resource cleanup must capture and propagate disposal errors without crashing the audio thread.")]
    [SuppressMessage("Reliability", "MA0045:Use 'DisposeAsync' instead of 'Dispose' and make method async", Justification = "The involved NAudio types expose only synchronous dispose semantics.")]
        private void ReleaseResources(Exception? error)
        {
            if (resourcesReleased)
            {
                if (error is not null)
                {
                    completionSource.TrySetException(error);
                }

                return;
            }

            resourcesReleased = true;

            Exception? disposeFailure = null;

            try
            {
                writer.Dispose();
            }
            catch (Exception ex)
            {
                disposeFailure ??= ex;
            }

            try
            {
                capture.Dispose();
            }
            catch (Exception ex)
            {
                disposeFailure ??= ex;
            }

            try
            {
                device.Dispose();
            }
            catch (Exception ex)
            {
                disposeFailure ??= ex;
            }

            var resultError = error ?? disposeFailure;
            if (resultError is not null)
            {
                completionSource.TrySetException(resultError);
                return;
            }

            completionSource.TrySetResult(true);
        }
    }
}
