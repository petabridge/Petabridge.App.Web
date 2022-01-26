# Petabridge.App.Web

Update this readme file with your details.

# The Build System

This build system is powered by [NUKE](https://nuke.build/); please see their [API documentation](https://nuke.build/docs/getting-started/philosophy.html) should you need to make any changes to the [`Build.cs`](build\Build.cs) file.

To install Nuke GlobalTool and SignClient:

```
build.cmd install
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
* `build.cmd tests` - compiles the solution in `Release` mode and runs the unit test suite (all projects that end with the `.Tests.csproj` suffix). All of the output will be published to the `./TestResults` folder.
* `build.cmd nbench` - compiles the solution in `Release` mode and runs the [NBench](https://nbench.io/) performance test suite (all projects that end with the `.Tests.Performance.csproj` suffix). All of the output will be published to the `./PerfResults` folder.
* `build.cmd nuget` - compiles the solution in `Release` mode and creates Nuget packages from any project that does not have `<IsPackable>false</IsPackable>` set and uses the version number from `GitVersion.SemVer`.
* `build.cmd nuget --SignClientUser $(signingUsername) --SignClientSecret $(signingPassword)` - compiles the solution in `Release` modem creates Nuget packages from any project that does not have `<IsPackable>false</IsPackable>` set using the version number from `GitVersion.SemVer`, and then signs those packages using the SignClient data below.
* `build.cmd nuget --SignClientUser $(signingUsername) --SignClientSecret $(signingPassword) --nugetpublishurl $(nugetUrl) --nugetkey $(nugetKey)` - compiles the solution in `Release` modem creates Nuget packages from any project that does not have `<IsPackable>false</IsPackable>` set using the version number from `GitVersion.SemVer`, signs those packages using the SignClient data below, and then publishes those packages to the `$(nugetUrl)` using NuGet key `$(nugetKey)`.
* `build.cmd DocFxBuild` - compiles the solution in `Release` mode and then uses [DocFx](http://dotnet.github.io/docfx/) to generate website documentation inside the `./docs/_site` folder. 
* `build.cmd ServeDocs` - To build and preview the documentation.

### Version Management
* `build.[cmd|sh] nuget nugetprerelease=dev` - compiles the solution in `Release` mode and creates Nuget packages from any project that does not have `<IsPackable>false</IsPackable>` set - but in this instance all projects will have a `VersionSuffix` of `-beta{DateTime.UtcNow.Ticks}`. It's typically used for publishing nightly releases.

### Deployment
Petabridge.App uses Docker for deployment - to create Docker images for this project, please run the following command:

```
build.cmd Docker
```

By default `build.fsx` will look for every `.csproj` file that has a `Dockerfile` in the same directory - from there the name of the `.csproj` will be converted into [the supported Docker image name format](https://docs.docker.com/engine/reference/commandline/tag/#extended-description), so "Petabridge.App.csproj" will be converted to an image called `petabridge.app:latest` and `petabridge.app:{VERSION}`, where version is determined using the rules defined in the section below.

#### Pushing to a Remote Docker Registry
You can also specify a remote Docker registry URL and that will cause a copy of this Docker image to be published there as well:

### Release Notes, Version Numbers, Etc
This project will automatically populate its release notes in all of its modules via the entries written inside [`RELEASE_NOTES.md`](RELEASE_NOTES.md) and will automatically update the versions of all assemblies and NuGet packages via the metadata included inside [`common.props`](src/common.props).

**RELEASE_NOTES.md**
```
#### 0.1.0 October 05 2019 ####
First release
```

In this instance, the NuGet and assembly version will be `0.1.0` based on what's available at the top of the `RELEASE_NOTES.md` file.

**RELEASE_NOTES.md**
```
#### 0.1.0-beta1 October 05 2019 ####
First release
```

But in this case the NuGet and assembly version will be `0.1.0-beta1`.


### Conventions
The attached build script will automatically do the following based on the conventions of the project names added to this project:

* Any project name ending with `.Tests` will automatically be treated as a [XUnit2](https://xunit.github.io/) project and will be included during the test stages of this build script;
* Any project name ending with `.Tests.Performance` will automatically be treated as a [NBench](https://github.com/petabridge/NBench) project and will be included during the test stages of this build script; and
* Any project meeting neither of these conventions will be treated as a NuGet packaging target and its `.nupkg` file will automatically be placed in the `bin\nuget` folder upon running the `build.[cmd|sh] all` command.

### DocFx for Documentation
This solution also supports [DocFx](http://dotnet.github.io/docfx/) for generating both API documentation and articles to describe the behavior, output, and usages of your project. 

All of the relevant articles you wish to write should be added to the `/docs/articles/` folder and any API documentation you might need will also appear there.

All of the documentation will be statically generated and the output will be placed in the `/docs/_site/` folder. 

#### Previewing Documentation
To preview the documentation for this project, execute the following command at the root of this folder:

```
C:\> serve-docs.cmd
```

This will use the built-in `docfx.console` binary that is installed as part of the NuGet restore process from executing any of the usual `build.cmd` or `build.sh` steps to preview the fully-rendered documentation. For best results, do this immediately after calling `build.cmd buildRelease`.

### Code Signing via SignService
This project uses [SignService](https://github.com/onovotny/SignService) to code-sign NuGet packages prior to publication. The `build.cmd` and `build.sh` scripts will automatically download the `SignClient` needed to execute code signing locally on the build agent, but it's still your responsibility to set up the SignService server per the instructions at the linked repository.

Once you've gone through the ropes of setting up a code-signing server, you'll need to set a few configuration options in your project in order to use the `SignClient`:

* Add your Active Directory settings to [`appsettings.json`](appsettings.json) and
* Pass in your signature information to the `signingName`, `signingDescription`, and `signingUrl` values inside `build.fsx`.

Whenever you're ready to run code-signing on the NuGet packages published by `build.fsx`, execute the following command:

```
C:\> build.cmd nuget SignClientSecret={your secret} SignClientUser={your username}
```

This will invoke the `SignClient` and actually execute code signing against your `.nupkg` files prior to NuGet publication.

If one of these two values isn't provided, the code signing stage will skip itself and simply produce unsigned NuGet code packages.