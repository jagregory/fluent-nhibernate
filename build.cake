#addin "Cake.FileHelpers"
#tool "nuget:?package=NUnit.ConsoleRunner&version=3.7.0"
#tool "nuget:?package=Machine.Specifications.Runner.Console&version=0.9.3"
#tool "nuget:?package=GitReleaseManager&version=0.5.0"
#tool "nuget:?package=GitVersion.CommandLine&version=3.6.2"

#load "./build/parameters.cake"

BuildParameters parameters = BuildParameters.GetParameters(Context);
DotNetCoreMSBuildSettings msBuildSettings = null;
bool publishingError = false;

var SolutionPath = "./src/FluentNHibernate.sln";
var SrcProjects = new [] { "FluentNHibernate" };
var TestProjects = new [] { "FluentNHibernate.Testing" };
var SpecProjects = new [] { "FluentNHibernate.Specs" };

Setup((context) =>
{    
    parameters.Initialize(context);

    Information("FluentNHibernate");
    Information($"SemVersion: {parameters.Version.SemVersion}");
    Information($"IsLocalBuild: {parameters.IsLocalBuild}");    
    Information($"IsTagged: {parameters.IsTagged}");
    Information($"IsPullRequest: {parameters.IsPullRequest}");
    Information($"Target: {parameters.Target}");        

    var releaseNotes = string.Join("\n", 
        parameters.ReleaseNotes.Notes.ToArray()).Replace("\"", "\"\"");

    msBuildSettings = new DotNetCoreMSBuildSettings()
        .WithProperty("Version", parameters.Version.SemVersion)
        .WithProperty("AssemblyVersion", parameters.Version.Version)
        .WithProperty("FileVersion", parameters.Version.Version)
        .WithProperty("PackageReleaseNotes", string.Concat("\"", releaseNotes, "\""));
});

Teardown((context) =>
{
});

Task("Clean")
	.Does(() =>
    {
        CleanDirectories(parameters.Paths.Directories.ToClean);        
        CleanProjects("src", SrcProjects);
        CleanProjects("src", TestProjects);
        CleanProjects("src", SpecProjects);         
        EnsureDirectoryExists(parameters.Paths.Directories.Artifacts);
        EnsureDirectoryExists(parameters.Paths.Directories.ArtifactsBinFullFx);
        EnsureDirectoryExists(parameters.Paths.Directories.TestResults);
        EnsureDirectoryExists(parameters.Paths.Directories.NugetRoot);
	});

Task("Restore")
    .IsDependentOn("Clean")
    .Does(() =>
    {
        DotNetCoreRestore(SolutionPath, new DotNetCoreRestoreSettings
        {
            Verbosity = DotNetCoreVerbosity.Minimal,            
        });
    });

Task("Build")
    .IsDependentOn("Restore")
    .Does(() =>
    {
        BuildProjects("src", SrcProjects, parameters.Configuration, msBuildSettings);
        BuildProjects("src", TestProjects, parameters.Configuration, msBuildSettings); 
        BuildProjects("src", SpecProjects, parameters.Configuration, msBuildSettings); 
    });

Task("Test")
    .IsDependentOn("Build")
    .Does(() =>
    {          
        var runtime = "net461";
        var testAssemblies = $"./src/**/bin/{parameters.Configuration}/{runtime}/*.Testing.dll";  
        NUnit3(testAssemblies, new NUnit3Settings {
            NoResults = true
        });

        testAssemblies = $"./src/**/bin/{parameters.Configuration}/{runtime}/*.Specs.dll";  
        MSpec(testAssemblies, new MSpecSettings {
            Silent = true
        });
    });


Task("Copy-Files")
    .IsDependentOn("Test")
    .Does(() =>
    {            
        PublishProjects(
            SrcProjects, "net461",
            parameters.Paths.Directories.ArtifactsBinFullFx.FullPath, 
            parameters.Version.DotNetAsterix, 
            parameters.Configuration, 
            msBuildSettings
        );
        PublishProjects(
            SrcProjects, "netstandard2.0",
            parameters.Paths.Directories.ArtifactsBinNetStandard20.FullPath, 
            parameters.Version.DotNetAsterix, 
            parameters.Configuration, 
            msBuildSettings
        );
        PublishProjects(
            SrcProjects, "netcoreapp2",
            parameters.Paths.Directories.ArtifactsBinNetCoreApp2.FullPath, 
            parameters.Version.DotNetAsterix, 
            parameters.Configuration, 
            msBuildSettings
        );
        
        CopyFileToDirectory("./LICENSE", parameters.Paths.Directories.ArtifactsBinFullFx);            
    });

Task("Zip-Files")
    .IsDependentOn("Copy-Files")
    .Does(() =>
    {            
        Zip(parameters.Paths.Directories.ArtifactsBinFullFx, parameters.Paths.Files.ZipArtifactPathDesktop, 
            GetFiles($"{parameters.Paths.Directories.ArtifactsBinFullFx.FullPath}/**/*"));
    });
  
Task("Create-NuGet-Packages")
    .IsDependentOn("Copy-Files")
    .Does(() =>
    {                
        PackProjects(
            SrcProjects, 
            parameters.Paths.Directories.NuspecRoot.FullPath, 
            parameters.Paths.Directories.NugetRoot.FullPath, 
            parameters.Paths.Directories.ArtifactsBin.FullPath, 
            parameters.Version.SemVersion);
    });

Task("Publish-Nuget")
    .IsDependentOn("Create-NuGet-Packages")
    .WithCriteria(() => parameters.ShouldPublish)
    .Does(() =>
    {        
        foreach(var project in SrcProjects)
        {            
            var packagePath = parameters.Paths.Directories.NugetRoot
                .CombineWithFilePath(string.Concat(project, ".", parameters.Version.SemVersion, ".nupkg"));
            NuGetPush(packagePath, new NuGetPushSettings {
                Source = parameters.NuGet.ApiUrl,
                ApiKey = parameters.NuGet.ApiKey
            });
        }
   });    

Task("Generate-Docs")   
    .IsDependentOn("Build")
    .Does(() =>
    {
        // TODO  build/docu/docu.exe...  and publish to gh-pages     
    });

Task("Publish-GitHub-Release")
    .WithCriteria(() => parameters.ShouldPublish)
    .Does(() =>
    {
        GitReleaseManagerAddAssets(
            parameters.GitHub.UserName, parameters.GitHub.Password, 
            parameters.GitHub.Owner, parameters.GitHub.Repository, 
            parameters.Version.Milestone, 
            parameters.Paths.Files.ZipArtifactPathDesktop.ToString());
        GitReleaseManagerClose(
            parameters.GitHub.UserName, parameters.GitHub.Password, 
            parameters.GitHub.Owner, parameters.GitHub.Repository, 
            parameters.Version.Milestone);
    })
    .OnError(exception =>
    {
        Information("Publish-GitHub-Release Task failed, but continuing with next Task...");
        publishingError = true;
    });

Task("Create-Release-Notes")
    .Does(() =>
    {
        GitReleaseManagerCreate(
            parameters.GitHub.UserName, parameters.GitHub.Password, 
            parameters.GitHub.Owner, parameters.GitHub.Repository, 
            new GitReleaseManagerCreateSettings {
                Milestone         = parameters.Version.Milestone,
                Name              = parameters.Version.Milestone,
                Prerelease        = true,
                TargetCommitish   = "master"
            }
        );
    });

Task("Update-AppVeyor-BuildNumber")
    .WithCriteria(() => parameters.IsRunningOnAppVeyor)
    .Does(() =>
    {
        // AppVeyor.UpdateBuildVersion(parameters.Version.SemVersion);
    })
    .ReportError(exception =>
    {
        // Via: See https://github.com/reactiveui/ReactiveUI/issues/1262
        Warning("Build with version {0} already exists.", parameters.Version.SemVersion);
    });    

Task("Upload-AppVeyor-Artifacts")        
    .WithCriteria(() => parameters.IsRunningOnAppVeyor)
    .Does(() =>
    {
        AppVeyor.UploadArtifact(parameters.Paths.Files.ZipArtifactPathDesktop);    
        foreach(var package in GetFiles(parameters.Paths.Directories.NugetRoot + "/*"))
        {
            AppVeyor.UploadArtifact(package);
        }
    });

Task("Release-Notes")
  .IsDependentOn("Create-Release-Notes");

Task("Package")
    .IsDependentOn("Zip-Files")
    .IsDependentOn("Create-NuGet-Packages");  

Task("AppVeyor")
    .IsDependentOn("Update-AppVeyor-BuildNumber")
    .IsDependentOn("Package")
    .IsDependentOn("Upload-AppVeyor-Artifacts")     
    .IsDependentOn("Publish-NuGet")
    .IsDependentOn("Publish-GitHub-Release")
    .Finally(() =>
    {
        if(publishingError)
        {
            throw new Exception("An error occurred during the publishing of Cake.  All publishing tasks have been attempted.");
        }
    });
    
Task("Default")
    .IsDependentOn("Package");
    
RunTarget(parameters.Target);

private void CleanProjects(string projectKind, IEnumerable<string> projectNames)
{
    foreach(var project in projectNames)
    {
        CleanDirectories($"./{projectKind}/{project}/bin/**");
        CleanDirectories($"./{projectKind}/{project}/obj/**");
    }
}

private void BuildProjects(
    string projectKind, 
    IEnumerable<string> projectNames,
    string configuration, 
    DotNetCoreMSBuildSettings msBuildSettings)
{
    foreach(var project in projectNames)
    {
        var projectPath = File($"./{projectKind}/{project}/{project}.csproj");
        DotNetCoreBuild(projectPath, new DotNetCoreBuildSettings()
        {
            Configuration = configuration,
            MSBuildSettings = msBuildSettings
        });
    }
}

private void PublishProjects(
    IEnumerable<string> projectNames,
    string framework,
    string artifactsBin,
    string versionSuffix,
    string configuration, 
    DotNetCoreMSBuildSettings msBuildSettings)
{
    foreach(var project in projectNames)
    {        
        DotNetCorePublish($"./src/{project}", new DotNetCorePublishSettings
        {
            Framework = framework,
            VersionSuffix = versionSuffix,
            Configuration = configuration,
            OutputDirectory = artifactsBin,
            MSBuildSettings = msBuildSettings
        });
     
        // Copy documentation XML (since publish does not do this anymore)
        CopyFileToDirectory($"./src/{project}/bin/{configuration}/{framework}/{project}.xml", artifactsBin);    
    }
}

private void PackProjects(
    IEnumerable<string> projectNames, 
    string nuspecDir,
    string nugetDir,
    string basePath,
    string semVersion)
{
    foreach(var project in projectNames)
    {
        // symbols
        NuGetPack($"{nuspecDir}/{project}.symbols.nuspec", new NuGetPackSettings {
            Version = semVersion,
            // ReleaseNotes = releaseNotes.Notes.ToArray(),
            BasePath = basePath,
            OutputDirectory = nugetDir,
            Symbols = true,
            NoPackageAnalysis = true
        });

        var fullBasePath = MakeAbsolute((FilePath)basePath).FullPath;
        var fullBasePathLength = fullBasePath.Length + 1;
        
        // normal
        NuGetPack($"{nuspecDir}/{project}.nuspec", new NuGetPackSettings {
            Version = semVersion,
            // ReleaseNotes = releaseNotes.Notes.ToArray(),
            BasePath = fullBasePath,
            OutputDirectory = nugetDir,
            Symbols = false,
            NoPackageAnalysis = true
            // Files = GetFiles(fullBasePath + "/**/*")
            //     .Where(file => file.FullPath.IndexOf("/runtimes/", StringComparison.OrdinalIgnoreCase) < 0)
            //     .Select(file => file.FullPath.Substring(fullBasePathLength))
            //     .Select(file => 
            //         new NuSpecContent 
            //         { 
            //             Source = file, 
            //             Target = file 
            //         })
            //     .ToArray()
        });
    }
}