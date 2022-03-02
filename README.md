﻿# Petabridge.App.Web

Update this readme file with your details.

# The Build System

This build system is powered by [NUKE](https://nuke.build/); please see their [API documentation](https://nuke.build/docs/getting-started/philosophy.html) should you need to make any changes to the `Build.cs` file.

To install Nuke GlobalTool and SignClient, execute the following command at the root of this folder:

```
build.cmd
```

## GitHub Actions `yml` auto-generation

You can define your GitHub workflows in code and Nuke will generate the YAML files for you.

You can update or add to what exist in `Build.CI.GitHubActions.cs` (`AutoGenerate` has to be set to true):

```csharp
[CustomGitHubActions("pr_validation",
    GitHubActionsImage.WindowsLatest,
    GitHubActionsImage.UbuntuLatest,
    AutoGenerate = true,
    OnPushBranches = new[] { "master", "dev" },
    OnPullRequestBranches = new[] { "master", "dev" },
    InvokedTargets = new[] { nameof(RunTests) },
    PublishArtifacts = true,
    EnableGitHubContext = true)
]

[CustomGitHubActions("Docker_build",
    GitHubActionsImage.UbuntuLatest,
    AutoGenerate = true,
    OnPushBranches = new[] { "master", "dev" },
    OnPullRequestBranches = new[] { "master", "dev" },
    InvokedTargets = new[] { nameof(Docker) },
    ImportSecrets = new [] { "Docker_Username", "Docker_Password" },
    PublishArtifacts = true,
    EnableGitHubContext = true)
]
[CustomGitHubActions("Windows_release",
    GitHubActionsImage.WindowsLatest,
    AutoGenerate = true,
    OnPushBranches = new[] { "refs/tags/*" },
    InvokedTargets = new[] { nameof(Nuget) },
    ImportSecrets = new[] { "Nuget_Key" }, 
    PublishArtifacts = true,
    EnableGitHubContext = true)
]
```
To generate or update existing workflow yaml file(s), execute any of the commands (e.g. `build.cmd compile`):

```shell
PS C:\Users\User\source\repos\Petabridge.App.Web> .\build.cmd compile
PowerShell Desktop version 5.1.19041.1320
Microsoft (R) .NET Core SDK version 6.0.101
11:42:25 [INF] Creating directory C:\Users\User\source\repos\Petabridge.App.Web\.github\workflows...
11:42:25 [INF] Creating directory C:\Users\User\source\repos\Petabridge.App.Web\.github\workflows...
11:42:25 [INF] Creating directory C:\Users\User\source\repos\Petabridge.App.Web\.github\workflows...
11:42:28 [WRN] Configuration files for GitHubActions (Windows_release) have changed.
11:42:28 [WRN] Configuration files for GitHubActions (pr_validation) have changed.
11:42:28 [WRN] Configuration files for GitHubActions (Docker_build) have changed.
Press any key to continue ...​

███╗   ██╗██╗   ██╗██╗  ██╗███████╗
████╗  ██║██║   ██║██║ ██╔╝██╔════╝
██╔██╗ ██║██║   ██║█████╔╝ █████╗  
██║╚██╗██║██║   ██║██╔═██╗ ██╔══╝  
██║ ╚████║╚██████╔╝██║  ██╗███████╗
╚═╝  ╚═══╝ ╚═════╝ ╚═╝  ╚═╝╚══════╝
```

## Supported Build System Commands

This project comes with some ready-made commands, all of which can be listed via:

```
 build.cmd help
```
If you desire to add more commands, please see the [Fundamentals](https://nuke.build/docs/authoring-builds/fundamentals.html).

### Summary

The ready-made commands you can start working with (both on **Windows** and **Linux**), are detailed as follows:

* `build.cmd all` - runs the following commands: `NBench`, `Tests`, and `Nuget`.
* `build.cmd compile` - compiles the solution in `Release` mode. The default mode is `Release`, to compile in `Debug` mode => `--configuration debug`
* `build.cmd runtests` - compiles the solution in `Release` mode and runs the unit test suite (all projects that end with the `.Tests.csproj` suffix). All of the output will be published to the `./TestResults` folder.
* `build.cmd nbench` - compiles the solution in `Release` mode and runs the [NBench](https://nbench.io/) performance test suite (all projects that end with the `.Tests.Performance.csproj` suffix). All of the output will be published to the `./PerfResults` folder.
* `build.cmd nuget` - compiles the solution in `Release` mode and creates Nuget packages from any project that does not have `<IsPackable>false</IsPackable>` set and uses the version number from `GitVersion.SemVer`.
* `build.cmd signpackages --SignClientUser $(signingUsername) --SignClientSecret $(signingPassword)` - compiles the solution in `Release` modem creates Nuget packages from any project that does not have `<IsPackable>false</IsPackable>` set using the version number from `GitVersion.SemVer`, and then signs those packages using the SignClient data below.
* `build.cmd signandpublishpackage --SignClientUser $(signingUsername) --SignClientSecret $(signingPassword) --nugetpublishurl $(nugetUrl) --nugetkey $(nugetKey)` - compiles the solution in `Release` modem creates Nuget packages from any project that does not have `<IsPackable>false</IsPackable>` set using the version number from `GitVersion.SemVer`, signs those packages using the SignClient data below, and then publishes those packages to the `$(nugetUrl)` using NuGet key `$(nugetKey)`.
* `build.cmd docfxBuild` - compiles the solution in `Release` mode and then uses [DocFx](http://dotnet.github.io/docfx/) to generate website documentation inside the `./docs/_site` folder. Execute `build.cmd servedocs` after build is done to preview the documentation.
* `build.cmd buildAndServeDocs` - this combines `build.cmd docfxBuild` and ``build.cmd servedocs` in one go!

This build script is powered by [NUKE](https://nuke.build/); please see their API documentation should you need to make any changes to the [`build.cs`](/build/build.cs) file.

### Release Notes, Version Numbers, Etc
This project will automatically populate its release notes in all of its modules via the entries written inside [`RELEASE_NOTES.md`](RELEASE_NOTES.md) and will automatically update the versions of all assemblies and NuGet packages via the metadata included inside [`Directory.Build.props`](src/Directory.Build.props).

**RELEASE_NOTES.md**
```
#### [0.1.0] / October 05 2019 ####
First release
```

In this instance, the NuGet and assembly version will be `0.1.0` based on what's available at the top of the `RELEASE_NOTES.md` file.

**RELEASE_NOTES.md**
```
#### [0.1.0] / October 05 2019 ####
First release
```
But in this case the NuGet and assembly version will be `0.1.0`.

### Deployment
Petabridge.App.Web uses Docker for deployment - to create Docker images for this project, please run the following command:

```
build.cmd Docker
```

By default `Docker` will look for every `.csproj` file that has a `Dockerfile` in the same directory - from there the name of the `.csproj` will be converted into [the supported Docker image name format](https://docs.docker.com/engine/reference/commandline/tag/#extended-description), so "Petabridge.App.Web.csproj" will be converted to an image called `petabridge.app.web:latest` and `petabridge.app.web:{VERSION}`, where version is determined using the rules defined in the section below.

#### Pushing to a Remote Docker Registry
You can also specify a remote Docker registry URL and that will cause a copy of this Docker image to be published there as well:

### Conventions
The attached build script will automatically do the following based on the conventions of the project names added to this project:

* Any project name ending with `.Tests` will automatically be treated as a [XUnit2](https://xunit.github.io/) project and will be included during the test stages of this build script;
* Any project name ending with `.Tests.Performance` will automatically be treated as a [NBench](https://github.com/petabridge/NBench) project and will be included during the test stages of this build script; and
* Any project meeting neither of these conventions will be treated as a NuGet packaging target and its `.nupkg` file will automatically be placed in the `bin\nuget` folder upon running the `build.cmd all` command.

### DocFx for Documentation
This solution also supports [DocFx](http://dotnet.github.io/docfx/) for generating both API documentation and articles to describe the behavior, output, and usages of your project. 

All of the relevant articles you wish to write should be added to the `/docs/articles/` folder and any API documentation you might need will also appear there.

All of the documentation will be statically generated and the output will be placed in the `/docs/_site/` folder. 

#### Previewing Documentation
To preview the documentation for this project, execute the following command at the root of this folder:

```
build.cmd servedocs
```

This will use the built-in `docfx.console` binary that is installed as part of the NuGet restore process from executing the above command to preview the fully-rendered documentation. For best results, do this immediately after calling `build.cmd compile`.

### Code Signing via SignService
This project uses [SignService](https://github.com/onovotny/SignService) to code-sign NuGet packages prior to publication. The `build.cmd` scripts will automatically download the `SignClient` needed to execute code signing locally on the build agent, but it's still your responsibility to set up the SignService server per the instructions at the linked repository.

Once you've gone through the ropes of setting up a code-signing server, you'll need to set a few configuration options in your project in order to use the `SignClient`:

* Add your Active Directory settings to [`appsettings.json`](appsettings.json) and
* Pass in your signature information to the `SigningName`, `SigningDescription`, and `SigningUrl` values inside `build.cs`.

Whenever you're ready to run code-signing on the NuGet packages published by `build.cs`, execute the following command:

```
build.cmd signpackages --SignClientSecret {your secret} --SignClientUser {your username}
```

This will invoke the `SignClient` and actually execute code signing against your `.nupkg` files prior to NuGet publication.