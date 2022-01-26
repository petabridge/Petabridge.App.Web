﻿// Copyright 2021 Maintainers of NUKE.
// Distributed under the MIT License.
// https://github.com/nuke-build/nuke/blob/master/LICENSE

using System;
using System.Collections.Generic;
using Nuke.Common.CI.GitHubActions;
using Nuke.Common.CI.GitHubActions.Configuration;
using Nuke.Common.Execution;
using Nuke.Common.Utilities;

[CustomGitHubActions("pr_validation",
    GitHubActionsImage.WindowsLatest,
    GitHubActionsImage.UbuntuLatest,
    AutoGenerate = true,
    OnPushBranches = new[] { "master", "dev" },
    OnPullRequestBranches = new[] { "master", "dev" },
    CacheKeyFiles = new[] { "global.json", "src/**/*.csproj" },
    InvokedTargets = new[] { nameof(Tests) },
    //causes the on push to not trigger - maybe path-ignore is the right approach!
    //OnPushExcludePaths = new[] { "docs/**/*", "package.json", "README.md" },
    PublishArtifacts = false,
    EnableGitHubContext = true)
]

[CustomGitHubActions("Docker_build",
    GitHubActionsImage.UbuntuLatest,
    AutoGenerate = true,
    OnPushBranches = new[] { "master", "dev" },
    OnPullRequestBranches = new[] { "master", "dev" },
    CacheKeyFiles = new[] { "global.json", "src/**/*.csproj" },
    InvokedTargets = new[] { nameof(BuildImage) },
    ImportSecrets = new [] { "Docker_Username", "Docker_Password" },
    //causes the on push to not trigger - maybe path-ignore is the right approach!
    //OnPushExcludePaths = new[] { "docs/**/*", "package.json", "README.md" },
    EnableGitHubContext = true)
]
[CustomGitHubActions("Windows_release",
    GitHubActionsImage.WindowsLatest,
    AutoGenerate = true,
    OnPushBranches = new[] { "refs/tags/*" },
    CacheKeyFiles = new[] { "global.json", "src/**/*.csproj" },
    InvokedTargets = new[] { nameof(BuildImage) },
    ImportSecrets = new[] { "Nuget_Key" },
    //causes the on push to not trigger - maybe path-ignore is the right approach!
    //OnPushExcludePaths = new[] { "docs/**/*", "package.json", "README.md" },
    EnableGitHubContext = true)
]

partial class Build
{
}
class CustomGitHubActionsAttribute : GitHubActionsAttribute
{
    public CustomGitHubActionsAttribute(string name, GitHubActionsImage image, params GitHubActionsImage[] images) : base(name, image, images)
    {
    }

    protected override GitHubActionsJob GetJobs(GitHubActionsImage image, IReadOnlyCollection<ExecutableTarget> relevantTargets)
    {
        var job = base.GetJobs(image, relevantTargets);
        var newSteps = new List<GitHubActionsStep>(job.Steps);
        foreach (var version in new[] { "6.0.*", "5.0.*" })
        {
            newSteps.Insert(1, new GitHubActionsSetupDotNetStep
            {
                Version = version
            });
        }

        job.Steps = newSteps.ToArray();
        return job;
    }
}

class GitHubActionsSetupDotNetStep : GitHubActionsStep
{
    public string Version { get; init; }

    public override void Write(CustomFileWriter writer)
    {
        writer.WriteLine("- uses: actions/setup-dotnet@v1");

        using (writer.Indent())
        {
            writer.WriteLine("with:");
            using (writer.Indent())
            {
                writer.WriteLine($"dotnet-version: {Version}");
            }
        }
    }
}