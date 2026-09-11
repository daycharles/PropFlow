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
          propflow-deploy credentials      Print the sign-in accounts for this deployment.
          propflow-deploy --help

        Options:
          --no-seed       Bring the stack up without demo organizations or sample data.
          --reset         With `down`, delete the database volume as well as stopping the stack.
          --root <path>   Use a bundle directory other than the one holding this executable.
          --state <path>  Put credentials, keys, attachments and logs here.
                          Defaults to <bundle>/state when that is writable, otherwise the
                          system application-data directory (%ProgramData%\PropFlow on Windows).
                          PROPFLOW_STATE sets the same thing.

        Requires Docker Desktop (running) and Node.js 22. Does not require the .NET SDK.
        """;

var arguments = args.ToList();
if (arguments.Count == 0 || arguments.Contains("--help") || arguments.Contains("-h"))
{
    Console.WriteLine(usage);
    return arguments.Count == 0 ? 1 : 0;
}

// Strip every option before reading the verb, so flag order does not matter:
// `up --no-seed` and `--no-seed up` are the same command.
var noSeed = arguments.Remove("--no-seed");
var reset = arguments.Remove("--reset");

if (!TakeOption(arguments, "--root", out var root)) return 1;
if (!TakeOption(arguments, "--state", out var state)) return 1;

if (arguments.Count != 1)
{
    Console.Error.WriteLine(arguments.Count == 0
        ? "No command given. Expected up, down, status, or credentials."
        : $"Expected one command, got: {string.Join(' ', arguments)}");
    Console.Error.WriteLine(usage);
    return 1;
}

var verb = arguments[0];

var layout = DeploymentLayout.Resolve(root, state);
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
        "credentials" => controller.Credentials(),
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
    Console.Error.WriteLine($"Unknown command '{verb}'. Expected up, down, status, or credentials.");
    Console.Error.WriteLine(usage);
    return 1;
}

// Pulls "--name value" out of the argument list. Returns false only when the flag is present
// without a value, which is a usage error rather than a missing option.
static bool TakeOption(List<string> arguments, string name, out string? value)
{
    value = null;
    var index = arguments.IndexOf(name);
    if (index < 0) return true;
    if (index + 1 >= arguments.Count)
    {
        Console.Error.WriteLine($"{name} needs a directory.");
        return false;
    }
    value = arguments[index + 1];
    arguments.RemoveRange(index, 2);
    return true;
}
