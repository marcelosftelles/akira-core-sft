using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace AkiraCore.App.Platform;

/// <summary>
/// Leva a linha de comando de um segundo AkiraCore.exe até a instância aberta, por um pipe nomeado
/// só do usuário atual (PipeOptions.CurrentUserOnly) e da sessão do Windows dele. O pedido vai como
/// JSON com os argumentos; a resposta é uma linha de texto e o código de saída.
/// </summary>
public static class CommandChannel
{
    private static string PipeName => $"AkiraCore.Commands.{Process.GetCurrentProcess().SessionId}";

    public sealed record Reply(bool Ok, string Text);

    /// <summary>Instância aberta: atende pedidos até o token cancelar.</summary>
    public static async Task ServeAsync(Func<string[], Task<Reply>> handle, CancellationToken cancel)
    {
        while (!cancel.IsCancellationRequested)
        {
            try
            {
                await using var server = new NamedPipeServerStream(PipeName, PipeDirection.InOut, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await server.WaitForConnectionAsync(cancel);
                using var reader = new StreamReader(server, Encoding.UTF8, leaveOpen: true);
                await using var writer = new StreamWriter(server, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
                var line = await reader.ReadLineAsync(cancel).AsTask().WaitAsync(TimeSpan.FromSeconds(5), cancel);
                var args = line is null ? null : JsonSerializer.Deserialize<string[]>(line);
                var reply = args is null ? new Reply(false, "Pedido vazio.") : await handle(args);
                await writer.WriteLineAsync(JsonSerializer.Serialize(reply));
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex) when (ex is IOException or JsonException or TimeoutException or UnauthorizedAccessException)
            {
                Debug.WriteLine($"Pedido da linha de comando ignorado: {ex.Message}");
                await Task.Delay(200, CancellationToken.None);
            }
        }
    }

    /// <summary>Segunda instância: entrega os argumentos e espera a resposta. Null se ninguém atendeu.</summary>
    public static Reply? Send(string[] args, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (true)
        {
            try
            {
                using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.CurrentUserOnly);
                client.Connect((int)Math.Max(200, (deadline - DateTime.UtcNow).TotalMilliseconds));
                using var writer = new StreamWriter(client, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
                using var reader = new StreamReader(client, Encoding.UTF8, leaveOpen: true);
                writer.WriteLine(JsonSerializer.Serialize(args));
                var line = reader.ReadLine();
                return line is null ? null : JsonSerializer.Deserialize<Reply>(line);
            }
            catch (Exception ex) when (ex is TimeoutException or IOException)
            {
                if (DateTime.UtcNow >= deadline)
                {
                    return null;
                }
                Thread.Sleep(200);
            }
        }
    }
}
