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
using static Nuke.Common.ChangeLog.ChangelogTasks;
using static Nuke.Common.Tools.Git.GitTasks;
using static Nuke.Common.Tools.BenchmarkDotNet.BenchmarkDotNetTasks;
using Nuke.Common.ChangeLog;
using System.Collections.Generic;
using Nuke.Common.Tools.DocFX;
using Nuke.Common.Tools.Docker;
using static Nuke.Common.Tools.SignClient.SignClientTasks;
using System.Text;

[CheckBuildProjectConfigurations]
[ShutdownDotNetAfterServerBuild]
partial class Build : NukeBuild
{
    /// Support plugins are available for:
    ///   - JetBrains ReSharper        https://nuke.build/resharper
    ///   - JetBrains Rider            https://nuke.build/rider
    ///   - Microsoft VisualStudio     https://nuke.build/visualstudio
    ///   - Microsoft VSCode           https://nuke.build/vscode

    public static int Main() => Execute<Build>(x => x.Install);

    [Parameter("Configuration to build - Default is 'Debug' (local) or 'Release' (server)")]
    readonly Configuration Configuration = IsLocalBuild ? Configuration.Debug : Configuration.Release;

    [Solution] readonly Solution Solution;
    [GitRepository] readonly GitRepository GitRepository;
    [GitVersion(Framework = "net6.0")] readonly GitVersion GitVersion;

    [Parameter] string NugetPublishUrl = "https://api.nuget.org/v3/index.json";
    [Parameter] string SymbolsPublishUrl;

    [Parameter] string DockerRegistryUrl;

    // Metadata used when signing packages and DLLs
    [Parameter] string SigningName = "My Library";
    [Parameter] string SigningDescription = "My REALLY COOL Library";
    [Parameter] string SigningUrl = "https://signing.is.cool/";

    [Parameter] [Secret] string NugetKey;
    [Parameter] [Secret] string DockerUsername;
    [Parameter] [Secret] string DockerPassword;

    [Parameter] [Secret] string SignClientSecret;
    [Parameter] [Secret] string SignClientUser;
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
          foreach (var project in projects)
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
    Target DockerLogin => _ => _
        .Before(PushImage)
        .Requires(() => !DockerRegistryUrl.IsNullOrEmpty())
        .Requires(() => !DockerPassword.IsNullOrEmpty())
        .Requires(() => !DockerUsername.IsNullOrEmpty())
        .Executes(() =>
        {
            var settings = new DockerLoginSettings()
                .SetServer(DockerRegistryUrl)
                .SetUsername(DockerUsername)
                .SetPassword(DockerPassword);
            DockerTasks.DockerLogin(settings);  
        });
    Target BuildImage => _ => _
        .DependsOn(PublishCode)
        .Executes(() =>
        {
            var version = LatestVersion;
            var tagVersion = $"{version.Version.Major}.{version.Version.Minor}.{version.Version.Patch}";
            var dockfiles = GetDockerProjects();
            foreach (var dockfile in dockfiles)
            {
                var image = $"{Directory.GetParent(dockfile).Name}".ToLower();
                var tags = new List<string>
                {
                    $"{image}:latest",
                    $"{image}:{tagVersion}"
                };
                if (!string.IsNullOrWhiteSpace(DockerRegistryUrl))
                {
                    tags.Add($"{DockerRegistryUrl}/{image}:latest");
                    tags.Add($"{DockerRegistryUrl}/{tagVersion}");
                }
                var settings = new DockerBuildSettings()
                 .SetFile(dockfile)
                 //.SetPull(true)
                 .SetPath(Directory.GetParent(dockfile).FullName)
                 //.SetProcessWorkingDirectory(Directory.GetParent(dockfile).FullName)
                 .SetTag(tags.ToArray());
                DockerTasks.DockerBuild(settings);
            }            
        });
    Target PushImage => _ => _
        .DependsOn(DockerLogin)
        .Executes(() =>
        {
            var version = LatestVersion;
            var tagVersion = $"{version.Version.Major}.{version.Version.Minor}.{version.Version.Patch}";
            var dockfiles = GetDockerProjects();
            foreach (var dockfile in dockfiles)
            {
                var image = $"{Directory.GetParent(dockfile).Name}".ToLower();
                var settings = new DockerImagePushSettings()
                    .SetName(string.IsNullOrWhiteSpace(DockerRegistryUrl) ? $"{image}:{tagVersion}" : $"{image}:{DockerRegistryUrl}/{tagVersion}");
                DockerTasks.DockerImagePush(settings);
            }
        });

    public Target BuildAndPush => _ => _
    .DependsOn(DockerLogin, BuildImage, PushImage);


    Target PublishNuget => _ => _
    .DependsOn(CreateNuget)
    .Requires(() => NugetPublishUrl)
    .Requires(() => !NugetKey.IsNullOrEmpty())
    .Executes(() =>
    {
        var packages = Output.GlobFiles("nuget/*.nupkg", "nuget/*.symbols.nupkg");
        var shouldPublishSymbolsPackages = !string.IsNullOrWhiteSpace(SymbolsPublishUrl);
        if (!string.IsNullOrWhiteSpace(NugetPublishUrl))
        {
            foreach (var package in packages)
            {
                if (shouldPublishSymbolsPackages)
                {
                    DotNetNuGetPush(s => s
                     .SetTimeout(TimeSpan.FromMinutes(10).Minutes)
                     .SetTargetPath(package)
                     .SetSource(NugetPublishUrl)
                     .SetSymbolSource(SymbolsPublishUrl)
                     .SetApiKey(NugetKey));
                }
                else
                {
                    DotNetNuGetPush(s => s
                      .SetTimeout(TimeSpan.FromMinutes(10).Minutes)
                      .SetTargetPath(package)
                      .SetSource(NugetPublishUrl)
                      .SetApiKey(NugetKey)
                  );
                }
            }
        }
    });
    Target RunTests => _ => _
        .DependsOn(Compile)
        .Executes(() =>
        {
            var projects = Solution.GetProjects("*.Tests");
            foreach (var project in projects)
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
    Target SignPackages => _ => _
        .DependsOn(CreateNuget)
        .Requires(() => !SignClientSecret.IsNullOrEmpty())
        .Requires(() => !SignClientUser.IsNullOrEmpty())
        .Executes(() =>
        {
            //not sure SignClient supports .nupkg
            //https://discoverdot.net/projects/sign-service?#user-content-supported-file-types
            var assemblies = OutputNuget.GlobFiles("*.nupkg");            
            foreach(var asm in assemblies)
            {
                var stringBuilder = new StringBuilder();
                stringBuilder.Append("sign");
                stringBuilder.Append("--config");
                stringBuilder.Append(RootDirectory / "appsettings.json");
                stringBuilder.Append("-i");
                stringBuilder.Append(asm);
                stringBuilder.Append("-r");
                stringBuilder.Append(SignClientUser);
                stringBuilder.Append("-s");
                stringBuilder.Append(SignClientSecret);
                stringBuilder.Append("-n");
                stringBuilder.Append(SigningName);
                stringBuilder.Append("-d");
                stringBuilder.Append(SigningDescription);
                stringBuilder.Append("-u");
                stringBuilder.Append(SigningUrl);
                SignClient(stringBuilder.ToString(), workingDirectory: RootDirectory, timeout: TimeSpan.FromMinutes(5).Minutes);
            }
        });
    private AbsolutePath[] GetDockerProjects()
    {
        return SourceDirectory.GlobFiles("**/Dockerfile")// folders with Dockerfiles in it
            .ToArray();
    }
    Target PublishCode => _ => _
        .Executes(() =>
        {
            var dockfiles = GetDockerProjects();
            foreach(var dockfile in dockfiles)
            {
                var (path, projectName) = ($"{Directory.GetParent(dockfile).FullName}", $"{Directory.GetParent(dockfile).Name}".ToLower());
                var project = Path.Combine(path, $"{projectName}.csproj");
                DotNetPublish(s => s
                .SetProject(project)
                .SetConfiguration(Configuration.Release));
            }            
        });
   Target All => _ => _
    .DependsOn(CreateNuget);
   Target NBench => _ => _
    .DependsOn(Compile)
    //.WhenSkipped(DependencyBehavior.Skip)
    .Executes(() => 
    {
        RootDirectory
            .GlobFiles("src/**/*.Tests.Performance.csproj")
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
        .Executes(() =>
        {
            DotNet($@"dotnet tool install SignClient --version 1.3.155 --tool-path ""{ToolsDir}"" ");
            DotNet($"tool install Nuke.GlobalTool --global");
        });


    static void Information(string info)
    {
        Serilog.Log.Information(info);
    }
}
