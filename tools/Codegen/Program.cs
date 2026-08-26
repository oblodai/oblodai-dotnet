using Oblodai.Codegen;

// Generates src/Oblodai/Contract/*.g.cs from contract/contract.json (exported by the gateway's own
// conformance suite) and contract/descriptions.en.json (English field docs). Nothing in the generated
// files is edited by hand.
//
//   dotnet run --project tools/Codegen -- generate   regenerate the files
//   dotnet run --project tools/Codegen -- check      fail when the committed files have drifted
var mode = args.FirstOrDefault() ?? "generate";
if (mode is not ("generate" or "check"))
{
    Console.Error.WriteLine("usage: Codegen [generate|check]");
    return 2;
}

var repoRoot = FindRepoRoot();
Contract contract;
try
{
    contract = Contract.Load(repoRoot);
}
catch (InvalidOperationException error)
{
    // The snapshot itself is unusable — say what is wrong in one line, rather than a stack trace.
    Console.Error.WriteLine($"codegen: {error.Message}");
    return 2;
}

var requests = new RequestsEmitter(contract);

var files = new Dictionary<string, string>(StringComparer.Ordinal)
{
    ["Routes.g.cs"] = Emitters.Routes(contract),
    ["Enums.g.cs"] = Emitters.Enums(contract),
    ["ErrorCodes.g.cs"] = Emitters.ErrorCodes(contract),
    ["Requests.g.cs"] = requests.Emit(),
    ["ContractVersion.g.cs"] = Emitters.Version(contract),
};

var outputDirectory = Path.Combine(repoRoot, "src", "Oblodai", "Contract");
Directory.CreateDirectory(outputDirectory);

if (mode == "check")
{
    var drifted = files
        .Where(f => !File.Exists(Path.Combine(outputDirectory, f.Key))
                    || File.ReadAllText(Path.Combine(outputDirectory, f.Key)) != f.Value)
        .Select(f => f.Key)
        .ToList();

    if (drifted.Count > 0)
    {
        Console.Error.WriteLine(
            $"contract drift: {string.Join(", ", drifted)} differ from contract/contract.json — "
            + "run `dotnet run --project tools/Codegen -- generate` and commit the result");
        return 1;
    }

    Console.WriteLine($"check-drift: {files.Count} generated files are in sync with contract/contract.json");
    return 0;
}

foreach (var (name, content) in files)
{
    File.WriteAllText(Path.Combine(outputDirectory, name), content);
}

if (requests.MissingDescriptions.Count > 0)
{
    Console.Error.WriteLine(
        $"codegen: {requests.MissingDescriptions.Count} request fields lack an English description in "
        + $"contract/descriptions.en.json:{Environment.NewLine}  "
        + string.Join($"{Environment.NewLine}  ", requests.MissingDescriptions));
}

Console.WriteLine(
    $"codegen: {contract.Routes.Count} routes, {requests.RequestTypes.Count} request bodies, "
    + $"{contract.Strings("error_codes").Count} error codes → {outputDirectory}");
return 0;

static string FindRepoRoot()
{
    var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
    while (directory is not null)
    {
        if (File.Exists(Path.Combine(directory.FullName, "contract", "contract.json")))
        {
            return directory.FullName;
        }

        directory = directory.Parent;
    }

    throw new InvalidOperationException("cannot find the repository root (no contract/contract.json above the cwd)");
}
