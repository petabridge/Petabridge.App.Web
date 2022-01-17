using System;
using System.Linq;
using Nuke.Common;
using Nuke.Common.CI;
using Nuke.Common.Execution;
using Nuke.Common.Git;
using Nuke.Common.IO;
using Nuke.Common.ProjectModel;
using Nuke.Common.Tooling;
using Nuke.Common.Tools.DotNet;
using Nuke.Common.Tools.GitVersion;
using Nuke.Common.Utilities.Collections;
using static Nuke.Common.EnvironmentInfo;
using static Nuke.Common.IO.FileSystemTasks;
using static Nuke.Common.IO.PathConstruction;
using static Nuke.Common.Tools.DotNet.DotNetTasks;
using static Nuke.Common.Tools.DocFX.DocFXTasks;
using System.Text.Json;
using System.IO;
using static Nuke.Common.Tools.NuGet.NuGetTasks;
using static Nuke.Common.ChangeLog.ChangelogTasks;
using static Nuke.Common.Tools.Git.GitTasks;
using static Nuke.Common.Tools.BenchmarkDotNet.BenchmarkDotNetTasks;
using Nuke.Common.ChangeLog;
using System.Collections.Generic;
using Nuke.Common.Tools.GitHub;
using Nuke.Common.Utilities;
using Nuke.Common.Tools.BenchmarkDotNet;
using Nuke.Common.Tools.DocFX;

[CheckBuildProjectConfigurations]
[ShutdownDotNetAfterServerBuild]
partial class Build : NukeBuild
{
    /// Support plugins are available for:
    ///   - JetBrains ReSharper        https://nuke.build/resharper
    ///   - JetBrains Rider            https://nuke.build/rider
    ///   - Microsoft VisualStudio     https://nuke.build/visualstudio
    ///   - Microsoft VSCode           https://nuke.build/vscode

    public static int Main() => Execute<Build>(x => x.ServeDocs);

    [Parameter("Configuration to build - Default is 'Debug' (local) or 'Release' (server)")]
    readonly Configuration Configuration = IsLocalBuild ? Configuration.Debug : Configuration.Release;

    [Solution] readonly Solution Solution;
    [GitRepository] readonly GitRepository GitRepository;
    [GitVersion(Framework = "net6.0")] readonly GitVersion GitVersion;

    // Directories
    AbsolutePath ToolsDir => RootDirectory / "tools";
    AbsolutePath Output => RootDirectory / "bin";
    AbsolutePath OutputNuget => Output / "nuget";
    AbsolutePath OutputTests => RootDirectory / "TestResults";
    AbsolutePath OutputPerfTests => RootDirectory / "PerfResults";
    AbsolutePath SourceDirectory => RootDirectory / "src";
    AbsolutePath DocSiteDirectory => RootDirectory / "docs" / "_site";
    public string ChangelogFile => RootDirectory / "CHANGELOG.md";
    public AbsolutePath DocFxDir => RootDirectory / "docs";
    public AbsolutePath DocFxDirJson => DocFxDir / "docfx.json";


    [Parameter] readonly string Source = "https://resharper-plugins.jetbrains.com/api/v2/package";

    static readonly JsonElement? _githubContext = string.IsNullOrWhiteSpace(EnvironmentInfo.GetVariable<string>("GITHUB_CONTEXT")) ? 
        null 
        : JsonSerializer.Deserialize<JsonElement>(EnvironmentInfo.GetVariable<string>("GITHUB_CONTEXT"));

    //let hasTeamCity = (not (buildNumber = "0")) // check if we have the TeamCity environment variable for build # set
    static readonly int BuildNumber = _githubContext.HasValue ? int.Parse(_githubContext.Value.GetProperty("run_number").GetString()) : 0;
        
    public ChangeLog Changelog => ReadChangelog(ChangelogFile);

    public ReleaseNotes LatestVersion => Changelog.ReleaseNotes.OrderByDescending(s => s.Version).FirstOrDefault() ?? throw new ArgumentException("Bad Changelog File. Version Should Exist");
    public string ReleaseVersion => LatestVersion.Version?.ToString() ?? throw new ArgumentException("Bad Changelog File. Define at least one version");

    Target Clean => _ => _
        .Before(Restore)
        .Executes(() =>
        {
            RootDirectory
            .GlobDirectories("src/**/bin", "src/**/obj", Output, OutputTests, OutputPerfTests, OutputNuget, DocSiteDirectory)
            .ForEach(DeleteDirectory);
            EnsureCleanDirectory(Output);
        });

    Target Restore => _ => _
        .Executes(() =>
        {
            DotNetRestore(s => s
                .SetProjectFile(Solution));
        });
    IEnumerable<string> ChangelogSectionNotes => ExtractChangelogSectionNotes(ChangelogFile);

    Target RunChangelog => _ => _
        .Executes(() =>
        {
            // GitVersion.SemVer appends the branch name to the version numer (this is good for pre-releases)
            // If you are executing this under a branch that is not beta or alpha - that is final release branch
            // you can update the switch block to reflect your release branch name
            var vNext = string.Empty;
            var branch = GitVersion.BranchName;
            switch (branch)
            {
                case "main":
                case "master":
                    vNext = GitVersion.MajorMinorPatch;
                    break;
                default:
                    vNext = GitVersion.SemVer;  
                    break;
            }
            FinalizeChangelog(ChangelogFile, vNext, GitRepository);

            Git($"add {ChangelogFile}");
            Git($"commit -m \"Finalize {Path.GetFileName(ChangelogFile)} for {vNext}.\"");

            //To sign your commit
            //Git($"commit -S -m \"Finalize {Path.GetFileName(ChangelogFile)} for {vNext}.\"");

            Git($"tag -f {GitVersion.SemVer}");
        });
    Target CreateNuget => _ => _
      .DependsOn(RunTests)
      .Executes(() =>
      {
          //Since this is about a new release, `RunChangeLog` need to be executed to update the ChangeLog.md with the new version
          //from which LatestVersion will be parsed
          var version = LatestVersion;
          var projects = SourceDirectory.GlobFiles("**/*.csproj")
          .Except(SourceDirectory.GlobFiles("**/*Tests.csproj", "**/*Tests*.csproj"));
          foreach(var project in projects)
          {
              DotNetPack(s => s
                  .SetProject(project)
                  .SetConfiguration(Configuration)
                  .EnableNoBuild()
                  .SetIncludeSymbols(true)  
                  .EnableNoRestore()
                  .SetAssemblyVersion(version.Version.ToString())
                  .SetFileVersion(version.Version.ToString())
                  .SetVersion(version.Version.ToString())
                  .SetPackageReleaseNotes(GetNuGetReleaseNotes(ChangelogFile, GitRepository))
                  .SetDescription("YOUR_DESCRIPTION_HERE")
                  .SetPackageProjectUrl("YOUR_PACKAGE_URL_HERE")
                  .SetOutputDirectory(OutputNuget)); 
          }
      });
    Target RunTests => _ => _
        .After(Compile)
        .Executes(() =>
        {
            var projects = Solution.GetProjects("*.Tests");
            foreach(var project in projects)
            {
                Information($"Running tests from {project}");
                foreach (var fw in project.GetTargetFrameworks())
                {
                    Information($"Running for {project} ({fw}) ...");
                    DotNetTest(c => c
                           .SetProjectFile(project)
                           .SetConfiguration(Configuration.ToString())
                           .SetFramework(fw)
                           .SetResultsDirectory(OutputTests)
                           .SetProcessWorkingDirectory(Directory.GetParent(project).FullName) 
                           .SetLoggers("trx")
                           .SetVerbosity(verbosity: DotNetVerbosity.Normal)
                           .EnableNoBuild());
                }
            }
        });
    Target NBench => _ => _
    .DependsOn(Compile)
    //.WhenSkipped(DependencyBehavior.Skip)
    .Executes(() => 
    {
        RootDirectory
            .GlobDirectories("src/**/*.Tests.Performance.csproj")
            .ForEach(path => 
            {
                BenchmarkDotNet($"--nobuild --concurrent true --trace true --output {OutputPerfTests}", workingDirectory: Directory.GetParent(path).FullName, timeout: TimeSpan.FromMinutes(30).Minutes, logOutput: true);
                /*BenchmarkDotNet(b => b
                    .SetProcessWorkingDirectory(Directory.GetParent(path).FullName)                    
                    .SetAffinity(1)
                    .SetDisassembly(true)
                    .SetDisassemblyDiff(true)
                    .SetExporters(BenchmarkDotNetExporter.GitHub, BenchmarkDotNetExporter.CSV));*/

            });

    });
    Target Docker => _ => _
        .Executes(() =>
        {
            DotNetRestore(s => s
                .SetProjectFile(Solution));
        });
    //--------------------------------------------------------------------------------
    // Documentation 
    //--------------------------------------------------------------------------------
    Target DocsInit => _ => _
        .DependsOn(Compile)
        .Executes(() =>
        {
            DocFXInit(s => s.SetOutputFolder(DocFxDir).SetQuiet(true));
        });
    Target DocsMetadata => _ => _
        .DependsOn(Compile)
        .Executes(() => 
        {
            DocFXMetadata(s => s
            .SetProjects(DocFxDirJson)
            .SetLogLevel(DocFXLogLevel.Verbose));
        });

    Target DocBuild => _ => _
        .DependsOn(DocsMetadata)
        .Executes(() => 
        {
            DocFXBuild(s => s
            .SetConfigFile(DocFxDirJson)
            .SetLogLevel(DocFXLogLevel.Verbose));
        });

    Target ServeDocs => _ => _
        .DependsOn(DocBuild)
        .Executes(() => DocFXServe(s=>s.SetFolder(DocFxDir)));

    Target Compile => _ => _
        .DependsOn(Restore)
        .Executes(() =>
        {
            var version = LatestVersion;
            DotNetBuild(s => s
                .SetProjectFile(Solution)
                .SetConfiguration(Configuration)
                .SetAssemblyVersion(version.Version.ToString())
                .SetFileVersion(version.Version.ToString())
                .EnableNoRestore());
        });

    Target Install => _ => _
        //.DependsOn<IPack>()
        .Executes(() =>
        {
            DotNet($"tool install Nuke.GlobalTool --global");
        });


    static void Information(string info)
    {
        Serilog.Log.Information(info);
    }
}
