using System.Diagnostics;

namespace WebApi.Tests.Infrastructure;

internal static class RedisContainer
{
    public static async Task<string> FindByPublishedPort(int port)
    {
        var output = await Docker("ps", "--format", "{{.ID}}\t{{.Ports}}");
        var marker = $":{port}->";
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!line.Contains(marker, StringComparison.Ordinal))
            {
                continue;
            }

            var id = line.Split('\t')[0];
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new InvalidOperationException("Redis container id is missing.");
            }

            return id;
        }

        throw new InvalidOperationException("Redis container was not found.");
    }

    public static Task Stop(string containerId)
    {
        return Docker("stop", containerId);
    }

    public static Task Start(string containerId)
    {
        return Docker("start", containerId);
    }

    private static async Task<string> Docker(string command, params string[] args)
    {
        var start = new ProcessStartInfo
        {
            FileName = "docker",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        start.ArgumentList.Add(command);
        foreach (var argument in args)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start);
        if (process is null)
        {
            throw new InvalidOperationException("Docker did not start.");
        }

        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(error);
        }

        return output;
    }
}
