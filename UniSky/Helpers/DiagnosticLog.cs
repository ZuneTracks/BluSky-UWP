using System;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage;

namespace UniSky.Helpers;

public static class DiagnosticLog
{
    private const string FileName = "unisky-diagnostics.log";
    private static readonly SemaphoreSlim writeSemaphore = new(1, 1);

    public static Task WriteAsync(string message)
        => WriteCoreAsync("INFO", message, null);

    public static Task WriteExceptionAsync(string operation, Exception exception)
        => WriteCoreAsync("ERROR", operation, exception);

    private static async Task WriteCoreAsync(string level, string message, Exception exception)
    {
        var lockTaken = false;
        try
        {
            await writeSemaphore.WaitAsync().ConfigureAwait(false);
            lockTaken = true;
            var file = await ApplicationData.Current.LocalFolder
                .CreateFileAsync(FileName, CreationCollisionOption.OpenIfExists)
                .AsTask()
                .ConfigureAwait(false);
            var entry = $"{DateTimeOffset.Now:O} [{level}] {message}";
            if (exception is not null)
                entry += Environment.NewLine + exception;

            await FileIO.AppendTextAsync(file, entry + Environment.NewLine).AsTask().ConfigureAwait(false);
        }
        catch
        {
            // Diagnostics must never cause a second application failure.
        }
        finally
        {
            if (lockTaken)
                writeSemaphore.Release();
        }
    }
}
