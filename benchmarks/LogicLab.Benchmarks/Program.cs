using BenchmarkDotNet.Running;

var summaries = BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args).ToArray();
if (args.Any(argument => argument is "--list" or "--help" or "-h" or "--info" || argument.StartsWith("--list=", StringComparison.Ordinal)))
{
    return 0;
}
return summaries.Length > 0 && summaries.All(summary => !summary.HasCriticalValidationErrors
    && summary.Reports.Length > 0 && summary.Reports.All(report => report.Success)) ? 0 : 1;
