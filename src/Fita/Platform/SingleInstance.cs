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
    // Fica com a primeira Fita até ela sair. Com vários ficheiros selecionados, o Explorador lança
    // um processo por ficheiro quase ao mesmo tempo: é o mutex que decide qual abre a janela.
    private static Mutex? _primary;

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

    /// <summary>
    /// Verdadeiro se já há outra Fita (aberta ou a abrir) e os argumentos lhe foram entregues:
    /// este processo deve sair. Falso se este é o primeiro e deve abrir a janela.
    /// </summary>
    public static bool HandOff(string[] args)
    {
        var deadline = Environment.TickCount64 + 30_000;
        while (true)
        {
            if (TryBecomePrimary())
                return false;
            // A primeira pode estar ainda a arrancar: espera-se que comece a ouvir.
            if (TrySend(args, 3000))
                return true;
            if (Environment.TickCount64 > deadline)
            {
                // Há uma Fita que não responde; abrir outra só baralhava a lista e as definições.
                Log.Write("Instância única: a Fita aberta não respondeu, ficheiros não entregues.");
                return true;
            }
        }
    }

    private static bool TryBecomePrimary()
    {
        var mutex = new Mutex(false, PipeName);
        bool owned;
        try
        {
            owned = mutex.WaitOne(0);
        }
        catch (AbandonedMutexException)
        {
            // A anterior fechou sem o largar (saiu à força): passa a ser deste.
            owned = true;
        }
        if (!owned)
        {
            mutex.Dispose();
            return false;
        }
        _primary = mutex;
        return true;
    }

    private static bool TrySend(string[] args, int timeoutMs)
    {
        // Caminhos relativos resolvem-se aqui, na pasta de quem os mandou.
        var full = args.Select(a => a.StartsWith('-') ? a : Path.GetFullPath(a)).ToArray();
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(full));
        var deadline = Environment.TickCount64 + timeoutMs;
        while (true)
        {
            try
            {
                using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out, PipeOptions.CurrentUserOnly);
                // Tentativas curtas com pausas: o Connect sozinho fica a girar enquanto o pipe não
                // existe, e dezenas de processos a girar atrasam o arranque da primeira.
                client.Connect(20);
                client.Write(bytes);
                client.Flush();
                return true;
            }
            catch (TimeoutException)
            {
                if (Environment.TickCount64 > deadline)
                    return false;
                Thread.Sleep(150);
            }
            catch
            {
                return false;
            }
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
                    // Já há outro servidor com este nome.
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
