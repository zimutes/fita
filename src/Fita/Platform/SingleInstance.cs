using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace Fita.Platform;

/// <summary>
/// Uma só Fita de cada vez: abrir ficheiros com a app já aberta manda-os à que está a correr
/// (por um pipe com nome, só do próprio utilizador) em vez de abrir outra janela.
/// </summary>
public static class SingleInstance
{
    private static string PipeName
    {
        get
        {
            var user = new string(Environment.UserName.Where(char.IsLetterOrDigit).ToArray());
            var data = Environment.GetEnvironmentVariable("FITA_DATA");
            // Com outra pasta de dados (testes) é outra instância.
            var suffix = string.IsNullOrEmpty(data)
                ? ""
                : "-" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(data)))[..10];
            return $"fita-{user}{suffix}";
        }
    }

    /// <summary>Verdadeiro se havia outra instância e os argumentos lhe foram entregues.</summary>
    public static bool TrySend(string[] args)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out, PipeOptions.CurrentUserOnly);
            client.Connect(300);
            // Caminhos relativos resolvem-se aqui, na pasta de quem os mandou.
            var full = args.Select(a => a.StartsWith('-') ? a : Path.GetFullPath(a)).ToArray();
            var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(full));
            client.Write(bytes);
            client.Flush();
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static void Listen(Action<string[]> onArgs)
    {
        new Thread(() =>
        {
            while (true)
            {
                try
                {
                    using var server = new NamedPipeServerStream(PipeName, PipeDirection.In, 1,
                        PipeTransmissionMode.Byte, PipeOptions.CurrentUserOnly);
                    server.WaitForConnection();
                    using var reader = new StreamReader(server, Encoding.UTF8);
                    var text = reader.ReadToEnd();
                    var args = JsonSerializer.Deserialize<string[]>(text) ?? [];
                    onArgs(args);
                }
                catch (IOException)
                {
                    // Já há outro servidor com este nome (duas instâncias a arrancar ao mesmo tempo).
                    return;
                }
                catch (Exception ex)
                {
                    Log.Write("Instância única: " + ex.Message);
                    Thread.Sleep(500);
                }
            }
        }) { IsBackground = true, Name = "fita-single-instance" }.Start();
    }
}
