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
using Nuke.Common.Tools.SignClient;

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
    readonly Configuration Configuration = Configuration.Release;

    [Parameter("The final release branch")]
    readonly string ReleaseBranch = "master";

    [Solution] readonly Solution Solution;
    [GitRepository] readonly GitRepository GitRepository;
    [GitVersion(Framework = "net6.0")] readonly GitVersion GitVersion;

    [Parameter] string NugetPublishUrl = "https://api.nuget.org/v3/index.json";
    [Parameter] [Secret] string NugetKey;

    [Parameter] string SymbolsPublishUrl;

    [Parameter] string DockerRegistryUrl;

    // Metadata used when signing packages and DLLs
    [Parameter] string SigningName = "My Library";
    [Parameter] string SigningDescription = "My REALLY COOL Library";
    [Parameter] string SigningUrl = "https://signing.is.cool/";

    
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
        .Description("Cleans all the output directories")
        .Before(Restore)
        .Executes(() =>
        {
            RootDirectory
            .GlobDirectories("src/**/bin", "src/**/obj", Output, OutputTests, OutputPerfTests, OutputNuget, DocSiteDirectory)
            .ForEach(DeleteDirectory);
            EnsureCleanDirectory(Output);
        });

    Target Restore => _ => _
        .Description("Restores all nuget packages")
        .DependsOn(Clean)
        .Executes(() =>
        {
            DotNetRestore(s => s
                .SetProjectFile(Solution));
        });
    IEnumerable<string> ChangelogSectionNotes => ExtractChangelogSectionNotes(ChangelogFile);

    Target RunChangelog => _ => _
        .OnlyWhenDynamic(()=> GitVersion.BranchName == ReleaseBranch)
        .Description("Updates the release notes and version number in the `ChangeLog.md`")
        .Executes(() =>
        {
            FinalizeChangelog(ChangelogFile, GitVersion.MajorMinorPatch, GitRepository);

            Git($"add {ChangelogFile}");
            Git($"commit -m \"Finalize {Path.GetFileName(ChangelogFile)} for {GitVersion.MajorMinorPatch}.\"");

            //To sign your commit
            //Git($"commit -S -m \"Finalize {Path.GetFileName(ChangelogFile)} for {vNext}.\"");

            Git($"tag -f {GitVersion.MajorMinorPatch}");
        });
    Target Nuget => _ => _
      .Description("Creates nuget packages")
      .Before(SignClient, PublishNuget)      
      .DependsOn(Tests)
      .Executes(() =>
      {
          var version = GitVersion.SemVer;
          var branchName = GitVersion.BranchName;

          if(branchName.Equals(ReleaseBranch, StringComparison.OrdinalIgnoreCase) 
          && !GitVersion.MajorMinorPatch.Equals(LatestVersion.Version.ToString()))
          {
              // Force CHANGELOG.md in case it skipped the mind
              Assert.Fail($"CHANGELOG.md needs to be update for final release. Current version: '{LatestVersion.Version}'. Next version: {GitVersion.MajorMinorPatch}");
          }
          var releaseNotes = branchName.Equals(ReleaseBranch, StringComparison.OrdinalIgnoreCase) 
                             ? GetNuGetReleaseNotes(ChangelogFile, GitRepository) 
                             : ParseReleaseNote();

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
                  .SetAssemblyVersion(version)
                  .SetFileVersion(version)
                  .SetVersion(version)
                  .SetPackageReleaseNotes(releaseNotes)
                  .SetDescription("YOUR_DESCRIPTION_HERE")
                  .SetPackageProjectUrl("YOUR_PACKAGE_URL_HERE")
                  .SetOutputDirectory(OutputNuget));
          }
      });
    Target DockerLogin => _ => _
        .Description("Docker login command")
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
        .Description("Build docker image")
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
        .Description("Push image to docker registry")
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
    .Description("Publishes .nuget packages to Nuget")
    .Requires(() => NugetPublishUrl)
    .Requires(() => !NugetKey.IsNullOrEmpty())
    .Executes(() =>
    {
        var packages = OutputNuget.GlobFiles("*.nupkg", "*.symbols.nupkg").NotNull();
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
    Target Tests => _ => _
        .Description("Runs all the unit tests")
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
    Target SignClient => _ => _
        .Unlisted()
        .Before(PublishNuget)
        .Requires(() => !SignClientSecret.IsNullOrEmpty())
        .Requires(() => !SignClientUser.IsNullOrEmpty())
        .Executes(() =>
        {
            var assemblies = OutputNuget.GlobFiles("*.nupkg");
            foreach (var asm in assemblies)
            {
                SignClientSign(s => s
                .SetProcessToolPath(ToolsDir / "SignClient.exe")
                .SetProcessLogOutput(true)
                .SetConfig(RootDirectory / "appsettings.json")
                .SetDescription(SigningDescription)
                .SetDescriptionUrl(SigningUrl)
                .SetInput(asm)
                .SetName(SigningName)
                .SetSecret(SignClientSecret)
                .SetUsername(SignClientUser)
                .SetProcessWorkingDirectory(RootDirectory)
                .SetProcessExecutionTimeout(TimeSpan.FromMinutes(5).Minutes));

                //SignClient(stringBuilder.ToString(), workingDirectory: RootDirectory, timeout: TimeSpan.FromMinutes(5).Minutes);
            }
        });
    Target SignPackages => _ => _
        .DependsOn(Nuget, SignClient);

    Target SignAndPublishPackage => _ => _
         .Description("Sign and publish nuget packages")
         .DependsOn(SignPackages, PublishNuget);
    private AbsolutePath[] GetDockerProjects()
    {
        return SourceDirectory.GlobFiles("**/Dockerfile")// folders with Dockerfiles in it
            .ToArray();
    }
    Target PublishCode => _ => _
        .Unlisted()
        .Description("Publish project as release")
        .DependsOn(Tests)
        .Executes(() =>
        {
            var dockfiles = GetDockerProjects();
            foreach(var dockfile in dockfiles)
            {
                Information(dockfile.Parent.ToString());
                var project = dockfile.Parent.GlobFiles("*.csproj").First();
                DotNetPublish(s => s
                .SetProject(project)
                .SetConfiguration(Configuration.Release));
            }            
        });
   Target All => _ => _
    .Description("Executes NBench, Tests and Nuget targets/commands")
    .DependsOn(Nuget, NBench);

    Target NBench => _ => _
    .Description("Runs all BenchMarkDotNet tests")
    .DependsOn(Compile)
    .Executes(() =>
    {
        RootDirectory
            .GlobFiles("src/**/*.Tests.Performance.csproj")
            .ForEach(path =>
            {
                DotNetRun(s => s
                .SetApplicationArguments($"--no-build -c release --concurrent true --trace true --output {OutputPerfTests} --diagnostic")
                .SetProcessLogOutput(true)
                .SetProcessWorkingDirectory(Directory.GetParent(path).FullName)
                .SetProcessExecutionTimeout((int)TimeSpan.FromMinutes(30).TotalMilliseconds)
                );
            });
    });
    //--------------------------------------------------------------------------------
    // Documentation 
    //--------------------------------------------------------------------------------
    Target DocsInit => _ => _
        .Unlisted()
        .DependsOn(Compile)
        .Executes(() =>
        {
            DocFXInit(s => s.SetOutputFolder(DocFxDir).SetQuiet(true));
        });
    Target DocsMetadata => _ => _
        .Unlisted()
        .Description("Create DocFx metadata")
        .DependsOn(Compile)
        .Executes(() => 
        {
            DocFXMetadata(s => s
            .SetProjects(DocFxDirJson)
            .SetLogLevel(DocFXLogLevel.Verbose));
        });

    Target DocFxBuild => _ => _
        .Description("Builds Documentation")
        .DependsOn(DocsMetadata)
        .Executes(() => 
        {
            DocFXBuild(s => s
            .SetConfigFile(DocFxDirJson)
            .SetLogLevel(DocFXLogLevel.Verbose));
        });

    Target BuildAndServeDocs => _ => _
    .DependsOn(DocFxBuild, ServeDocs);

    Target ServeDocs => _ => _
        .Description("Build and preview documentation")
        .Executes(() => DocFXServe(s=>s.SetFolder(DocFxDir)));

    Target Compile => _ => _
        .Description("Builds all the projects in the solution")
        .DependsOn(Restore)
        .Executes(() =>
        {
            var version = GitVersion.MajorMinorPatch;
            DotNetBuild(s => s
                .SetProjectFile(Solution)
                .SetConfiguration(Configuration)
                .SetAssemblyVersion(version)
                .SetFileVersion(version)
                .SetVersion(GitVersion.SemVer)
                .EnableNoRestore());
        });

    Target Install => _ => _
        .Description("Install `Nuke.GlobalTool` and SignClient")
        .Executes(() =>
        {
            DotNet($@"dotnet tool install SignClient --version 1.3.155 --tool-path ""{ToolsDir}"" ");
            DotNet($"tool install Nuke.GlobalTool --global");
        });
    string ParseReleaseNote()
    {
        return XmlTasks.XmlPeek(SourceDirectory / "Directory.Build.props", "//Project/PropertyGroup/PackageReleaseNotes").FirstOrDefault();
    }
    
    static void Information(string info)
    {
        Serilog.Log.Information(info);
    }
}
