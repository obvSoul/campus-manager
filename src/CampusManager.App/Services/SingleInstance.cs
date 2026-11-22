using System.IO.Pipes;

namespace CampusManager.App.Services;

/// <summary>
/// Une seule instance : un 2ᵉ lancement envoie sa demande (« ouvrir », avec une page éventuelle) à
/// l'instance déjà ouverte par un canal nommé, puis quitte.
/// </summary>
public sealed class SingleInstance : IDisposable
{
    public const string MutexName = @"Local\CampusManager.Instance";
    const string PipeName = "CampusManager.Open";

    /// <summary>
    /// Essai sur d'autres données (CAMPUS_DATA_DIR) : verrou et canal à part, pour tourner à côté de l'appli
    /// installée sans lui passer la main.
    /// </summary>
    static readonly string Suffix = Environment.GetEnvironmentVariable("CAMPUS_DATA_DIR") is { Length: > 0 } d
        ? "." + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(d.ToLowerInvariant())))[..12]
        : "";

    readonly Mutex _mutex;
    readonly CancellationTokenSource _stop = new();
    public bool IsFirst { get; }

    public SingleInstance()
    {
        _mutex = new Mutex(true, MutexName + Suffix, out var created);
        IsFirst = created;
    }

    /// <summary>Envoie le message à l'instance déjà ouverte.</summary>
    public static void Signal(string message)
    {
        try
        {
            using var c = new NamedPipeClientStream(".", PipeName + Suffix, PipeDirection.Out);
            c.Connect(2000);
            using var w = new StreamWriter(c);
            w.WriteLine(message);
        }
        catch { /* instance en cours de fermeture : rien à faire */ }
    }

    /// <summary>Écoute les demandes des lancements suivants (appelé par la 1ʳᵉ instance).</summary>
    public void Listen(Action<string> onMessage) => Task.Run(async () =>
    {
        while (!_stop.IsCancellationRequested)
        {
            try
            {
                using var s = new NamedPipeServerStream(PipeName + Suffix, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await s.WaitForConnectionAsync(_stop.Token);
                using var r = new StreamReader(s);
                onMessage(await r.ReadLineAsync(_stop.Token) ?? "");
            }
            catch (OperationCanceledException) { return; }
            catch { await Task.Delay(500); }
        }
    });

    public void Dispose()
    {
        _stop.Cancel();
        if (IsFirst) try { _mutex.ReleaseMutex(); } catch { }
        _mutex.Dispose();
    }
}
