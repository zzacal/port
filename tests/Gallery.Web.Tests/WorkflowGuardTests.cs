namespace Gallery.Web.Tests;

/// <summary>
/// 012 T020 [SAFETY] FR-024: source guards on the CI/CD workflow (contracts/workflow.md "Rules").
/// The deploy credential is OIDC-only, branch-scoped, and reachable only from the deploy job.
/// The file is read as text and split into job blocks by indentation; no YAML package.
/// </summary>
public class WorkflowGuardTests
{
    private static readonly string WorkflowPath = Path.Combine(Render.RepoRoot, ".github", "workflows", "ci.yml");

    private static string[] Lines() => File.ReadAllLines(WorkflowPath);

    private static int Indent(string line) => line.Length - line.TrimStart(' ').Length;

    private static bool IsContent(string line) =>
        line.Trim().Length > 0 && !line.TrimStart().StartsWith('#');

    /// <summary>Job name → the job's lines (header excluded), split on two-space job headers under <c>jobs:</c>.</summary>
    private static Dictionary<string, List<string>> JobBlocks()
    {
        var lines = Lines();
        var jobs = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var inJobs = false;
        List<string>? current = null;
        foreach (var line in lines)
        {
            if (!IsContent(line))
            {
                current?.Add(line);
                continue;
            }
            var indent = Indent(line);
            if (indent == 0)
            {
                inJobs = line.TrimEnd() == "jobs:";
                current = null;
                continue;
            }
            if (!inJobs) continue;
            if (indent == 2 && line.TrimEnd().EndsWith(':'))
            {
                var name = line.Trim().TrimEnd(':');
                current = [];
                jobs[name] = current;
                continue;
            }
            current?.Add(line);
        }
        return jobs;
    }

    private static List<string> Job(string name)
    {
        var jobs = JobBlocks();
        Assert.True(jobs.ContainsKey(name),
            $"ci.yml has no '{name}' job under jobs: (found: {string.Join(", ", jobs.Keys)})");
        return jobs[name];
    }

    private static int CountTrimmed(IEnumerable<string> lines, string exact) =>
        lines.Count(l => l.Trim() == exact);

    [Fact]
    public void Workflow_file_exists()
    {
        Assert.True(File.Exists(WorkflowPath), $"{WorkflowPath} is missing");
    }

    [Fact]
    public void Workflow_references_no_secret()
    {
        var hits = Lines().Select((l, i) => (l, i)).Where(x => x.l.Contains("secrets.", StringComparison.Ordinal))
            .Select(x => $"line {x.i + 1}: {x.l.Trim()}").ToList();
        Assert.True(hits.Count == 0, $"ci.yml references a secret (OIDC via vars only): {string.Join("; ", hits)}");
    }

    [Fact]
    public void Workflow_declares_no_environment()
    {
        var hits = Lines().Select((l, i) => (l, i)).Where(x => x.l.Contains("environment:", StringComparison.Ordinal))
            .Select(x => $"line {x.i + 1}: {x.l.Trim()}").ToList();
        Assert.True(hits.Count == 0,
            $"ci.yml declares environment: (it changes the OIDC subject and breaks the branch-based federated credential): {string.Join("; ", hits)}");
    }

    [Fact]
    public void Deploy_job_needs_build()
    {
        // Owner decision (012 decisions #48): the deploy ships the CI-built artifact and does not
        // wait on the test suite.
        var deploy = Job("deploy");
        Assert.True(CountTrimmed(deploy, "needs: build") == 1, "deploy job must contain exactly one 'needs: build'");
        Job("build");
    }

    [Fact]
    public void Deploy_job_runs_only_on_main()
    {
        var deploy = Job("deploy");
        Assert.True(CountTrimmed(deploy, "if: github.ref == 'refs/heads/main'") == 1,
            "deploy job must contain exactly one \"if: github.ref == 'refs/heads/main'\"");
    }

    [Fact]
    public void Id_token_write_appears_exactly_once_and_only_in_deploy()
    {
        var total = CountTrimmed(Lines(), "id-token: write");
        Assert.True(total == 1, $"'id-token: write' must appear exactly once in ci.yml, found {total}");
        var inDeploy = CountTrimmed(Job("deploy"), "id-token: write");
        Assert.True(inDeploy == 1, "'id-token: write' must be inside the deploy job's block");
        var anyIdToken = Lines().Count(l => l.Contains("id-token", StringComparison.Ordinal));
        Assert.True(anyIdToken == 1, $"'id-token' must appear only once (the deploy job's write grant), found {anyIdToken}");
    }

    [Fact]
    public void Deploy_targets_portfolio_web()
    {
        var deploy = Job("deploy");
        Assert.True(CountTrimmed(deploy, "app-name: portfolio-web") == 1,
            "deploy job must contain exactly one 'app-name: portfolio-web'");
        var appNames = Lines().Where(l => l.Trim().StartsWith("app-name:", StringComparison.Ordinal)).Select(l => l.Trim()).ToList();
        Assert.True(appNames.Count == 1 && appNames[0] == "app-name: portfolio-web",
            $"ci.yml must name exactly one app, portfolio-web (found: {string.Join("; ", appNames)})");
    }

    [Fact]
    public void Test_job_runs_dotnet_test()
    {
        var test = Job("test");
        var runs = test.Count(l => l.Trim().StartsWith("run: dotnet test ", StringComparison.Ordinal));
        Assert.True(runs == 1, $"test job must contain exactly one 'run: dotnet test' step, found {runs}");
    }
}
