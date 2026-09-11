using PropFlow.Deploy;

// propflow-deploy — stands the PropFlow stack up on one host in a Production posture.
// See docs/DEPLOYMENT.md. This program orchestrates already-published binaries; it never links
// the application, so it carries no PropFlow dependencies of its own.

const string usage = """
        propflow-deploy — deploy PropFlow on this machine

        Usage:
          propflow-deploy up [--no-seed]   Start everything. Safe to re-run.
          propflow-deploy down [--reset]   Stop everything. --reset also drops the database volume.
          propflow-deploy status           Report ports and API health.
          propflow-deploy --help

        Options:
          --no-seed      Bring the stack up without demo organizations or sample data.
          --reset        With `down`, delete the database volume as well as stopping the stack.
          --root <path>  Use a bundle directory other than the one holding this executable.

        Requires Docker Desktop (running) and Node.js 22. Does not require the .NET SDK.
        """;

var arguments = args.ToList();
if (arguments.Count == 0 || arguments.Contains("--help") || arguments.Contains("-h"))
{
    Console.WriteLine(usage);
    return arguments.Count == 0 ? 1 : 0;
}

var verb = arguments[0];
var noSeed = arguments.Remove("--no-seed");
var reset = arguments.Remove("--reset");

var rootIndex = arguments.IndexOf("--root");
string? root = null;
if (rootIndex >= 0)
{
    if (rootIndex + 1 >= arguments.Count)
    {
        Console.Error.WriteLine("--root needs a directory.");
        return 1;
    }
    root = arguments[rootIndex + 1];
    arguments.RemoveRange(rootIndex, 2);
}

if (arguments.Count != 1)
{
    Console.Error.WriteLine($"Unrecognized arguments: {string.Join(' ', arguments.Skip(1))}");
    Console.Error.WriteLine(usage);
    return 1;
}

var layout = root is null ? DeploymentLayout.FromExecutableLocation() : DeploymentLayout.At(root);
var controller = new StackController(layout, Console.WriteLine);

// Ctrl+C cancels the in-flight step rather than leaving a half-written state directory.
using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    Console.WriteLine();
    Console.WriteLine("Cancelling. Run `propflow-deploy down` to stop anything already started.");
    cancellation.Cancel();
};

try
{
    return verb switch
    {
        "up" => await controller.UpAsync(!noSeed, cancellation.Token),
        "down" => await controller.DownAsync(reset, cancellation.Token),
        "status" => await controller.StatusAsync(cancellation.Token),
        _ => Unknown(verb),
    };
}
catch (OperationCanceledException)
{
    return 130;
}
catch (Exception error)
{
    Console.Error.WriteLine($"Deployment failed: {error.Message}");
    return 1;
}

static int Unknown(string verb)
{
    Console.Error.WriteLine($"Unknown command '{verb}'. Expected up, down, or status.");
    Console.Error.WriteLine(usage);
    return 1;
}
