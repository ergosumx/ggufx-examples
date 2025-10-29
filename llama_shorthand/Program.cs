using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace LlamaShorthand;

internal static class Program
{
    /// <summary>
    /// Entry point that prepares native dependencies and invokes the exported llama mtmd CLI main function.
    /// </summary>
    /// <param name="args">Optional command line arguments to forward to the native CLI.</param>
    /// <returns>The exit code produced by the native CLI.</returns>
    public static int Main(string[] args)
    {
        try
        {
            return Execute(args);
        }
        catch (FileNotFoundException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
        catch (DllNotFoundException ex)
        {
            Console.Error.WriteLine($"Native library not found: {ex.Message}");
            return 1;
        }
        catch (BadImageFormatException ex)
        {
            Console.Error.WriteLine($"Failed to load native library: {ex.Message}");
            return 1;
        }
    }

    private static int Execute(string[] args)
    {
        var repositoryRoot = LocateRepositoryRoot();
        if (repositoryRoot is null)
        {
            Console.Error.WriteLine("Unable to locate the repository root. Run the sample from within the repo.");
            return 1;
        }

        var runtimePath = Path.Combine(repositoryRoot, "runtimes", "windows-x64", "native");
        if (!Directory.Exists(runtimePath))
        {
            Console.Error.WriteLine($"Runtime directory not found: {runtimePath}");
            return 1;
        }

        AppendToProcessPath(runtimePath);
        LoadNativeDependencies(runtimePath);

        var defaultArguments = BuildDefaultArguments(repositoryRoot);
        var forwardedArguments = args.Length > 0
            ? BuildForwardedArguments(args)
            : defaultArguments;

        Console.WriteLine("Invoking ggufx-mtmd-cli with arguments:");
        Console.WriteLine(string.Join(" ", forwardedArguments.Select(QuoteIfNeeded)));
        Console.WriteLine();

        using var argv = new Utf8StringArray(forwardedArguments);
        var exitCode = NativeMethods.LlamaMtmdCliMain(argv.Count, argv.Pointer);
        Console.WriteLine($"Native CLI completed with exit code {exitCode}.");
        return exitCode;
    }

    private static string? LocateRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "ErgoX.VecraX.GgufX.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        return null;
    }

    private static IReadOnlyList<string> BuildDefaultArguments(string repositoryRoot)
    {
        var modelPath = Path.Combine(repositoryRoot, ".models", "granite-docling-258M-GGUF", "granite-docling-258M-Q8_0.gguf");
        var projectorPath = Path.Combine(repositoryRoot, ".models", "granite-docling-258M-GGUF", "mmproj-granite-docling-258M-Q8_0.gguf");
        var imagePath = Path.Combine(repositoryRoot, "examples", "GraniteDockling-258M", "temp", "Screenshot 2025-10-27 224448.png");

        ValidateFile(modelPath, "Model");
        ValidateFile(projectorPath, "Projector");
        ValidateFile(imagePath, "Image");

        return new[]
        {
            "llama-mtmd-cli",
            "-m", modelPath,
            "--mmproj", projectorPath,
            "--image", imagePath,
            "-p", "Convert to Markdown"
        };
    }

    private static IReadOnlyList<string> BuildForwardedArguments(string[] args)
    {
        var forwarded = new string[args.Length + 1];
        forwarded[0] = "llama-mtmd-cli";
        Array.Copy(args, 0, forwarded, 1, args.Length);
        return forwarded;
    }

    private static void ValidateFile(string path, string description)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"{description} file not found: {path}");
        }
    }

    private static void AppendToProcessPath(string directory)
    {
        var currentPath = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        var pathSegments = currentPath.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
        if (!pathSegments.Contains(directory, StringComparer.OrdinalIgnoreCase))
        {
            var updatedPath = string.IsNullOrEmpty(currentPath)
                ? directory
                : string.Concat(directory, Path.PathSeparator, currentPath);
            Environment.SetEnvironmentVariable("PATH", updatedPath);
        }
    }

    private static void LoadNativeDependencies(string runtimePath)
    {
        foreach (var library in new[] { "ggml.dll", "ggufx.dll", "ggufx-mtmd.dll", "ggufx-mtmd-cli.dll" })
        {
            var libraryPath = Path.Combine(runtimePath, library);
            if (!File.Exists(libraryPath))
            {
                throw new FileNotFoundException($"Missing native dependency: {libraryPath}");
            }

            _ = NativeLibrary.Load(libraryPath);
        }
    }

    private static string QuoteIfNeeded(string value)
    {
        return value.Contains(' ') ? $"\"{value}\"" : value;
    }

    private sealed class Utf8StringArray : IDisposable
    {
        private readonly IntPtr[] pointers;
        private readonly IntPtr arrayPointer;
        private bool disposed;

        public Utf8StringArray(IReadOnlyList<string> values)
        {
            if (values.Count == 0)
            {
                throw new ArgumentException("At least one argument is required to invoke the native CLI.", nameof(values));
            }

            pointers = new IntPtr[values.Count];

            try
            {
                for (var i = 0; i < values.Count; i++)
                {
                    pointers[i] = AllocateUtf8(values[i]);
                }

                arrayPointer = Marshal.AllocHGlobal(IntPtr.Size * pointers.Length);
                Marshal.Copy(pointers, 0, arrayPointer, pointers.Length);
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public int Count => pointers.Length;

        public IntPtr Pointer => arrayPointer;

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            foreach (var ptr in pointers)
            {
                if (ptr != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(ptr);
                }
            }

            if (arrayPointer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(arrayPointer);
            }

            disposed = true;
        }

        private static IntPtr AllocateUtf8(string value)
        {
            var bytes = Encoding.UTF8.GetBytes(value);
            var handle = Marshal.AllocHGlobal(bytes.Length + 1);
            Marshal.Copy(bytes, 0, handle, bytes.Length);
            Marshal.WriteByte(handle, bytes.Length, 0);
            return handle;
        }
    }

    private static class NativeMethods
    {
        [DllImport("ggufx-mtmd-cli.dll", EntryPoint = "llama_mtmd_cli_main", CallingConvention = CallingConvention.Cdecl)]
        public static extern int LlamaMtmdCliMain(int argc, IntPtr argv);
    }
}
